using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using System.Text.Json;
using Npgsql;
using static IntegraOps.Infrastructure.PilotDb;
using static IntegraOps.Infrastructure.PilotSecurity;

namespace IntegraOps.Infrastructure;

public sealed record SignupInput(string? OrganizationName, string? Name, string? Email, string? Password);
public sealed record OrganizationInput(string? Name);
public sealed record OrganizationMemberInput(string? Email, string? Role);
public sealed record GroupInput(string? Name, List<Guid>? Projects, List<GroupMemberInput>? Members);
public sealed record GroupMemberInput(string? Email, string? Role);
public sealed record PlanInput(string? Plan, int? MaxProjects, int? MaxUsers, int? MaxEventsPerDay, int? MaxRetentionDays, string? Note, int? TrialDays);
public sealed record RequestContext(string? Ip, string? UserAgent);

/// <summary>Organizations, their members, project groups, plans, limits, usage and the onboarding checklist.</summary>
public sealed class PostgresOrganizationStore(NpgsqlDataSource source, IConfigurationReader panel)
{
    private readonly PasswordHasher<string> hasher = new();

    // ---------- signup ----------

    public async Task<object> Signup(SignupInput input, RequestContext ctx, CancellationToken ct)
    {
        var orgName = Text(input.OrganizationName, 160, "Şirket adı"); var name = Text(input.Name, 120, "Ad");
        var email = Email(input.Email); var password = Password(input.Password);
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await Value(c, tx, "SELECT pg_advisory_xact_lock(hashtextextended(@e,7142645))", ct, ("e", email));
        if (await Value(c, tx, "SELECT 1 FROM app_users WHERE email=@e", ct, ("e", email)) is not null)
            throw new PilotHttpException(409, "Bu e-posta ile bir hesap zaten var. Giriş yapın ya da parolanızı sıfırlayın.");
        var user = Guid.CreateVersion7(); var org = Guid.CreateVersion7(); var project = Guid.CreateVersion7();
        await Run(c, tx, """
            INSERT INTO app_users(id,email,name,password_hash,activated_at,last_login_at) VALUES(@u,@e,@n,@h,clock_timestamp(),clock_timestamp());
            INSERT INTO organizations(id,name,plan,trial_ends_at,plan_updated_at) VALUES(@o,@on,'trial',clock_timestamp()+make_interval(days => @d),clock_timestamp());
            INSERT INTO organization_members(organization_id,user_id,role) VALUES(@o,@u,'owner');
            INSERT INTO projects(id,organization_id,name) VALUES(@p,@o,'İlk proje');
            INSERT INTO project_members(project_id,user_id,role) VALUES(@p,@u,'owner')
            """, ct, ("u", user), ("e", email), ("n", name), ("h", hasher.HashPassword(email, password)), ("o", org), ("on", orgName), ("d", Plans.TrialDays), ("p", project));
        await Audit(c, tx, project, user, "org.signup", org.ToString(), ct);
        await Security(c, tx, user, org, "signup", ctx, null, ct);
        await PostgresOperationsQueries.QueueEmail(c, tx, email, "IntegraOps · Hoş geldiniz",
            $"Merhaba {name},\n\n\"{orgName}\" için IntegraOps hesabınız açıldı. Deneme süreniz {Plans.TrialDays} gündür.\n\n"
            + $"Başlamak için: {panel.PanelUrl}/login\n\nİlk adımlar: bir akış tanımlayın, bir gönderim anahtarı oluşturun, ilk bildirimi gönderin ve bir bildirim kanalı ekleyin. "
            + "Panelde \"Projeler\" sayfası bu adımları takip eder.\n", ct);
        var token = Token();
        await Run(c, tx, "INSERT INTO user_sessions(token_hash,user_id,expires_at) VALUES(@h,@u,clock_timestamp()+interval '8 hours')", ct, ("h", Hash(token)), ("u", user));
        await tx.CommitAsync(ct);
        return new { token, expiresIn = 28800, user = new SessionUser(user, email, name), organizationId = org, projectId = project };
    }

    internal static Task Security(NpgsqlConnection c, NpgsqlTransaction? tx, Guid? user, Guid? org, string kind, RequestContext ctx, string? detail, CancellationToken ct) =>
        Run(c, tx, "INSERT INTO security_events(user_id,organization_id,kind,ip,user_agent,detail) VALUES(CAST(@u AS uuid),CAST(@o AS uuid),@k,@ip,@ua,@d)", ct,
            ("u", user), ("o", org), ("k", kind), ("ip", Clip(ctx.Ip, 64)), ("ua", Clip(ctx.UserAgent, 200)), ("d", Clip(detail, 200)));

    private static string? Clip(string? s, int max) => s is null ? null : s.Length > max ? s[..max] : s;

    // ---------- organization overview ----------

    public async Task<object> Overview(Guid org, Guid user, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var o = (await Rows(c, null, """
            SELECT o.id,o.name,o.plan,o.trial_ends_at,o.plan_updated_at,o.plan_note,o.sso_issuer,o.sso_client_id,o.sso_domain,o.sso_required,
                o.deletion_requested_at,o.deletion_scheduled_at,o.created_at,
                l.max_projects,l.max_users,l.max_events_per_day,l.max_retention_days,
                (SELECT count(*) FROM projects p WHERE p.organization_id=o.id) projects,
                (SELECT count(DISTINCT user_id) FROM (SELECT m.user_id FROM organization_members m WHERE m.organization_id=o.id
                    UNION SELECT pm.user_id FROM project_members pm JOIN projects p ON p.id=pm.project_id WHERE p.organization_id=o.id
                    UNION SELECT gm.user_id FROM project_group_members gm JOIN project_groups g ON g.id=gm.group_id WHERE g.organization_id=o.id) u) users,
                (SELECT role FROM organization_members m WHERE m.organization_id=o.id AND m.user_id=@u) my_role
            FROM organizations o LEFT JOIN organization_limits l ON l.organization_id=o.id WHERE o.id=@o
            """, ct, ("o", org), ("u", user)))[0];
        var limits = Plans.Effective((string)o["plan"]!, o["max_projects"] as int?, o["max_users"] as int?, o["max_events_per_day"] as int?, o["max_retention_days"] as int?);
        var usage = await Rows(c, null, """
            SELECT day, events, rejected FROM usage_daily WHERE organization_id=@o AND day >= current_date - 29 ORDER BY day
            """, ct, ("o", org));
        var today = usage.LastOrDefault(u => ((DateOnly)u["day"]!) == DateOnly.FromDateTime(DateTime.UtcNow));
        var members = await Rows(c, null, """
            SELECT u.id,u.name,u.email,m.role,m.created_at,u.mfa_enabled,u.last_login_at FROM organization_members m JOIN app_users u ON u.id=m.user_id
            WHERE m.organization_id=@o ORDER BY m.role,u.name
            """, ct, ("o", org));
        var projects = await Rows(c, null, "SELECT id,name,created_at FROM projects WHERE organization_id=@o ORDER BY name", ct, ("o", org));
        var groups = await Rows(c, null, """
            SELECT g.id,g.name,g.created_at,
                (SELECT array_agg(project_id) FROM project_group_projects gp WHERE gp.group_id=g.id) project_ids,
                (SELECT json_agg(json_build_object('id',u.id,'name',u.name,'email',u.email,'role',gm.role) ORDER BY u.name) FROM project_group_members gm JOIN app_users u ON u.id=gm.user_id WHERE gm.group_id=g.id) members
            FROM project_groups g WHERE g.organization_id=@o ORDER BY g.name
            """, ct, ("o", org));
        foreach (var g in groups) g["members"] = JsonDocument.Parse(g["members"] as string ?? "[]").RootElement;
        var trialEnds = o["trial_ends_at"] as DateTime?;
        return new
        {
            id = o["id"], name = o["name"], plan = o["plan"], planName = Plans.Names.GetValueOrDefault((string)o["plan"]!, (string)o["plan"]!),
            trialEndsAt = trialEnds, trialExpired = (string)o["plan"]! == "trial" && trialEnds is { } t && t < DateTime.UtcNow,
            planUpdatedAt = o["plan_updated_at"], planNote = o["plan_note"], myRole = o["my_role"],
            limits, counts = new { projects = o["projects"], users = o["users"], eventsToday = today?["events"] ?? 0L },
            usage, members, projects, groups,
            sso = new { issuer = o["sso_issuer"], clientId = o["sso_client_id"], domain = o["sso_domain"], required = o["sso_required"], configured = o["sso_issuer"] is not null },
            deletion = new { requestedAt = o["deletion_requested_at"], scheduledAt = o["deletion_scheduled_at"] },
        };
    }

    public async Task Rename(Guid org, Guid user, OrganizationInput input, CancellationToken ct)
    {
        var name = Text(input.Name, 160, "Şirket adı");
        await using var c = await source.OpenConnectionAsync(ct);
        await Run(c, null, "UPDATE organizations SET name=@n WHERE id=@o", ct, ("n", name), ("o", org));
        await Security(c, null, user, org, "org.rename", new RequestContext(null, null), name, ct);
    }

    // ---------- organization members ----------

    public async Task PutMember(Guid org, Guid actor, OrganizationMemberInput input, CancellationToken ct)
    {
        var email = Email(input.Email);
        if (input.Role is not ("owner" or "admin" or "member")) throw new PilotHttpException(400, "Organizasyon rolü owner, admin veya member olmalı.");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var user = await Value(c, tx, "SELECT id FROM app_users WHERE email=@e", ct, ("e", email)) as Guid?
            ?? throw new PilotHttpException(404, "Bu e-postayla kayıtlı kullanıcı yok. Önce bir projeye davet edin.");
        await EnsureUserLimit(c, tx, org, user, ct);
        if (input.Role != "owner") await GuardLastOrgOwner(c, tx, org, user, ct);
        await Run(c, tx, "INSERT INTO organization_members(organization_id,user_id,role) VALUES(@o,@u,@r) ON CONFLICT(organization_id,user_id) DO UPDATE SET role=EXCLUDED.role", ct,
            ("o", org), ("u", user), ("r", input.Role));
        await Security(c, tx, actor, org, "org.member." + input.Role, new RequestContext(null, null), email, ct);
        await tx.CommitAsync(ct);
    }

    public async Task RemoveMember(Guid org, Guid actor, Guid user, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await GuardLastOrgOwner(c, tx, org, user, ct);
        if (await Run(c, tx, "DELETE FROM organization_members WHERE organization_id=@o AND user_id=@u", ct, ("o", org), ("u", user)) == 0)
            throw new PilotHttpException(404, "Üye bulunamadı.");
        await Security(c, tx, actor, org, "org.member.remove", new RequestContext(null, null), user.ToString(), ct);
        await tx.CommitAsync(ct);
    }

    private static async Task GuardLastOrgOwner(NpgsqlConnection c, NpgsqlTransaction tx, Guid org, Guid user, CancellationToken ct)
    {
        if ((bool)(await Value(c, tx, """
            SELECT EXISTS(SELECT 1 FROM organization_members WHERE organization_id=@o AND user_id=@u AND role='owner')
                AND (SELECT count(*) FROM organization_members WHERE organization_id=@o AND role='owner')=1
            """, ct, ("o", org), ("u", user)))!)
            throw new PilotHttpException(409, "Organizasyonun son sahibi kaldırılamaz ya da rolü düşürülemez.");
    }

    // ---------- project groups ----------

    public async Task<object> SaveGroup(Guid org, Guid actor, Guid? id, GroupInput input, CancellationToken ct)
    {
        var name = Text(input.Name, 120, "Grup adı");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var groupId = id ?? Guid.CreateVersion7();
        try
        {
            if (id is null) await Run(c, tx, "INSERT INTO project_groups(id,organization_id,name) VALUES(@id,@o,@n)", ct, ("id", groupId), ("o", org), ("n", name));
            else if (await Run(c, tx, "UPDATE project_groups SET name=@n WHERE id=@id AND organization_id=@o", ct, ("id", groupId), ("o", org), ("n", name)) == 0)
                throw new PilotHttpException(404, "Grup bulunamadı.");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation) { throw new PilotHttpException(409, "Bu grup adı zaten kullanılıyor."); }
        await Run(c, tx, "DELETE FROM project_group_projects WHERE group_id=@g; DELETE FROM project_group_members WHERE group_id=@g", ct, ("g", groupId));
        foreach (var project in (input.Projects ?? []).Distinct())
        {
            if (await Value(c, tx, "SELECT 1 FROM projects WHERE id=@p AND organization_id=@o", ct, ("p", project), ("o", org)) is null)
                throw new PilotHttpException(400, "Proje bu organizasyona ait değil.");
            await Run(c, tx, "INSERT INTO project_group_projects(group_id,project_id) VALUES(@g,@p)", ct, ("g", groupId), ("p", project));
        }
        foreach (var m in input.Members ?? [])
        {
            var email = Email(m.Email); var role = Role(m.Role ?? "viewer");
            var user = await Value(c, tx, "SELECT id FROM app_users WHERE email=@e", ct, ("e", email)) as Guid?
                ?? throw new PilotHttpException(404, $"Kullanıcı bulunamadı: {email}");
            await EnsureUserLimit(c, tx, org, user, ct);
            await Run(c, tx, "INSERT INTO project_group_members(group_id,user_id,role) VALUES(@g,@u,@r) ON CONFLICT(group_id,user_id) DO UPDATE SET role=EXCLUDED.role", ct, ("g", groupId), ("u", user), ("r", role));
        }
        await Security(c, tx, actor, org, id is null ? "org.group.create" : "org.group.update", new RequestContext(null, null), name, ct);
        await tx.CommitAsync(ct);
        return new { id = groupId, name };
    }

    public async Task DeleteGroup(Guid org, Guid actor, Guid id, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        if (await Run(c, null, "DELETE FROM project_groups WHERE id=@id AND organization_id=@o", ct, ("id", id), ("o", org)) == 0) throw new PilotHttpException(404, "Grup bulunamadı.");
        await Security(c, null, actor, org, "org.group.delete", new RequestContext(null, null), id.ToString(), ct);
    }

    // ---------- limits ----------

    public static async Task<(string Plan, PlanLimits Limits, bool TrialExpired)> LimitsFor(NpgsqlConnection c, NpgsqlTransaction? tx, Guid org, CancellationToken ct)
    {
        var r = (await Rows(c, tx, """
            SELECT o.plan,o.trial_ends_at,l.max_projects,l.max_users,l.max_events_per_day,l.max_retention_days
            FROM organizations o LEFT JOIN organization_limits l ON l.organization_id=o.id WHERE o.id=@o
            """, ct, ("o", org)))[0];
        var plan = (string)r["plan"]!;
        var expired = plan == "trial" && r["trial_ends_at"] is DateTime t && t < DateTime.UtcNow;
        return (plan, Plans.Effective(plan, r["max_projects"] as int?, r["max_users"] as int?, r["max_events_per_day"] as int?, r["max_retention_days"] as int?), expired);
    }

    /// <summary>Counts one accepted submission for today and enforces the plan: 402 when the plan is not active, 429 over the daily limit.</summary>
    public async Task CountEvent(Guid org, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var (plan, limits, expired) = await LimitsFor(c, null, org, ct);
        if (plan == "suspended") throw new PilotHttpException(402, "Hesap askıda. Yeni bildirim kabul edilmiyor; destekle iletişime geçin.");
        if (expired) throw new PilotHttpException(402, "Deneme süresi doldu. Bildirim kabulü için bir plan seçilmesi gerekiyor.");
        var events = (long)(await Value(c, null, """
            INSERT INTO usage_daily(organization_id,day,events) VALUES(@o,current_date,1)
            ON CONFLICT(organization_id,day) DO UPDATE SET events=usage_daily.events+1 RETURNING events
            """, ct, ("o", org)))!;
        if (limits.MaxEventsPerDay is { } max && events > max)
        {
            await Run(c, null, "UPDATE usage_daily SET events=events-1, rejected=rejected+1 WHERE organization_id=@o AND day=current_date", ct, ("o", org));
            throw new PilotHttpException(429, $"Günlük olay sınırı ({max:N0}) aşıldı. Bildirimler yarın kabul edilir ya da plan yükseltilmeli.");
        }
    }

    public async Task CountRejected(Guid org, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await Run(c, null, """
            INSERT INTO usage_daily(organization_id,day,events,rejected) VALUES(@o,current_date,0,1)
            ON CONFLICT(organization_id,day) DO UPDATE SET events=greatest(usage_daily.events-1,0), rejected=usage_daily.rejected+1
            """, ct, ("o", org));
    }

    public async Task<object> MyOrganizations(Guid user, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        return await Rows(c, null, """
            SELECT o.id,o.name,o.plan,m.role,o.trial_ends_at FROM organization_members m JOIN organizations o ON o.id=m.organization_id WHERE m.user_id=@u ORDER BY m.created_at
            """, ct, ("u", user));
    }

    public async Task<string?> RoleIn(Guid org, Guid user, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        return await Value(c, null, "SELECT role FROM organization_members WHERE organization_id=@o AND user_id=@u", ct, ("o", org), ("u", user)) as string;
    }

    /// <summary>Grants the system-administrator flag to the configured addresses at startup; the flag is otherwise set by SQL.</summary>
    public async Task SyncSystemAdmins(IEnumerable<string> emails, CancellationToken ct)
    {
        var list = emails.Select(e => e.Trim().ToLowerInvariant()).Where(e => e.Length > 0).ToArray();
        if (list.Length == 0) return;
        await using var c = await source.OpenConnectionAsync(ct);
        await Run(c, null, "UPDATE app_users SET is_system_admin=true WHERE email=ANY(@e) AND NOT is_system_admin", ct, ("e", list));
    }

    public static async Task EnsureProjectLimit(NpgsqlConnection c, NpgsqlTransaction? tx, Guid org, CancellationToken ct)
    {
        var (plan, limits, _) = await LimitsFor(c, tx, org, ct);
        if (plan == "suspended") throw new PilotHttpException(402, "Hesap askıda.");
        if (limits.MaxProjects is { } max && (long)(await Value(c, tx, "SELECT count(*) FROM projects WHERE organization_id=@o", ct, ("o", org)))! >= max)
            throw new PilotHttpException(403, $"Plan en fazla {max} proje izin veriyor.");
    }

    public static async Task EnsureUserLimit(NpgsqlConnection c, NpgsqlTransaction? tx, Guid org, Guid newUser, CancellationToken ct)
    {
        var (_, limits, _) = await LimitsFor(c, tx, org, ct);
        if (limits.MaxUsers is not { } max) return;
        var count = (long)(await Value(c, tx, """
            SELECT count(DISTINCT user_id) FROM (SELECT m.user_id FROM organization_members m WHERE m.organization_id=@o
                UNION SELECT pm.user_id FROM project_members pm JOIN projects p ON p.id=pm.project_id WHERE p.organization_id=@o
                UNION SELECT gm.user_id FROM project_group_members gm JOIN project_groups g ON g.id=gm.group_id WHERE g.organization_id=@o) u
            WHERE u.user_id<>@new
            """, ct, ("o", org), ("new", newUser)))!;
        if (count >= max) throw new PilotHttpException(403, $"Plan en fazla {max} kullanıcı izin veriyor.");
    }

    // ---------- onboarding ----------

    public async Task<object> Onboarding(Guid project, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var r = (await Rows(c, null, """
            SELECT EXISTS(SELECT 1 FROM flows WHERE project_id=@p) flow,
                EXISTS(SELECT 1 FROM api_keys WHERE project_id=@p AND scope IN ('ingest','legacy') AND revoked_at IS NULL) key,
                EXISTS(SELECT 1 FROM transactions WHERE project_id=@p AND NOT is_test) event,
                EXISTS(SELECT 1 FROM notification_channels WHERE project_id=@p AND enabled) channel,
                (SELECT count(*) FROM project_members WHERE project_id=@p)>1 member,
                EXISTS(SELECT 1 FROM connections WHERE project_id=@p) connection
            """, ct, ("p", project)))[0];
        var steps = new[]
        {
            new { key = "flow", title = "Bir akış tanımlayın", done = (bool)r["flow"]!, href = "/flows/new" },
            new { key = "key", title = "Gönderim anahtarı oluşturun", done = (bool)r["key"]!, href = "/settings#keys" },
            new { key = "connection", title = "Kaynak sistemi bağlantı olarak kaydedin", done = (bool)r["connection"]!, href = "/connections" },
            new { key = "event", title = "İlk gerçek bildirimi gönderin", done = (bool)r["event"]!, href = "/flows" },
            new { key = "channel", title = "Bir bildirim kanalı ekleyin", done = (bool)r["channel"]!, href = "/settings#channels" },
            new { key = "member", title = "Bir ekip arkadaşınızı davet edin", done = (bool)r["member"]!, href = "/settings#members" },
        };
        return new { steps, completed = steps.Count(s => s.done), total = steps.Length };
    }

    // ---------- system administration ----------

    public async Task<object> AdminOrganizations(CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        return await Rows(c, null, """
            SELECT o.id,o.name,o.plan,o.trial_ends_at,o.plan_updated_at,o.plan_note,o.created_at,o.deletion_scheduled_at,
                (SELECT count(*) FROM projects p WHERE p.organization_id=o.id) projects,
                (SELECT count(*) FROM organization_members m WHERE m.organization_id=o.id) members,
                (SELECT coalesce(sum(events),0) FROM usage_daily u WHERE u.organization_id=o.id AND u.day>=current_date-29) events_30d,
                (SELECT u.email FROM organization_members m JOIN app_users u ON u.id=m.user_id WHERE m.organization_id=o.id AND m.role='owner' ORDER BY m.created_at LIMIT 1) owner_email
            FROM organizations o ORDER BY o.created_at DESC LIMIT 500
            """, ct);
    }

    public async Task SetPlan(Guid admin, Guid org, PlanInput input, CancellationToken ct)
    {
        if (input.Plan is not null && !Plans.Defaults.ContainsKey(input.Plan)) throw new PilotHttpException(400, "Plan geçersiz.");
        static int? Check(int? v, string field) => v is null or >= 0 ? v : throw new PilotHttpException(400, $"{field} negatif olamaz.");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await Run(c, tx, """
            UPDATE organizations SET plan=COALESCE(@p,plan), plan_updated_at=CASE WHEN @p IS NULL THEN plan_updated_at ELSE clock_timestamp() END,
                plan_note=COALESCE(@n,plan_note),
                trial_ends_at=CASE WHEN @p='trial' THEN clock_timestamp()+make_interval(days => @td) ELSE trial_ends_at END WHERE id=@o
            """, ct, ("p", input.Plan), ("n", input.Note), ("td", input.TrialDays ?? Plans.TrialDays), ("o", org));
        await Run(c, tx, """
            INSERT INTO organization_limits(organization_id,max_projects,max_users,max_events_per_day,max_retention_days)
            VALUES(@o,CAST(@a AS integer),CAST(@b AS integer),CAST(@c AS integer),CAST(@d AS integer))
            ON CONFLICT(organization_id) DO UPDATE SET max_projects=EXCLUDED.max_projects,max_users=EXCLUDED.max_users,
                max_events_per_day=EXCLUDED.max_events_per_day,max_retention_days=EXCLUDED.max_retention_days,updated_at=clock_timestamp()
            """, ct, ("o", org), ("a", Check(input.MaxProjects, "Proje sınırı")), ("b", Check(input.MaxUsers, "Kullanıcı sınırı")),
            ("c", Check(input.MaxEventsPerDay, "Günlük olay sınırı")), ("d", Check(input.MaxRetentionDays, "Saklama sınırı")));
        await Security(c, tx, admin, org, "org.plan", new RequestContext(null, null), $"{input.Plan ?? "-"} {input.Note ?? ""}".Trim(), ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>Organizations whose trial ends in 3 days or ended today; used by the worker for reminder e-mails.</summary>
    public async Task<int> SendTrialReminders(CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var due = await Rows(c, null, """
            SELECT o.id,o.name,o.trial_ends_at,
                CASE WHEN o.trial_ends_at<clock_timestamp() THEN 'trial.ended' ELSE 'trial.ending' END kind
            FROM organizations o WHERE o.plan='trial' AND o.trial_ends_at IS NOT NULL
              AND (o.trial_ends_at BETWEEN clock_timestamp()+interval '2 days' AND clock_timestamp()+interval '3 days'
                   OR o.trial_ends_at BETWEEN clock_timestamp()-interval '1 day' AND clock_timestamp())
              AND NOT EXISTS (SELECT 1 FROM security_events s WHERE s.organization_id=o.id AND s.kind IN ('trial.ending','trial.ended')
                   AND s.kind=CASE WHEN o.trial_ends_at<clock_timestamp() THEN 'trial.ended' ELSE 'trial.ending' END)
            """, ct);
        var sent = 0;
        foreach (var o in due)
        {
            var kind = (string)o["kind"]!;
            var owners = await Rows(c, null, "SELECT u.email,u.name FROM organization_members m JOIN app_users u ON u.id=m.user_id WHERE m.organization_id=@o AND m.role='owner'", ct, ("o", o["id"]));
            foreach (var u in owners)
                await PostgresOperationsQueries.QueueEmail(c, null, (string)u["email"]!,
                    kind == "trial.ended" ? $"IntegraOps · {o["name"]} deneme süresi doldu" : $"IntegraOps · {o["name"]} deneme süresi 3 gün içinde doluyor",
                    $"Merhaba {u["name"]},\n\n\"{o["name"]}\" için deneme süresi {(kind == "trial.ended" ? "doldu" : "3 gün içinde doluyor")}. "
                    + "Bildirim kabulünün kesintisiz sürmesi için bir plan seçilmesi gerekir. Panel → Organizasyon sayfasından destekle iletişime geçebilirsiniz.\n\n"
                    + $"{panel.PanelUrl}/organization\n", ct);
            await Security(c, null, null, (Guid)o["id"]!, kind, new RequestContext(null, null), null, ct);
            sent++;
        }
        return sent;
    }
}
