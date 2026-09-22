using IntegraOps.Infrastructure;

namespace IntegraOps.Api;

/// <summary>Stage 13: self-service signup, second factor, single sign-on, organizations, plans and data procedures.</summary>
public static class EnterpriseEndpoints
{
    public sealed record OrganizationRule(string Roles = "owner,admin,member");
    private static SessionUser User(HttpContext c) => (SessionUser)c.Items[typeof(SessionUser)]!;
    private static Guid Org(HttpContext c) => (Guid)c.Items["organization"]!;

    public static RequestContext Context(HttpContext c)
    {
        var forwarded = c.Request.Headers["X-Forwarded-For"].ToString();
        var ip = forwarded.Length > 0 ? forwarded.Split(',')[0].Trim() : c.Connection.RemoteIpAddress?.ToString();
        return new RequestContext(ip, c.Request.Headers.UserAgent.ToString());
    }

    public static async ValueTask<object?> SessionFilter(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var c = invocation.HttpContext;
        var user = await c.RequestServices.GetRequiredService<PostgresIdentityStore>().Session(PilotEndpoints.Bearer(c), c.RequestAborted)
            ?? throw new PilotHttpException(401, "Oturum sona erdi. Tekrar giriş yapın.");
        c.Items[typeof(SessionUser)] = user;
        return await next(invocation);
    }

    public static void Map(WebApplication app, RouteGroupBuilder projectApi, IConfiguration configuration)
    {
        // ---- public ----
        app.MapPost("/api/v1/auth/signup", async (HttpContext c, SignupInput input, PostgresOrganizationStore s, CancellationToken ct) =>
        {
            if (!configuration.GetValue("Signup:Enabled", true)) throw new PilotHttpException(403, "Kayıt kapalı. Davet ile katılabilirsiniz.");
            return Results.Json(await s.Signup(input, Context(c), ct), statusCode: 201);
        }).RequireRateLimiting("login");
        app.MapPost("/api/v1/auth/mfa", async (HttpContext c, MfaChallengeInput input, PostgresSecurityStore s, CancellationToken ct)
            => Results.Ok(await s.CompleteMfa(input, Context(c), ct))).RequireRateLimiting("login");
        app.MapPost("/api/v1/auth/sso/start", async (SsoStartInput input, string? next, PostgresSecurityStore s, CancellationToken ct)
            => Results.Ok(await s.StartSso(input, next, ct))).RequireRateLimiting("login");
        app.MapGet("/api/v1/auth/sso/callback", async (HttpContext c, string? code, string? state, string? error, PostgresSecurityStore s, CancellationToken ct)
            => Results.Redirect(await s.CompleteSso(code, state, error, Context(c), ct))).RequireRateLimiting("login");
        app.MapPost("/api/v1/auth/sso/ticket", async (TicketInput input, PostgresSecurityStore s, CancellationToken ct)
            => Results.Ok(await s.RedeemTicket(input, ct))).RequireRateLimiting("login");

        // ---- signed-in user ----
        var me = app.MapGroup("/api/v1/me").AddEndpointFilter(SessionFilter);
        me.MapGet("/organizations", async (HttpContext c, PostgresOrganizationStore s, CancellationToken ct) => Results.Ok(await s.MyOrganizations(User(c).Id, ct)));
        me.MapGet("/security", async (HttpContext c, PostgresSecurityStore s, CancellationToken ct)
            => Results.Ok(new { mfaEnabled = User(c).Mfa, systemAdmin = User(c).SystemAdmin, events = await s.MyEvents(User(c).Id, ct) }));
        me.MapPost("/mfa/begin", async (HttpContext c, PostgresSecurityStore s, CancellationToken ct) => Results.Ok(await s.BeginMfa(User(c), ct)));
        me.MapPost("/mfa/enable", async (HttpContext c, MfaCodeInput input, PostgresSecurityStore s, CancellationToken ct) => Results.Ok(await s.EnableMfa(User(c), input, Context(c), ct)));
        me.MapPost("/mfa/disable", async (HttpContext c, MfaDisableInput input, PostgresSecurityStore s, CancellationToken ct) =>
        { await s.DisableMfa(User(c), input, Context(c), ct); return Results.NoContent(); });

        projectApi.MapGet("/onboarding", async (HttpContext c, PostgresOrganizationStore s, CancellationToken ct)
            => Results.Ok(await s.Onboarding(PilotEndpoints.Actor(c).ProjectId, ct))).WithMetadata(new ProjectRule());

        // ---- organization administration ----
        var org = app.MapGroup("/api/v1/organizations/{org:guid}").AddEndpointFilter(SessionFilter).AddEndpointFilter(async (invocation, next) =>
        {
            var c = invocation.HttpContext; var id = (Guid)c.Request.RouteValues["org"]!.ToString()!.Let(Guid.Parse);
            var role = await c.RequestServices.GetRequiredService<PostgresOrganizationStore>().RoleIn(id, User(c).Id, c.RequestAborted)
                ?? (User(c).SystemAdmin ? "owner" : throw new PilotHttpException(403, "Bu organizasyona erişiminiz yok."));
            var rule = c.GetEndpoint()?.Metadata.GetMetadata<OrganizationRule>() ?? new OrganizationRule();
            if (!rule.Roles.Split(',').Contains(role)) throw new PilotHttpException(403, "Bu işlem için organizasyon sahibi ya da yöneticisi olmalısınız.");
            c.Items["organization"] = id; c.Items["organizationRole"] = role;
            return await next(invocation);
        });
        org.MapGet("", async (HttpContext c, PostgresOrganizationStore s, CancellationToken ct) => Results.Ok(await s.Overview(Org(c), User(c).Id, ct)));
        org.MapPut("", async (HttpContext c, OrganizationInput input, PostgresOrganizationStore s, CancellationToken ct) =>
        { await s.Rename(Org(c), User(c).Id, input, ct); return Results.NoContent(); }).WithMetadata(new OrganizationRule("owner,admin"));
        org.MapPost("/members", async (HttpContext c, OrganizationMemberInput input, PostgresOrganizationStore s, CancellationToken ct) =>
        {
            if (input.Role == "owner" && c.Items["organizationRole"] is not "owner") throw new PilotHttpException(403, "Sahip rolünü yalnızca bir sahip verebilir.");
            await s.PutMember(Org(c), User(c).Id, input, ct); return Results.NoContent();
        }).WithMetadata(new OrganizationRule("owner,admin"));
        org.MapDelete("/members/{user:guid}", async (HttpContext c, Guid user, PostgresOrganizationStore s, CancellationToken ct) =>
        { await s.RemoveMember(Org(c), User(c).Id, user, ct); return Results.NoContent(); }).WithMetadata(new OrganizationRule("owner,admin"));
        org.MapPost("/groups", async (HttpContext c, GroupInput input, PostgresOrganizationStore s, CancellationToken ct)
            => Results.Json(await s.SaveGroup(Org(c), User(c).Id, null, input, ct), statusCode: 201)).WithMetadata(new OrganizationRule("owner,admin"));
        org.MapPut("/groups/{id:guid}", async (HttpContext c, Guid id, GroupInput input, PostgresOrganizationStore s, CancellationToken ct)
            => Results.Ok(await s.SaveGroup(Org(c), User(c).Id, id, input, ct))).WithMetadata(new OrganizationRule("owner,admin"));
        org.MapDelete("/groups/{id:guid}", async (HttpContext c, Guid id, PostgresOrganizationStore s, CancellationToken ct) =>
        { await s.DeleteGroup(Org(c), User(c).Id, id, ct); return Results.NoContent(); }).WithMetadata(new OrganizationRule("owner,admin"));
        org.MapGet("/security-events", async (HttpContext c, PostgresSecurityStore s, CancellationToken ct)
            => Results.Ok(await s.OrganizationEvents(Org(c), ct))).WithMetadata(new OrganizationRule("owner,admin"));
        org.MapGet("/audit", async (HttpContext c, PostgresSecurityStore s, CancellationToken ct)
            => Results.Ok(await s.OrganizationAudit(Org(c), ct))).WithMetadata(new OrganizationRule("owner,admin"));
        org.MapPut("/sso", async (HttpContext c, SsoConfigInput input, PostgresSecurityStore s, CancellationToken ct) =>
        { await s.SaveSso(Org(c), User(c).Id, input, ct); return Results.NoContent(); }).WithMetadata(new OrganizationRule("owner"));
        org.MapGet("/exports", async (HttpContext c, PostgresSecurityStore s, CancellationToken ct)
            => Results.Ok(await s.Exports(Org(c), ct))).WithMetadata(new OrganizationRule("owner"));
        org.MapPost("/exports", async (HttpContext c, PostgresSecurityStore s, CancellationToken ct)
            => Results.Json(await s.RequestExport(Org(c), User(c).Id, ct), statusCode: 202)).WithMetadata(new OrganizationRule("owner"));
        org.MapGet("/exports/{id:guid}/download", async (HttpContext c, Guid id, PostgresSecurityStore s, CancellationToken ct) =>
        {
            var (path, name) = await s.ExportFile(Org(c), id, User(c).Id, ct);
            return Results.File(path, "application/zip", name);
        }).WithMetadata(new OrganizationRule("owner"));
        org.MapPost("/deletion", async (HttpContext c, DeletionInput input, PostgresSecurityStore s, CancellationToken ct) =>
        { await s.RequestDeletion(Org(c), User(c), input, Context(c), ct); return Results.NoContent(); }).WithMetadata(new OrganizationRule("owner"));

        // ---- system administration (plans and limits per customer) ----
        var admin = app.MapGroup("/api/v1/admin").AddEndpointFilter(SessionFilter).AddEndpointFilter(async (invocation, next) =>
        {
            if (!User(invocation.HttpContext).SystemAdmin) throw new PilotHttpException(403, "Sistem yöneticisi yetkisi gerekli.");
            return await next(invocation);
        });
        admin.MapGet("/organizations", async (PostgresOrganizationStore s, CancellationToken ct) => Results.Ok(await s.AdminOrganizations(ct)));
        admin.MapGet("/plans", () => Results.Ok(Plans.Defaults.Select(p => new { code = p.Key, name = Plans.Names[p.Key], limits = p.Value })));
        admin.MapPut("/organizations/{id:guid}/plan", async (HttpContext c, Guid id, PlanInput input, PostgresOrganizationStore s, CancellationToken ct) =>
        { await s.SetPlan(User(c).Id, id, input, ct); return Results.NoContent(); });
    }

    private static T Let<T>(this string value, Func<string, T> f) => f(value);
}
