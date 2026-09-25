using System.Text.Json;
using IntegraOps.Application;
using IntegraOps.Infrastructure;

namespace IntegraOps.Api;

public sealed record ProjectRule(string Roles = "owner,operator,viewer", bool SessionOnly = true);
public sealed record ProjectNameInput(string Name, Guid? OrganizationId = null);

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
            // Ingest keys may read their own reconciliation history to resume from the last window.
            var mappedIngest = write && path is not null && path.StartsWith("/api/v1/connections/") && path.EndsWith("/ingest") && actor.Scope is "legacy" or "ingest";
            var allowed = (HttpMethods.IsGet(c.Request.Method) && (actor.Scope is "legacy" or "read" || path == "/api/v1/reconciliation/runs"))
                || mappedIngest || (write && path == "/api/v1/connections/ping")
                || (write && path is "/api/v1/events" or "/api/v1/agents/heartbeat" or "/api/v1/reconciliation/runs" or "/api/v1/reports/digest" && actor.Scope is "legacy" or "ingest")
                || (write && path == "/api/v1/reconciliation/lookup" && actor.Scope is "legacy" or "read" or "ingest")
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
        app.MapPost("/api/v1/auth/login", async (HttpContext c, LoginInput input, PostgresIdentityStore store, CancellationToken ct)
            => Results.Ok(await store.Login(input, EnterpriseEndpoints.Context(c), ct))).RequireRateLimiting("login");

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
            => Results.Json(await s.CreateProject(User(c).Id, input.Name, input.OrganizationId, ct), statusCode: 201));
        me.MapPost("/logout", async (HttpContext c, PostgresIdentityStore s, CancellationToken ct) =>
        { await s.Logout(Bearer(c), ct); return Results.NoContent(); });
        me.MapPost("/password", async (HttpContext c, PasswordInput input, PostgresIdentityStore s, CancellationToken ct) =>
        { await s.ChangePassword(User(c).Id, input, ct); return Results.NoContent(); });

        projectApi.MapGet("/access", (HttpContext c) => Results.Ok(Actor(c))).WithMetadata(new ProjectRule());
        projectApi.MapGet("/alarms", async (HttpContext c, PostgresPilotQueries q, string? state, string? flow, int? page,
            string? search, string? from, string? to, string? test, string? assigned, string? step, string? cause, CancellationToken ct)
            => Results.Ok(await q.Alarms(Actor(c).ProjectId,
                PostgresPilotQueries.ParseAlarmFilter(state, flow, search, from, to, test, assigned, Actor(c).UserId, step, cause), page ?? 1, ct)))
            .WithMetadata(new ProjectRule());
        projectApi.MapGet("/alarms/triage", async (HttpContext c, PostgresTriageQueries q, string? test, CancellationToken ct)
            => Results.Ok(await q.Triage(Actor(c).ProjectId, test ?? "exclude", ct))).WithMetadata(new ProjectRule());
        projectApi.MapPost("/alarms/bulk", async (HttpContext c, BulkAlarmInput input, PostgresPilotQueries q, CancellationToken ct)
            => Results.Ok(await q.Bulk(Actor(c), input.Ids, input.Op, input.UserId, ct))).WithMetadata(new ProjectRule("owner,operator"));
        projectApi.MapPut("/alarms/{id:guid}/resolution", async (HttpContext c, Guid id, ResolutionInput input, PostgresPilotQueries q, CancellationToken ct) =>
        { await q.SetCause(Actor(c), id, input.Cause, input.Fix, ct); return Results.NoContent(); }).WithMetadata(new ProjectRule("owner,operator"));
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
        // Transfer agents report with their ingest key; the panel lists them on the system page.
        projectApi.MapPost("/agents/heartbeat", async (HttpContext c, AgentHeartbeatInput input, PostgresPilotQueries q, CancellationToken ct) =>
        { await q.Heartbeat(Actor(c), input, ct); return Results.NoContent(); }).WithMetadata(new ProjectRule("owner,operator,viewer", SessionOnly: false));
        projectApi.MapGet("/agents", async (HttpContext c, PostgresPilotQueries q, CancellationToken ct)
            => Results.Ok(await q.Agents(Actor(c).ProjectId, ct))).WithMetadata(new ProjectRule());

        MapOperations(app, projectApi);
        MapVersions(projectApi);
        MapReconciliation(projectApi);
        MapReports(projectApi);
        MapConnections(projectApi);
    }

    /// <summary>V2–V3: connections, mapping templates, key rotation, client pings.</summary>
    private static void MapConnections(RouteGroupBuilder projectApi)
    {
        projectApi.MapGet("/connections", async (HttpContext c, PostgresConnectionStore s, CancellationToken ct)
            => Results.Ok(await s.List(Actor(c).ProjectId, ct))).WithMetadata(new ProjectRule());
        projectApi.MapGet("/connections/catalog", (PostgresConnectionStore s) => Results.Ok(s.Catalog())).WithMetadata(new ProjectRule());
        projectApi.MapPost("/connections", async (HttpContext c, ConnectionInput input, PostgresConnectionStore s, CancellationToken ct)
            => Results.Json(await s.Save(Actor(c), null, input, ct), statusCode: 201)).WithMetadata(new ProjectRule("owner"));
        projectApi.MapPut("/connections/{id:guid}", async (HttpContext c, Guid id, ConnectionInput input, PostgresConnectionStore s, CancellationToken ct)
            => Results.Ok(await s.Save(Actor(c), id, input, ct))).WithMetadata(new ProjectRule("owner"));
        projectApi.MapDelete("/connections/{id:guid}", async (HttpContext c, Guid id, PostgresConnectionStore s, CancellationToken ct) =>
        { await s.Delete(Actor(c), id, ct); return Results.NoContent(); }).WithMetadata(new ProjectRule("owner"));
        projectApi.MapPost("/connections/test", (MappingTestInput input, PostgresConnectionStore s) => Results.Ok(s.TestMapping(input))).WithMetadata(new ProjectRule("owner,operator"));
        // Sources with an ingest key post raw payloads here; the connection's template turns them into events.
        projectApi.MapPost("/connections/{id:guid}/ingest", async (HttpContext c, Guid id, JsonElement payload, PostgresConnectionStore s, CancellationToken ct) =>
        {
            var result = await s.Ingest(Actor(c), id, payload, ct);
            await s.RecordUsage(Actor(c), c.Request.Headers.UserAgent, true, null, ct);
            return Results.Json(result, statusCode: 202);
        }).WithMetadata(new ProjectRule("owner,operator", SessionOnly: false));
        projectApi.MapPost("/connections/ping", async (HttpContext c, PostgresConnectionStore s, CancellationToken ct) =>
        { await s.Ping(Actor(c), c.Request.Headers.UserAgent, ct); return Results.Ok(new { ok = true, serverTime = DateTimeOffset.UtcNow }); })
            .WithMetadata(new ProjectRule("owner,operator,viewer", SessionOnly: false));
        projectApi.MapPost("/keys/{id:guid}/rotate", async (HttpContext c, Guid id, RotateInput input, PostgresConnectionStore s, CancellationToken ct)
            => Results.Json(await s.Rotate(Actor(c), id, input, ct), statusCode: 201)).WithMetadata(new ProjectRule("owner"));
    }

    /// <summary>V2 analytics: overview, trends, digest settings and on-demand digest.</summary>
    private static void MapReports(RouteGroupBuilder projectApi)
    {
        projectApi.MapGet("/reports/overview", async (HttpContext c, PostgresReportQueries r, string? from, string? to, string? flow, string? test, CancellationToken ct)
            => Results.Ok(await r.Overview(Actor(c).ProjectId, from, to, flow, test, ct))).WithMetadata(new ProjectRule("owner,operator,viewer", SessionOnly: false));
        projectApi.MapGet("/reports/trend", async (HttpContext c, PostgresReportQueries r, string? flow, string? test, string? period, int? buckets, CancellationToken ct)
            => Results.Ok(await r.Trend(Actor(c).ProjectId, flow, test, period, buckets, ct))).WithMetadata(new ProjectRule("owner,operator,viewer", SessionOnly: false));
        projectApi.MapGet("/reports/digest", async (HttpContext c, PostgresReportQueries r, CancellationToken ct)
            => Results.Ok(await r.DigestSettings(Actor(c).ProjectId, ct))).WithMetadata(new ProjectRule("owner"));
        projectApi.MapPut("/reports/digest", async (HttpContext c, DigestSettingsInput input, PostgresReportQueries r, CancellationToken ct) =>
        { await r.SetDigestSettings(Actor(c), input, ct); return Results.NoContent(); }).WithMetadata(new ProjectRule("owner"));
        // Owners (panel) and ingest keys (the Python digest command) may trigger a digest now.
        projectApi.MapPost("/reports/digest", async (HttpContext c, DigestRequest input, PostgresReportQueries r, CancellationToken ct)
            => Results.Ok(await r.SendDigest(Actor(c).ProjectId, input.Period, Actor(c).UserId is null ? "api" : "manual", Actor(c).UserId, ct)))
            .WithMetadata(new ProjectRule("owner", SessionOnly: false));
    }

    /// <summary>V2: service contract for the Python reconciler and panel queries.</summary>
    private static void MapReconciliation(RouteGroupBuilder projectApi)
    {
        projectApi.MapPost("/reconciliation/lookup", async (HttpContext c, LookupInput input, PostgresReconciliationStore s, CancellationToken ct)
            => Results.Ok(await s.Lookup(Actor(c).ProjectId, input, ct))).WithMetadata(new ProjectRule("owner,operator,viewer", SessionOnly: false));
        projectApi.MapPost("/reconciliation/runs", async (HttpContext c, ReconciliationRunInput input, PostgresReconciliationStore s, CancellationToken ct)
            => Results.Json(await s.RecordRun(Actor(c), input, ct), statusCode: 201)).WithMetadata(new ProjectRule("owner,operator", SessionOnly: false));
        projectApi.MapGet("/reconciliation/runs", async (HttpContext c, PostgresReconciliationStore s, string? flow, int? page, CancellationToken ct)
            => Results.Ok(await s.Runs(Actor(c).ProjectId, flow, page ?? 1, ct))).WithMetadata(new ProjectRule("owner,operator,viewer", SessionOnly: false));
        projectApi.MapGet("/reconciliation/discrepancies", async (HttpContext c, PostgresReconciliationStore s, string? state, string? flow, string? kind, int? page, CancellationToken ct)
            => Results.Ok(await s.Discrepancies(Actor(c).ProjectId, state, flow, kind, page ?? 1, ct))).WithMetadata(new ProjectRule());
    }

    /// <summary>V1.2: flow drafts and versions, calendars, breach report.</summary>
    private static void MapVersions(RouteGroupBuilder projectApi)
    {
        static string Code(string flow) => IntegraOps.Application.InputValidation.FlowCode(flow);
        projectApi.MapGet("/flows/{flow}/versions/{number:int}", async (HttpContext c, string flow, int number, PostgresFlowVersionStore s, CancellationToken ct)
            => Results.Ok(await s.Version(Actor(c).ProjectId, Code(flow), number, ct))).WithMetadata(new ProjectRule());
        projectApi.MapGet("/flows/{flow}/versions/{from:int}/diff/{to:int}", async (HttpContext c, string flow, int from, int to, PostgresFlowVersionStore s, CancellationToken ct)
            => Results.Ok(await s.Diff(Actor(c).ProjectId, Code(flow), from, to, ct))).WithMetadata(new ProjectRule());
        projectApi.MapGet("/flows/{flow}/threshold-advice", async (HttpContext c, string flow, PostgresThresholdAdvisor a, int? days, string? test, CancellationToken ct)
            => Results.Ok(await a.Advise(Actor(c).ProjectId, Code(flow), days ?? 30, test ?? "exclude", ct))).WithMetadata(new ProjectRule());
        projectApi.MapPost("/flows/{flow}/threshold-advice/apply", async (HttpContext c, string flow, ThresholdApplyInput input, PostgresThresholdAdvisor a, PostgresFlowVersionStore s, CancellationToken ct)
            => Results.Ok(await a.Apply(Actor(c), Code(flow), input.Steps, s, ct))).WithMetadata(new ProjectRule("owner,operator"));
        projectApi.MapPost("/flows/{flow}/versions", async (HttpContext c, string flow, PostgresFlowVersionStore s, CancellationToken ct)
            => Results.Json(await s.CreateDraft(Actor(c), Code(flow), ct), statusCode: 201)).WithMetadata(new ProjectRule("owner,operator"));
        projectApi.MapPut("/flows/{flow}/versions/{number:int}", async (HttpContext c, string flow, int number, VersionInput input, PostgresFlowVersionStore s, CancellationToken ct) =>
        { await s.UpdateDraft(Actor(c), Code(flow), number, input, ct); return Results.NoContent(); }).WithMetadata(new ProjectRule("owner,operator"));
        projectApi.MapPost("/flows/{flow}/versions/{number:int}/publish", async (HttpContext c, string flow, int number, PublishInput input, PostgresFlowVersionStore s, CancellationToken ct)
            => Results.Ok(await s.Publish(Actor(c), Code(flow), number, input, ct))).WithMetadata(new ProjectRule("owner,operator"));
        projectApi.MapDelete("/flows/{flow}/versions/{number:int}", async (HttpContext c, string flow, int number, PostgresFlowVersionStore s, CancellationToken ct) =>
        { await s.DeleteDraft(Actor(c), Code(flow), number, ct); return Results.NoContent(); }).WithMetadata(new ProjectRule("owner,operator"));
        projectApi.MapPut("/flows/{flow}", async (HttpContext c, string flow, FlowNameInput input, PostgresFlowVersionStore s, CancellationToken ct) =>
        { await s.Rename(Actor(c), Code(flow), input, ct); return Results.NoContent(); }).WithMetadata(new ProjectRule("owner,operator"));

        // Dalga 1: sector templates and sample data so a new project shows a living board before any source is connected.
        projectApi.MapGet("/flows/catalog", () => Results.Ok(FlowTemplates.Catalog())).WithMetadata(new ProjectRule());
        projectApi.MapPost("/flows/from-template", async (HttpContext c, TemplateInput input, PostgresFlowVersionStore s, CancellationToken ct)
            => Results.Json(await s.CreateFromTemplate(Actor(c), input, ct), statusCode: 201)).WithMetadata(new ProjectRule("owner,operator"));
        projectApi.MapPost("/flows/{flow}/demo", async (HttpContext c, string flow, DemoInput input, DemoData d, CancellationToken ct)
            => Results.Json(await d.Generate(Actor(c), Code(flow), input.Count ?? 30, input.Days ?? 14, ct), statusCode: 201)).WithMetadata(new ProjectRule("owner,operator"));
        projectApi.MapDelete("/flows/{flow}/demo", async (HttpContext c, string flow, DemoData d, CancellationToken ct)
            => Results.Ok(await d.Delete(Actor(c), Code(flow), ct))).WithMetadata(new ProjectRule("owner,operator"));

        projectApi.MapGet("/calendars", async (HttpContext c, PostgresFlowVersionStore s, CancellationToken ct)
            => Results.Ok(await s.Calendars(Actor(c).ProjectId, ct))).WithMetadata(new ProjectRule());
        projectApi.MapPost("/calendars", async (HttpContext c, CalendarInput input, PostgresFlowVersionStore s, CancellationToken ct)
            => Results.Json(await s.SaveCalendar(Actor(c), null, input, ct), statusCode: 201)).WithMetadata(new ProjectRule("owner,operator"));
        projectApi.MapPut("/calendars/{id:guid}", async (HttpContext c, Guid id, CalendarInput input, PostgresFlowVersionStore s, CancellationToken ct)
            => Results.Ok(await s.SaveCalendar(Actor(c), id, input, ct))).WithMetadata(new ProjectRule("owner,operator"));
        projectApi.MapDelete("/calendars/{id:guid}", async (HttpContext c, Guid id, PostgresFlowVersionStore s, CancellationToken ct) =>
        { await s.DeleteCalendar(Actor(c), id, ct); return Results.NoContent(); }).WithMetadata(new ProjectRule("owner,operator"));

        projectApi.MapGet("/reports/breaches", async (HttpContext c, PostgresFlowVersionStore s, string? flow, string? from, string? to, string? test, int? page, CancellationToken ct)
            => Results.Ok(await s.Breaches(Actor(c).ProjectId, flow, from, to, test, page ?? 1, ct))).WithMetadata(new ProjectRule());
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
            string? search, string? from, string? to, string? test, string? assigned, string? step, string? cause, CancellationToken ct) =>
        {
            var filter = PostgresPilotQueries.ParseAlarmFilter(state, flow, search, from, to, test, assigned, Actor(c).UserId, step, cause);
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
