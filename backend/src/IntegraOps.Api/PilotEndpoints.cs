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
        projectApi.MapGet("/alarms", async (HttpContext c, PostgresPilotQueries q, string? state, string? flow, int? page, CancellationToken ct)
            => Results.Ok(await q.Alarms(Actor(c).ProjectId, state, flow, page ?? 1, ct))).WithMetadata(new ProjectRule());
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
    }
}
