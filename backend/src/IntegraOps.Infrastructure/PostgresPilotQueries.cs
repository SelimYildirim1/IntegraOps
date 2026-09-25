using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using static IntegraOps.Infrastructure.PilotDb;
using static IntegraOps.Infrastructure.PilotSecurity;

namespace IntegraOps.Infrastructure;

public sealed class PostgresPilotQueries(NpgsqlDataSource source, IDataProtectionProvider protection, NotificationTransport transport)
{
    private readonly IDataProtector protector = protection.CreateProtector("notification-channels.v1");

    public sealed record AlarmFilter(string State, string Flow, string? Search, DateTimeOffset? From, DateTimeOffset? To,
        string Test, Guid? AssignedTo, bool UnassignedOnly, string Step = "", string Cause = "");

    public static AlarmFilter ParseAlarmFilter(string? state, string? flow, string? search, string? from, string? to,
        string? test, string? assigned, Guid? me, string? step = null, string? cause = null)
    {
        state ??= "open";
        var errors = new Dictionary<string, string[]>();
        if (state is not ("open" or "resolved" or "all")) errors["state"] = ["open, resolved veya all kullanın."];
        flow = string.IsNullOrWhiteSpace(flow) ? "" : flow.Trim();
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (search is { Length: > 128 }) errors["search"] = ["Arama metni en fazla 128 karakter olabilir."];
        var fromAt = IntegraOps.Application.InputValidation.DateFilter(errors, "from", from);
        var toAt = IntegraOps.Application.InputValidation.DateFilter(errors, "to", to);
        test = string.IsNullOrWhiteSpace(test) ? "exclude" : test.Trim().ToLowerInvariant();
        if (test is not ("exclude" or "include" or "only")) errors["test"] = ["exclude, include veya only kullanın."];
        Guid? assignedTo = null; var unassigned = false;
        assigned = string.IsNullOrWhiteSpace(assigned) ? "" : assigned.Trim();
        if (assigned == "me") assignedTo = me ?? throw new PilotHttpException(400, "assigned=me için oturum gerekli.");
        else if (assigned == "none") unassigned = true;
        else if (assigned.Length > 0 && !Guid.TryParse(assigned, out var parsed)) errors["assigned"] = ["me, none veya kullanıcı kimliği kullanın."];
        else if (assigned.Length > 0) assignedTo = Guid.Parse(assigned);
        step = string.IsNullOrWhiteSpace(step) ? "" : step.Trim();
        if (step.Length > 64) errors["step"] = ["Adım kodu en fazla 64 karakter olabilir."];
        // "none" lists alarms whose cause was never recorded; a class name lists that class.
        cause = string.IsNullOrWhiteSpace(cause) ? "" : cause.Trim();
        if (cause.Length > 0 && cause != "none" && !IntegraOps.Domain.AlarmRules.Causes.Contains(cause)) errors["cause"] = ["Bilinen bir neden sınıfı ya da none kullanın."];
        if (errors.Count > 0) throw new IntegraOps.Application.RequestValidationException(errors);
        return new AlarmFilter(state, flow, search, fromAt, toAt, test, assignedTo, unassigned, step, cause);
    }

    private const string AlarmWhere = """
        FROM alarms a JOIN transactions t ON t.id=a.transaction_id JOIN flows f ON f.id=t.flow_id
        LEFT JOIN app_users asg ON asg.id=a.assigned_to
        WHERE a.project_id=@p AND (@s='all' OR (@s='open' AND a.resolved_at IS NULL) OR (@s='resolved' AND a.resolved_at IS NOT NULL))
            AND (@f='' OR f.code=@f)
            AND (CAST(@search AS text) IS NULL OR t.business_id ILIKE '%' || @search || '%')
            AND (CAST(@from AS timestamptz) IS NULL OR a.opened_at >= @from)
            AND (CAST(@to AS timestamptz) IS NULL OR a.opened_at < @to)
            AND (@test = 'include' OR (@test = 'only' AND t.is_test) OR (@test = 'exclude' AND NOT t.is_test))
            AND (CAST(@assigned AS uuid) IS NULL OR a.assigned_to = @assigned)
            AND (NOT @unassigned OR a.assigned_to IS NULL)
            AND (@step='' OR a.step_code=@step)
            AND (@cause='' OR (@cause='none' AND a.cause IS NULL) OR a.cause=@cause)
        """;
    private const string AlarmSelect = "SELECT a.*,f.code flow,t.business_id,t.is_test,asg.name assigned_name ";

    private static (string, object?)[] AlarmArgs(Guid project, AlarmFilter f) =>
        [("p", project), ("s", f.State), ("f", f.Flow), ("search", f.Search is null ? null : PostgresTransactionQueries.EscapeLike(f.Search)),
         ("from", f.From), ("to", f.To), ("test", f.Test), ("assigned", f.AssignedTo), ("unassigned", f.UnassignedOnly), ("step", f.Step), ("cause", f.Cause)];

    public async Task<object> Alarms(Guid project, AlarmFilter filter, int page, CancellationToken ct)
    {
        if (page is < 1 or > 100000) throw new PilotHttpException(400, "Sayfa geçersiz.");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var args = AlarmArgs(project, filter).Append(("skip", (object?)((page - 1) * 25))).ToArray();
        var total = await Value(c, tx, "SELECT count(*) " + AlarmWhere, ct, args);
        var items = await Rows(c, tx, AlarmSelect + AlarmWhere + " ORDER BY a.opened_at DESC,a.id DESC LIMIT 25 OFFSET @skip", ct, args);
        await tx.CommitAsync(ct);
        return new { items, total, page, pageSize = 25 };
    }

    /// <summary>Streams alarm rows for CSV export; bounded so one request cannot dump an unbounded table.</summary>
    public async Task<List<Dictionary<string, object?>>> AlarmsForExport(Guid project, AlarmFilter filter, int limit, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        return await Rows(c, null, AlarmSelect + AlarmWhere + " ORDER BY a.opened_at DESC,a.id DESC LIMIT @limit", ct,
            AlarmArgs(project, filter).Append(("limit", (object?)limit)).ToArray());
    }

    public async Task<object> Alarm(Guid project, Guid id, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var alarms = await Rows(c, null, """
            SELECT a.*,f.code flow,t.flow_id,t.business_id,t.is_test,asg.name assigned_name,cb.name cause_set_by_name,s.name step_name,s.instructions,s.link_template
            FROM alarms a JOIN transactions t ON t.id=a.transaction_id
            JOIN flows f ON f.id=t.flow_id LEFT JOIN app_users asg ON asg.id=a.assigned_to LEFT JOIN app_users cb ON cb.id=a.cause_set_by
            LEFT JOIN flow_version_steps s ON s.version_id=t.flow_version_id AND s.code=a.step_code
            WHERE a.project_id=@p AND a.id=@id
            """, ct, ("p", project), ("id", id));
        if (alarms.Count == 0) throw new PilotHttpException(404, "Alarm bu projede bulunamadı.");
        var alarm = alarms[0];
        // Test transactions remember only test history, so demo data never shapes real advice.
        var memory = await Memory(c, project, (Guid)alarm["flow_id"]!, (string)alarm["step_code"]!, (string)alarm["kind"]!, id, (bool)alarm["is_test"]!, ct);
        alarm["instructions"] = alarm["instructions"] as string ?? "";
        alarm["link"] = IntegraOps.Domain.StepLinks.Render(alarm["link_template"] as string ?? "", (string)alarm["flow"]!, (string)alarm["business_id"]!, (string)alarm["step_code"]!);
        alarm.Remove("link_template");
        var notes = await Rows(c, null, """
            SELECT n.id,n.body,n.created_at,u.name author FROM alarm_notes n JOIN app_users u ON u.id=n.author_id
            WHERE n.alarm_id=@id ORDER BY n.created_at,n.id
            """, ct, ("id", id));
        var events = await Rows(c, null, """
            SELECT e.id,e.kind,e.created_at,u.name actor FROM alarm_events e LEFT JOIN app_users u ON u.id=e.actor_id
            WHERE e.alarm_id=@id ORDER BY e.created_at,e.id
            """, ct, ("id", id));
        var deliveries = await Rows(c, null, """
            SELECT d.id,c.name channel,c.kind,e.kind event,d.state,d.attempts,d.available_at,d.last_error,d.sent_at
            FROM notification_deliveries d JOIN alarm_events e ON e.id=d.event_id
            JOIN notification_channels c ON c.id=d.channel_id WHERE e.alarm_id=@id ORDER BY d.created_at,d.id
            """, ct, ("id", id));
        // Signals decide whether the AI card has anything to add beyond the alarm text; the page hides it otherwise.
        var similar = (long)(await Value(c, null, """
            SELECT count(*) FROM alarms x JOIN transactions t ON t.id=x.transaction_id
            WHERE x.project_id=@p AND t.flow_id=@f AND x.step_code=@s AND x.id<>@id
              AND x.opened_at BETWEEN CAST(@at AS timestamptz)-interval '6 hours' AND CAST(@at AS timestamptz)+interval '6 hours'
            """, ct, ("p", project), ("f", alarm["flow_id"]), ("s", alarm["step_code"]), ("id", id), ("at", alarm["opened_at"])))!;
        var silent = (long)(await Value(c, null, SilentSourcesSql, ct, ("p", project)))!;
        var context = new { similar_recent = similar, silent_sources = silent, dead_deliveries = deliveries.Count(d => (string)d["state"]! == "dead") };
        alarm.Remove("flow_id");
        return new { alarm = alarms[0], events, deliveries, notes, memory, context };
    }

    /// <summary>
    /// What happened the last times this flow and step raised the same kind of alarm: the cause distribution and a few
    /// examples with the recorded fix. Shown on the alarm page without AI; the assistant cites the same rows.
    /// </summary>
    internal static async Task<object> Memory(NpgsqlConnection c, Guid project, Guid flow, string step, string kind, Guid? exclude, bool isTest, CancellationToken ct)
    {
        const int windowDays = 90;
        var causes = await Rows(c, null, """
            SELECT a.cause,count(*) count FROM alarms a JOIN transactions t ON t.id=a.transaction_id
            WHERE a.project_id=@p AND t.flow_id=@f AND a.step_code=@s AND a.kind=@k AND a.id<>COALESCE(CAST(@x AS uuid),'00000000-0000-0000-0000-000000000000')
              AND a.opened_at > clock_timestamp() - make_interval(days => @d) AND t.is_test=@test
            GROUP BY a.cause ORDER BY count DESC, a.cause
            """, ct, ("p", project), ("f", flow), ("s", step), ("k", kind), ("x", exclude), ("d", windowDays), ("test", isTest));
        var recent = await Rows(c, null, """
            SELECT a.id,t.business_id,a.opened_at,a.resolved_at,a.cause,a.fix,a.error,u.name cause_set_by_name,
                CASE WHEN a.resolved_at IS NULL THEN NULL ELSE CAST(extract(epoch FROM a.resolved_at-a.opened_at) AS bigint) END resolved_after_seconds,
                (SELECT n.body FROM alarm_notes n WHERE n.alarm_id=a.id ORDER BY n.created_at DESC LIMIT 1) note
            FROM alarms a JOIN transactions t ON t.id=a.transaction_id LEFT JOIN app_users u ON u.id=a.cause_set_by
            WHERE a.project_id=@p AND t.flow_id=@f AND a.step_code=@s AND a.kind=@k AND a.id<>COALESCE(CAST(@x AS uuid),'00000000-0000-0000-0000-000000000000')
              AND a.opened_at > clock_timestamp() - make_interval(days => @d) AND t.is_test=@test
            ORDER BY a.cause IS NULL, a.fix IS NULL, a.opened_at DESC LIMIT 5
            """, ct, ("p", project), ("f", flow), ("s", step), ("k", kind), ("x", exclude), ("d", windowDays), ("test", isTest));
        var total = causes.Sum(r => (long)r["count"]!);
        return new
        {
            window_days = windowDays, total, with_cause = causes.Where(r => r["cause"] is not null).Sum(r => (long)r["count"]!),
            causes = causes.Where(r => r["cause"] is not null).Select(r => new { cause = r["cause"], count = r["count"] }),
            recent,
        };
    }

    /// <summary>
    /// What the assistant could add for a transaction beyond its timeline: resolution memory, simultaneous alarms,
    /// silent sources and dead deliveries of its most relevant alarm. Empty when the transaction has no alarm.
    /// </summary>
    public async Task<object> TransactionSignals(Guid project, string flow, string transactionId, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var rows = await Rows(c, null, """
            SELECT a.id,a.kind,a.step_code,a.opened_at,t.flow_id,t.is_test FROM alarms a JOIN transactions t ON t.id=a.transaction_id JOIN flows f ON f.id=t.flow_id
            WHERE a.project_id=@p AND f.code=@flow AND t.business_id=@b ORDER BY a.resolved_at IS NOT NULL, a.opened_at DESC LIMIT 1
            """, ct, ("p", project), ("flow", flow), ("b", transactionId));
        if (rows.Count == 0) return new { alarm_id = (Guid?)null, memory_total = 0L, similar_recent = 0L, silent_sources = 0L, dead_deliveries = 0L };
        var a = rows[0]; var id = (Guid)a["id"]!;
        // Same definition as the alarm page's memory panel: every alarm of the last 90 days, open ones included.
        var memoryTotal = (long)(await Value(c, null, """
            SELECT count(*) FROM alarms x JOIN transactions t ON t.id=x.transaction_id
            WHERE x.project_id=@p AND t.flow_id=@f AND x.step_code=@s AND x.kind=@k AND x.id<>@id AND t.is_test=@test
              AND x.opened_at > clock_timestamp()-interval '90 days'
            """, ct, ("p", project), ("f", a["flow_id"]), ("s", a["step_code"]), ("k", a["kind"]), ("id", id), ("test", a["is_test"])))!;
        var similar = (long)(await Value(c, null, """
            SELECT count(*) FROM alarms x JOIN transactions t ON t.id=x.transaction_id
            WHERE x.project_id=@p AND t.flow_id=@f AND x.step_code=@s AND x.id<>@id
              AND x.opened_at BETWEEN CAST(@at AS timestamptz)-interval '6 hours' AND CAST(@at AS timestamptz)+interval '6 hours'
            """, ct, ("p", project), ("f", a["flow_id"]), ("s", a["step_code"]), ("id", id), ("at", a["opened_at"])))!;
        var silent = (long)(await Value(c, null, SilentSourcesSql, ct, ("p", project)))!;
        var dead = (long)(await Value(c, null, "SELECT count(*) FROM notification_deliveries d JOIN alarm_events e ON e.id=d.event_id WHERE e.alarm_id=@id AND d.state='dead'", ct, ("id", id)))!;
        return new { alarm_id = (Guid?)id, memory_total = memoryTotal, similar_recent = similar, silent_sources = silent, dead_deliveries = dead };
    }

    private const string SilentSourcesSql = """
        SELECT (SELECT count(*) FROM connections WHERE project_id=@p AND last_event_at < clock_timestamp()-interval '15 minutes' AND last_event_at > clock_timestamp()-interval '24 hours')
             + (SELECT count(*) FROM agent_heartbeats WHERE project_id=@p AND seen_at < clock_timestamp()-interval '5 minutes' AND seen_at > clock_timestamp()-interval '24 hours')
        """;

    public sealed record MemoryDigest(long Total, string? Cause, long CauseCount, string? LastFix);

    /// <summary>Dominant recorded cause and the latest fix for a flow, step and alarm kind; used per triage group.</summary>
    internal static async Task<MemoryDigest> MemorySummary(NpgsqlConnection c, Guid project, Guid flow, string step, string kind, bool isTest, CancellationToken ct)
    {
        var rows = await Rows(c, null, """
            SELECT count(*) total,
                (SELECT x.cause FROM alarms x JOIN transactions tx ON tx.id=x.transaction_id WHERE x.project_id=@p AND tx.flow_id=@f AND x.step_code=@s AND x.kind=@k AND x.cause IS NOT NULL
                   AND x.opened_at > clock_timestamp()-interval '90 days' AND tx.is_test=@test GROUP BY x.cause ORDER BY count(*) DESC,x.cause LIMIT 1) cause,
                (SELECT x.fix FROM alarms x JOIN transactions tx ON tx.id=x.transaction_id WHERE x.project_id=@p AND tx.flow_id=@f AND x.step_code=@s AND x.kind=@k AND x.fix IS NOT NULL
                   AND tx.is_test=@test ORDER BY x.cause_set_at DESC LIMIT 1) last_fix
            FROM alarms a JOIN transactions t ON t.id=a.transaction_id
            WHERE a.project_id=@p AND t.flow_id=@f AND a.step_code=@s AND a.kind=@k AND a.resolved_at IS NOT NULL
              AND a.opened_at > clock_timestamp()-interval '90 days' AND t.is_test=@test
            """, ct, ("p", project), ("f", flow), ("s", step), ("k", kind), ("test", isTest));
        var r = rows[0]; var cause = r["cause"] as string;
        long causeCount = cause is null ? 0 : (long)(await Value(c, null, """
            SELECT count(*) FROM alarms x JOIN transactions tx ON tx.id=x.transaction_id WHERE x.project_id=@p AND tx.flow_id=@f AND x.step_code=@s AND x.kind=@k AND x.cause=@c
              AND x.opened_at > clock_timestamp()-interval '90 days' AND tx.is_test=@test
            """, ct, ("p", project), ("f", flow), ("s", step), ("k", kind), ("c", cause), ("test", isTest)))!;
        return new((long)r["total"]!, cause, causeCount, r["last_fix"] as string);
    }

    /// <summary>Assigns or acknowledges up to 200 alarms of one project in a single transaction with one audit row.</summary>
    public async Task<object> Bulk(ProjectIdentity actor, List<Guid>? ids, string? op, string? userId, CancellationToken ct)
    {
        if (ids is null || ids.Count == 0) throw new PilotHttpException(400, "En az bir alarm seçin.");
        if (ids.Count > 200) throw new PilotHttpException(400, "Tek seferde en fazla 200 alarm işlenebilir.");
        if (op is not ("assign" or "acknowledge")) throw new PilotHttpException(400, "İşlem assign ya da acknowledge olmalı.");
        Guid? assignee = op != "assign" ? null : userId == "me" ? actor.UserId : Guid.TryParse(userId, out var parsed) ? parsed : null;
        if (op == "assign" && assignee is null) throw new PilotHttpException(400, "Sorumlu seçin.");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        if (assignee is { } target && await Value(c, tx, "SELECT 1 FROM project_members WHERE project_id=@p AND user_id=@u AND role IN ('owner','operator')", ct,
            ("p", actor.ProjectId), ("u", target)) is null)
            throw new PilotHttpException(400, "Sorumlu bu projede sahip ya da operatör olmalı.");
        var changed = await Rows(c, tx, op == "assign" ? """
            UPDATE alarms SET assigned_to=@a,assigned_at=clock_timestamp() WHERE project_id=@p AND id=ANY(@ids) AND assigned_to IS DISTINCT FROM @a RETURNING id
            """ : """
            UPDATE alarms SET acknowledged_at=clock_timestamp(),acknowledged_by=@a WHERE project_id=@p AND id=ANY(@ids) AND acknowledged_at IS NULL RETURNING id
            """, ct, ("p", actor.ProjectId), ("ids", ids.Distinct().ToArray()), ("a", op == "assign" ? assignee : actor.UserId));
        foreach (var row in changed)
            await PostgresAlarmStore.Record(c, tx, (Guid)row["id"]!, op == "assign" ? "assigned" : "acknowledged", actor.UserId, ct);
        await Audit(c, tx, actor.ProjectId, actor.UserId, "alarm.bulk_" + op, $"{changed.Count}/{ids.Count}", ct);
        await tx.CommitAsync(ct);
        return new { changed = changed.Count, requested = ids.Count };
    }

    /// <summary>Records the operator's root cause and fix; allowed while the alarm is open and after it closed.</summary>
    public async Task SetCause(ProjectIdentity actor, Guid id, string? cause, string? fix, CancellationToken ct)
    {
        cause = cause?.Trim() ?? "";
        if (!IntegraOps.Domain.AlarmRules.Causes.Contains(cause)) throw new PilotHttpException(400, "Bilinen bir neden sınıfı seçin.");
        fix = string.IsNullOrWhiteSpace(fix) ? null : fix.Trim();
        if (fix is { Length: > 1000 }) throw new PilotHttpException(400, "Çözüm açıklaması en fazla 1000 karakter olabilir.");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        if (await Value(c, tx, "SELECT 1 FROM alarms WHERE id=@id AND project_id=@p FOR UPDATE", ct, ("p", actor.ProjectId), ("id", id)) is null)
            throw new PilotHttpException(404, "Alarm bulunamadı.");
        await Run(c, tx, "UPDATE alarms SET cause=@c,fix=@f,cause_set_at=clock_timestamp(),cause_set_by=CAST(@u AS uuid) WHERE id=@id", ct,
            ("id", id), ("c", cause), ("f", fix), ("u", actor.UserId));
        await PostgresAlarmStore.Record(c, tx, id, "cause_set", actor.UserId, ct);
        await Audit(c, tx, actor.ProjectId, actor.UserId, "alarm.cause", id + " " + cause, ct);
        await tx.CommitAsync(ct);
    }

    public async Task Assign(ProjectIdentity actor, Guid id, Guid? assignee, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var current = await Value(c, tx, "SELECT assigned_to FROM alarms WHERE id=@id AND project_id=@p FOR UPDATE", ct,
            ("p", actor.ProjectId), ("id", id));
        if (await Value(c, tx, "SELECT 1 FROM alarms WHERE id=@id AND project_id=@p", ct, ("p", actor.ProjectId), ("id", id)) is null)
            throw new PilotHttpException(404, "Alarm bulunamadı.");
        if (assignee is { } target && await Value(c, tx, "SELECT 1 FROM project_members WHERE project_id=@p AND user_id=@u AND role IN ('owner','operator')", ct,
            ("p", actor.ProjectId), ("u", target)) is null)
            throw new PilotHttpException(400, "Sorumlu bu projede sahip ya da operatör olmalı.");
        if ((current as Guid?) == assignee) { await tx.CommitAsync(ct); return; }
        await Run(c, tx, "UPDATE alarms SET assigned_to=CAST(@a AS uuid),assigned_at=CASE WHEN @a IS NULL THEN NULL ELSE clock_timestamp() END WHERE id=@id", ct,
            ("a", assignee), ("id", id));
        await PostgresAlarmStore.Record(c, tx, id, assignee is null ? "unassigned" : "assigned", actor.UserId, ct);
        await Audit(c, tx, actor.ProjectId, actor.UserId, assignee is null ? "alarm.unassign" : "alarm.assign", id.ToString(), ct);
        await tx.CommitAsync(ct);
    }

    public async Task<object> AddNote(ProjectIdentity actor, Guid id, string? body, CancellationToken ct)
    {
        body = Text(body, 2000, "Not");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        if (await Value(c, tx, "SELECT 1 FROM alarms WHERE id=@id AND project_id=@p", ct, ("p", actor.ProjectId), ("id", id)) is null)
            throw new PilotHttpException(404, "Alarm bulunamadı.");
        var noteId = Guid.CreateVersion7();
        await Run(c, tx, "INSERT INTO alarm_notes(id,alarm_id,author_id,body) VALUES(@n,@a,@u,@b)", ct,
            ("n", noteId), ("a", id), ("u", actor.UserId), ("b", body));
        await PostgresAlarmStore.Record(c, tx, id, "noted", actor.UserId, ct);
        await Audit(c, tx, actor.ProjectId, actor.UserId, "alarm.note", id.ToString(), ct);
        await tx.CommitAsync(ct);
        return new { id = noteId };
    }

    public async Task Acknowledge(ProjectIdentity actor, Guid id, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        if (await Value(c, tx, "SELECT id FROM alarms WHERE id=@id AND project_id=@p FOR UPDATE", ct,
            ("p", actor.ProjectId), ("id", id)) is null) throw new PilotHttpException(404, "Alarm bulunamadı.");
        if (await Run(c, tx, """
            UPDATE alarms SET acknowledged_at=clock_timestamp(),acknowledged_by=@u WHERE id=@id AND acknowledged_at IS NULL
            """, ct, ("id", id), ("u", actor.UserId)) > 0)
        {
            await PostgresAlarmStore.Record(c, tx, id, "acknowledged", actor.UserId, ct);
            await Audit(c, tx, actor.ProjectId, actor.UserId, "alarm.acknowledge", id.ToString(), ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task Retry(ProjectIdentity actor, Guid id, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        if (await Run(c, tx, """
            UPDATE notification_deliveries d SET state='pending',attempts=0,available_at=clock_timestamp(),last_error=NULL
            FROM notification_channels c WHERE d.channel_id=c.id AND c.project_id=@p AND c.enabled
                AND d.id=@id AND d.state='dead'
            """, ct, ("p", actor.ProjectId), ("id", id)) == 0)
            throw new PilotHttpException(409, "Bu projede yeniden denenebilir bir bildirim bulunamadı. Kanal da etkin olmalı.");
        await Audit(c, tx, actor.ProjectId, actor.UserId, "delivery.retry", id.ToString(), ct);
        await tx.CommitAsync(ct);
    }

    public async Task<object> Channels(Guid project, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var rows = await Rows(c, null, "SELECT id,name,kind,enabled,created_at,target_protected FROM notification_channels WHERE project_id=@p ORDER BY created_at", ct, ("p", project));
        foreach (var row in rows)
        {
            var target = protector.Unprotect((string)row["target_protected"]!);
            row.Remove("target_protected");
            // Chat webhook URLs carry their secret in the path; only the host is shown.
            row["target"] = row["kind"] is "webhook" or "slack" or "teams" ? new Uri(target).GetLeftPart(UriPartial.Authority) : target;
        }
        return rows;
    }

    public async Task<object> CreateChannel(ProjectIdentity actor, ChannelInput input, CancellationToken ct)
    {
        var name = Text(input.Name, 120, "Kanal adı"); var target = Text(input.Target, 2048, "Hedef");
        string? secret = null;
        if (input.Kind == "email") target = Email(target);
        else if (input.Kind == "webhook")
        {
            transport.ValidateTarget(target);
            secret = string.IsNullOrEmpty(input.Secret) ? Token() : input.Secret;
            if (secret.Length is < 32 or > 128) throw new PilotHttpException(400, "Webhook sırrı 32–128 karakter olmalı.");
        }
        else if (input.Kind is "slack" or "teams") transport.ValidateTarget(target, input.Kind); // the URL itself is the secret
        else throw new PilotHttpException(400, "Kanal türü email, webhook, slack veya teams olmalı.");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var id = Guid.CreateVersion7();
        try
        {
            await Run(c, tx, """
                INSERT INTO notification_channels(id,project_id,name,kind,target_protected,secret_protected)
                VALUES(@id,@p,@n,@k,@t,@s)
                """, ct, ("id", id), ("p", actor.ProjectId), ("n", name), ("k", input.Kind),
                ("t", protector.Protect(target)), ("s", secret is null ? null : protector.Protect(secret)));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        { throw new PilotHttpException(409, "Bu kanal adı zaten kullanılıyor."); }
        await Audit(c, tx, actor.ProjectId, actor.UserId, "channel.create", id.ToString(), ct);
        await tx.CommitAsync(ct);
        return new { id, name, secret };
    }

    public async Task SetChannel(ProjectIdentity actor, Guid id, bool enabled, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        if (await Run(c, tx, "UPDATE notification_channels SET enabled=@e WHERE project_id=@p AND id=@id", ct,
            ("p", actor.ProjectId), ("id", id), ("e", enabled)) == 0) throw new PilotHttpException(404, "Kanal bulunamadı.");
        if (!enabled)
            await Run(c, tx, "UPDATE notification_deliveries SET state='cancelled' WHERE channel_id=@id AND state='pending'", ct, ("id", id));
        await Audit(c, tx, actor.ProjectId, actor.UserId, enabled ? "channel.enable" : "channel.disable", id.ToString(), ct);
        await tx.CommitAsync(ct);
    }

    public async Task Heartbeat(ProjectIdentity actor, AgentHeartbeatInput input, CancellationToken ct)
    {
        var name = Text(input.Name, 64, "Ajan adı");
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")) throw new PilotHttpException(400, "Ajan adı geçersiz.");
        var version = Text(input.Version, 64, "Sürüm");
        var host = (input.Host ?? "").Trim(); if (host.Length > 200) host = host[..200];
        var kind = (input.SourceKind ?? "").Trim(); if (kind.Length > 32) throw new PilotHttpException(400, "Kaynak türü geçersiz.");
        var lastError = string.IsNullOrWhiteSpace(input.LastError) ? null : input.LastError.Trim();
        if (lastError is { Length: > 200 }) lastError = lastError[..200];
        DateTimeOffset? lastSuccess = null;
        if (!string.IsNullOrWhiteSpace(input.LastSuccessAt))
        {
            var errors = new Dictionary<string, string[]>();
            lastSuccess = IntegraOps.Application.InputValidation.DateFilter(errors, "lastSuccessAt", input.LastSuccessAt);
            if (errors.Count > 0) throw new PilotHttpException(400, "lastSuccessAt ISO 8601 olmalı.");
        }
        var config = input.Config is { ValueKind: System.Text.Json.JsonValueKind.Object } cfg ? cfg.GetRawText() : "{}";
        if (config.Length > 4000) throw new PilotHttpException(400, "Yapılandırma özeti çok büyük.");
        await using var c = await source.OpenConnectionAsync(ct);
        await Run(c, null, """
            INSERT INTO agent_heartbeats(project_id,name,version,host,source_kind,pending,sent_total,dead_total,retry_total,last_error,last_success_at,config)
            VALUES(@p,@n,@v,@h,@k,@pending,@sent,@dead,@retry,@err,CAST(@ok AS timestamptz),CAST(@cfg AS jsonb))
            ON CONFLICT(project_id,name) DO UPDATE SET version=EXCLUDED.version,host=EXCLUDED.host,source_kind=EXCLUDED.source_kind,
                pending=EXCLUDED.pending,sent_total=EXCLUDED.sent_total,dead_total=EXCLUDED.dead_total,retry_total=EXCLUDED.retry_total,
                last_error=EXCLUDED.last_error,last_success_at=EXCLUDED.last_success_at,config=EXCLUDED.config,seen_at=clock_timestamp()
            """, ct, ("p", actor.ProjectId), ("n", name), ("v", version), ("h", host), ("k", kind), ("pending", Math.Max(0, input.Pending ?? 0)),
            ("sent", Math.Max(0, input.SentTotal ?? 0)), ("dead", Math.Max(0, input.DeadTotal ?? 0)), ("retry", Math.Max(0, input.RetryTotal ?? 0)),
            ("err", lastError), ("ok", lastSuccess), ("cfg", config));
    }

    public async Task<object> Agents(Guid project, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var rows = await Rows(c, null, """
            SELECT name,version,host,source_kind,pending,sent_total,dead_total,retry_total,last_error,last_success_at,config,first_seen_at,seen_at,
                seen_at < clock_timestamp() - interval '2 minutes' AS stale
            FROM agent_heartbeats WHERE project_id=@p ORDER BY name
            """, ct, ("p", project));
        foreach (var row in rows) // jsonb arrives as text; hand the panel a real object
            row["config"] = System.Text.Json.JsonDocument.Parse((string)row["config"]!).RootElement;
        return rows;
    }

    public async Task<object> Operations(Guid project, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var counts = (await Rows(c, null, """
            SELECT
                (SELECT count(*) FROM outbox_messages WHERE project_id=@p AND published_at IS NULL) outbox_pending,
                (SELECT min(created_at) FROM outbox_messages WHERE project_id=@p AND published_at IS NULL) oldest_outbox_at,
                (SELECT count(*) FROM transaction_events WHERE project_id=@p AND processed_at IS NULL) events_pending,
                (SELECT count(*) FROM notification_deliveries d JOIN notification_channels n ON n.id=d.channel_id
                    WHERE n.project_id=@p AND d.state IN ('pending','sending')) notifications_pending,
                (SELECT count(*) FROM notification_deliveries d JOIN notification_channels n ON n.id=d.channel_id
                    WHERE n.project_id=@p AND d.state='dead') notifications_dead,
                (SELECT max(seen_at) FROM worker_heartbeats) worker_seen_at,
                clock_timestamp() checked_at
            """, ct, ("p", project)))[0];
        var audit = await Rows(c, null, """
            SELECT a.action,a.target,a.created_at,u.name actor FROM audit_log a LEFT JOIN app_users u ON u.id=a.actor_id
            WHERE a.project_id=@p ORDER BY a.id DESC LIMIT 100
            """, ct, ("p", project));
        return new { counts, audit };
    }
}
