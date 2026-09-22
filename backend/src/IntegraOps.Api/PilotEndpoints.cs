using IntegraOps.Application;
using IntegraOps.Infrastructure;

namespace IntegraOps.Api;

public sealed record ProjectRule(string Roles = "owner,operator,viewer", bool SessionOnly = true);
public sealed record ProjectNameInput(string Name);

public static class PilotEndpoints
{
    public static string Bearer(HttpContext c)
    {
        var value = c.Request.Headers.Authorization.ToString();
        return value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? value[7..] : "";
    }
    public static ProjectIdentity Actor(HttpContext c) => (ProjectIdentity)c.Items[typeof(ProjectIdentity)]!;
    private static SessionUser User(HttpContext c) => (SessionUser)c.Items[typeof(SessionUser)]!;

    public static async ValueTask<object?> ProjectFilter(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var c = invocation.HttpContext; var ct = c.RequestAborted;
        var store = c.RequestServices.GetRequiredService<PostgresIdentityStore>();
        SessionUser? user = null; var project = Guid.Empty;
        if (c.Request.Headers.ContainsKey("Authorization"))
        {
            user = await store.Session(Bearer(c), ct) ?? throw new PilotHttpException(401, "Oturum sona erdi. Tekrar giriş yapın.");
            if (!Guid.TryParse(c.Request.Headers["X-Project-Id"], out project))
                throw new PilotHttpException(400, "X-Project-Id gerekli.");
        }
        var actor = await store.Access(user?.Id, project, c.Request.Headers["X-API-Key"].ToString(), ct)
            ?? throw new PilotHttpException(user is null ? 401 : 403, "Bu projeye erişim doğrulanamadı.");
        var rule = c.GetEndpoint()?.Metadata.GetMetadata<ProjectRule>();
        if (actor.UserId is null)
        {
            var write = HttpMethods.IsPost(c.Request.Method);
            var path = c.Request.Path.Value;
            var allowed = (HttpMethods.IsGet(c.Request.Method) && actor.Scope is "legacy" or "read")
                || (write && path == "/api/v1/events" && actor.Scope is "legacy" or "ingest")
                || (write && path == "/api/v1/flows" && actor.Scope == "legacy");
            if (rule?.SessionOnly == true || !allowed) throw new PilotHttpException(403, "Bu anahtarın bu işlem için yetkisi yok.");
        }
        else
        {
            var roles = rule?.Roles ?? (HttpMethods.IsGet(c.Request.Method) ? "owner,operator,viewer" : "owner,operator");
            if (!roles.Split(',').Contains(actor.Role)) throw new PilotHttpException(403, "Bu işlem için yetkiniz yok.");
        }
        c.Items[typeof(ProjectIdentity)] = actor;
        c.Items[typeof(ProjectAccess)] = new ProjectAccess(actor.OrganizationId, actor.ProjectId);
        return await next(invocation);
    }

    public static void Map(WebApplication app, RouteGroupBuilder projectApi, Func<HttpContext, bool> bootstrapValid)
    {
        app.MapPost("/api/v1/bootstrap/owner", async (HttpContext c, FirstOwnerInput input,
            ITrackingStore tracking, PostgresIdentityStore store, CancellationToken ct) =>
        {
            if (!bootstrapValid(c)) throw new PilotHttpException(401, "Kurulum anahtarı doğrulanamadı.");
            var access = await tracking.AuthenticateAsync(c.Request.Headers["X-API-Key"].ToString(), ct)
                ?? throw new PilotHttpException(401, "Mevcut proje anahtarı gerekli.");
            await store.FirstOwner(access.ProjectId, input, ct);
            return Results.Ok(new { created = true });
        }).RequireRateLimiting("login");
        app.MapPost("/api/v1/auth/login", async (LoginInput input, PostgresIdentityStore store, CancellationToken ct)
            => Results.Ok(await store.Login(input, ct))).RequireRateLimiting("login");

        var me = app.MapGroup("/api/v1/me").AddEndpointFilter(async (invocation, next) =>
        {
            var c = invocation.HttpContext;
            var user = await c.RequestServices.GetRequiredService<PostgresIdentityStore>().Session(Bearer(c), c.RequestAborted)
                ?? throw new PilotHttpException(401, "Oturum sona erdi. Tekrar giriş yapın.");
            c.Items[typeof(SessionUser)] = user;
            return await next(invocation);
        });
        me.MapGet("/user", (HttpContext c) => Results.Ok(User(c)));
        me.MapGet("/projects", async (HttpContext c, PostgresIdentityStore s, CancellationToken ct) => Results.Ok(await s.Projects(User(c).Id, ct)));
        me.MapPost("/projects", async (HttpContext c, ProjectNameInput input, PostgresIdentityStore s, CancellationToken ct)
            => Results.Json(await s.CreateProject(User(c).Id, input.Name, ct), statusCode: 201));
        me.MapPost("/logout", async (HttpContext c, PostgresIdentityStore s, CancellationToken ct) =>
        { await s.Logout(Bearer(c), ct); return Results.NoContent(); });
        me.MapPost("/password", async (HttpContext c, PasswordInput input, PostgresIdentityStore s, CancellationToken ct) =>
        { await s.ChangePassword(User(c).Id, input, ct); return Results.NoContent(); });

        projectApi.MapGet("/access", (HttpContext c) => Results.Ok(Actor(c))).WithMetadata(new ProjectRule());
        projectApi.MapGet("/alarms", async (HttpContext c, PostgresPilotQueries q, string? state, string? flow, int? page,
            string? search, string? from, string? to, string? test, string? assigned, CancellationToken ct)
            => Results.Ok(await q.Alarms(Actor(c).ProjectId,
                PostgresPilotQueries.ParseAlarmFilter(state, flow, search, from, to, test, assigned, Actor(c).UserId), page ?? 1, ct)))
            .WithMetadata(new ProjectRule());
        projectApi.MapPost("/alarms/{id:guid}/assign", async (HttpContext c, Guid id, AssignInput input, PostgresPilotQueries q, CancellationToken ct) =>
        {
            var target = input.UserId == "me" ? Actor(c).UserId : string.IsNullOrEmpty(input.UserId) ? null
                : Guid.TryParse(input.UserId, out var parsed) ? parsed : throw new PilotHttpException(400, "Kullanıcı kimliği geçersiz.");
            await q.Assign(Actor(c), id, target, ct); return Results.NoContent();
        }).WithMetadata(new ProjectRule("owner,operator"));
        projectApi.MapPost("/alarms/{id:guid}/notes", async (HttpContext c, Guid id, NoteInput input, PostgresPilotQueries q, CancellationToken ct)
            => Results.Json(await q.AddNote(Actor(c), id, input.Body, ct), statusCode: 201)).WithMetadata(new ProjectRule("owner,operator"));
        projectApi.MapGet("/alarms/{id:guid}", async (HttpContext c, Guid id, PostgresPilotQueries q, CancellationToken ct)
            => Results.Ok(await q.Alarm(Actor(c).ProjectId, id, ct))).WithMetadata(new ProjectRule());
        projectApi.MapPost("/alarms/{id:guid}/acknowledge", async (HttpContext c, Guid id, PostgresPilotQueries q, CancellationToken ct) =>
        { await q.Acknowledge(Actor(c), id, ct); return Results.NoContent(); }).WithMetadata(new ProjectRule("owner,operator"));
        projectApi.MapPost("/deliveries/{id:guid}/retry", async (HttpContext c, Guid id, PostgresPilotQueries q, CancellationToken ct) =>
        { await q.Retry(Actor(c), id, ct); return Results.NoContent(); }).WithMetadata(new ProjectRule("owner,operator"));

        projectApi.MapGet("/members", async (HttpContext c, PostgresIdentityStore s, CancellationToken ct)
            => Results.Ok(await s.Members(Actor(c).ProjectId, ct))).WithMetadata(new ProjectRule("owner"));
        projectApi.MapPost("/members", async (HttpContext c, MemberInput input, PostgresIdentityStore s, CancellationToken ct) =>
        { await s.PutMember(Actor(c), input, ct); return Results.NoContent(); }).WithMetadata(new ProjectRule("owner"));
        projectApi.MapDelete("/members/{id:guid}", async (HttpContext c, Guid id, PostgresIdentityStore s, CancellationToken ct) =>
        { await s.RemoveMember(Actor(c), id, ct); return Results.NoContent(); }).WithMetadata(new ProjectRule("owner"));
        projectApi.MapGet("/keys", async (HttpContext c, PostgresIdentityStore s, CancellationToken ct)
            => Results.Ok(await s.Keys(Actor(c).ProjectId, ct))).WithMetadata(new ProjectRule("owner"));
        projectApi.MapPost("/keys", async (HttpContext c, KeyInput input, PostgresIdentityStore s, CancellationToken ct)
            => Results.Json(await s.CreateKey(Actor(c), input, ct), statusCode: 201)).WithMetadata(new ProjectRule("owner"));
        projectApi.MapDelete("/keys/{id:guid}", async (HttpContext c, Guid id, PostgresIdentityStore s, CancellationToken ct) =>
        { await s.RevokeKey(Actor(c), id, ct); return Results.NoContent(); }).WithMetadata(new ProjectRule("owner"));
        projectApi.MapGet("/channels", async (HttpContext c, PostgresPilotQueries q, CancellationToken ct)
            => Results.Ok(await q.Channels(Actor(c).ProjectId, ct))).WithMetadata(new ProjectRule("owner"));
        projectApi.MapPost("/channels", async (HttpContext c, ChannelInput input, PostgresPilotQueries q, CancellationToken ct)
            => Results.Json(await q.CreateChannel(Actor(c), input, ct), statusCode: 201)).WithMetadata(new ProjectRule("owner"));
        projectApi.MapPut("/channels/{id:guid}", async (HttpContext c, Guid id, EnabledInput input, PostgresPilotQueries q, CancellationToken ct) =>
        { await q.SetChannel(Actor(c), id, input.Enabled, ct); return Results.NoContent(); }).WithMetadata(new ProjectRule("owner"));
        projectApi.MapGet("/operations", async (HttpContext c, PostgresPilotQueries q, CancellationToken ct)
            => Results.Ok(await q.Operations(Actor(c).ProjectId, ct))).WithMetadata(new ProjectRule("owner"));

        MapOperations(app, projectApi);
    }

    /// <summary>V1.1: saved views, invitations, password reset, retention and CSV exports.</summary>
    private static void MapOperations(WebApplication app, RouteGroupBuilder projectApi)
    {
        // Saved views belong to the signed-in user; every role may keep its own.
        projectApi.MapGet("/views", async (HttpContext c, PostgresOperationsQueries q, string? kind, CancellationToken ct)
            => Results.Ok(await q.SavedViews(Actor(c), kind, ct))).WithMetadata(new ProjectRule());
        projectApi.MapPost("/views", async (HttpContext c, SavedViewInput input, PostgresOperationsQueries q, CancellationToken ct)
            => Results.Json(await q.SaveView(Actor(c), input, ct), statusCode: 201)).WithMetadata(new ProjectRule());
        projectApi.MapDelete("/views/{id:guid}", async (HttpContext c, Guid id, PostgresOperationsQueries q, CancellationToken ct) =>
        { await q.DeleteView(Actor(c), id, ct); return Results.NoContent(); }).WithMetadata(new ProjectRule());

        // Owners and operators pick an alarm assignee from this list; it exposes only names.
        projectApi.MapGet("/members/assignable", async (HttpContext c, PostgresIdentityStore s, CancellationToken ct)
            => Results.Ok(await s.Assignable(Actor(c).ProjectId, ct))).WithMetadata(new ProjectRule("owner,operator"));
        projectApi.MapPost("/members/invite", async (HttpContext c, InviteInput input, PostgresOperationsQueries q, CancellationToken ct)
            => Results.Json(await q.Invite(Actor(c), input, ct), statusCode: 201)).WithMetadata(new ProjectRule("owner"));

        // Public account links. Rate limited like login; responses never reveal whether an address exists.
        app.MapPost("/api/v1/auth/password-reset", async (ResetRequestInput input, PostgresOperationsQueries q, CancellationToken ct) =>
        { await q.RequestPasswordReset(input, ct); return Results.Accepted(); }).RequireRateLimiting("login");
        app.MapGet("/api/v1/auth/tokens/{token}", async (string token, PostgresOperationsQueries q, CancellationToken ct)
            => Results.Ok(await q.TokenInfo(token, ct))).RequireRateLimiting("login");
        app.MapPost("/api/v1/auth/invitations/{token}", async (string token, TokenPasswordInput input, PostgresOperationsQueries q, CancellationToken ct) =>
        { await q.CompleteToken(token, "invite", input, ct); return Results.NoContent(); }).RequireRateLimiting("login");
        app.MapPost("/api/v1/auth/password-reset/{token}", async (string token, TokenPasswordInput input, PostgresOperationsQueries q, CancellationToken ct) =>
        { await q.CompleteToken(token, "reset", input, ct); return Results.NoContent(); }).RequireRateLimiting("login");

        projectApi.MapGet("/retention", async (HttpContext c, PostgresOperationsQueries q, CancellationToken ct)
            => Results.Ok(await q.Retention(Actor(c).ProjectId, ct))).WithMetadata(new ProjectRule("owner"));
        projectApi.MapPut("/retention", async (HttpContext c, RetentionInput input, PostgresOperationsQueries q, CancellationToken ct) =>
        { await q.SetRetention(Actor(c), input, ct); return Results.NoContent(); }).WithMetadata(new ProjectRule("owner"));

        // Exports carry customer data; viewers read on screen but do not download.
        projectApi.MapGet("/transactions/export", async (HttpContext c, PostgresOperationsQueries q, string? flow, string? status, string? delay,
            string? search, string? from, string? to, string? test, CancellationToken ct) =>
        {
            var query = IntegraOps.Application.InputValidation.TransactionList(flow, status, delay, 1, 100, search, from, to, test);
            var csv = await q.TransactionsCsv(Actor(c).ProjectId, query, ct);
            await Audit(c, "export.transactions", ct);
            return Results.Text(csv, "text/csv; charset=utf-8", System.Text.Encoding.UTF8);
        }).WithMetadata(new ProjectRule("owner,operator"));
        projectApi.MapGet("/alarms/export", async (HttpContext c, PostgresPilotQueries alarms, PostgresOperationsQueries q, string? state, string? flow,
            string? search, string? from, string? to, string? test, string? assigned, CancellationToken ct) =>
        {
            var filter = PostgresPilotQueries.ParseAlarmFilter(state, flow, search, from, to, test, assigned, Actor(c).UserId);
            var rows = await alarms.AlarmsForExport(Actor(c).ProjectId, filter, PostgresOperationsQueries.ExportLimit, ct);
            await Audit(c, "export.alarms", ct);
            return Results.Text(q.AlarmsCsv(rows), "text/csv; charset=utf-8", System.Text.Encoding.UTF8);
        }).WithMetadata(new ProjectRule("owner,operator"));
    }

    private static async Task Audit(HttpContext c, string action, CancellationToken ct)
    {
        var actor = Actor(c);
        await using var connection = await c.RequestServices.GetRequiredService<Npgsql.NpgsqlDataSource>().OpenConnectionAsync(ct);
        await PilotDb.Audit(connection, null, actor.ProjectId, actor.UserId, action, c.Request.QueryString.Value is { Length: > 0 and <= 200 } qs ? qs : "-", ct);
    }
}
