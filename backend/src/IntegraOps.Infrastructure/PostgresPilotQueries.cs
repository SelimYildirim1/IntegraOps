using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using static IntegraOps.Infrastructure.PilotDb;
using static IntegraOps.Infrastructure.PilotSecurity;

namespace IntegraOps.Infrastructure;

public sealed class PostgresPilotQueries(NpgsqlDataSource source, IDataProtectionProvider protection, NotificationTransport transport)
{
    private readonly IDataProtector protector = protection.CreateProtector("notification-channels.v1");

    public sealed record AlarmFilter(string State, string Flow, string? Search, DateTimeOffset? From, DateTimeOffset? To,
        string Test, Guid? AssignedTo, bool UnassignedOnly);

    public static AlarmFilter ParseAlarmFilter(string? state, string? flow, string? search, string? from, string? to,
        string? test, string? assigned, Guid? me)
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
        if (errors.Count > 0) throw new IntegraOps.Application.RequestValidationException(errors);
        return new AlarmFilter(state, flow, search, fromAt, toAt, test, assignedTo, unassigned);
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
        """;
    private const string AlarmSelect = "SELECT a.*,f.code flow,t.business_id,t.is_test,asg.name assigned_name ";

    private static (string, object?)[] AlarmArgs(Guid project, AlarmFilter f) =>
        [("p", project), ("s", f.State), ("f", f.Flow), ("search", f.Search is null ? null : PostgresTransactionQueries.EscapeLike(f.Search)),
         ("from", f.From), ("to", f.To), ("test", f.Test), ("assigned", f.AssignedTo), ("unassigned", f.UnassignedOnly)];

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
            SELECT a.*,f.code flow,t.business_id,t.is_test,asg.name assigned_name FROM alarms a JOIN transactions t ON t.id=a.transaction_id
            JOIN flows f ON f.id=t.flow_id LEFT JOIN app_users asg ON asg.id=a.assigned_to WHERE a.project_id=@p AND a.id=@id
            """, ct, ("p", project), ("id", id));
        if (alarms.Count == 0) throw new PilotHttpException(404, "Alarm bu projede bulunamadı.");
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
        return new { alarm = alarms[0], events, deliveries, notes };
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
            row["target"] = row["kind"] is "webhook" ? new Uri(target).GetLeftPart(UriPartial.Authority) : target;
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
        else throw new PilotHttpException(400, "Kanal türü email veya webhook olmalı.");
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
