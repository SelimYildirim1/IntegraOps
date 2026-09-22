using System.Text.Json;
using IntegraOps.Domain;
using Npgsql;
using static IntegraOps.Infrastructure.PilotDb;

namespace IntegraOps.Infrastructure;

public static class PostgresAlarmStore
{
    // The caller already holds the transaction row lock. Events and delivery jobs
    // commit atomically with the updated projection; repeated scans cannot re-open an active alarm.
    internal static async Task Sync(NpgsqlConnection c, NpgsqlTransaction tx, FlowDefinition flow,
        Guid transaction, Projection projection, DelayAssessment delay, CancellationToken ct)
    {
        if (delay.State == "pending_processing") return;
        var desired = AlarmRules.Current(projection, delay);
        var active = await Rows(c, tx, "SELECT id,kind,step_code FROM alarms WHERE project_id=@p AND transaction_id=@t AND resolved_at IS NULL",
            ct, ("p", flow.ProjectId), ("t", transaction));
        foreach (var old in active)
        {
            var step = (string)old["step_code"]!; var kind = (string)old["kind"]!; var id = (Guid)old["id"]!;
            if (desired.Any(x => x.Step == step && x.Kind == kind)) continue;
            var reason = AlarmRules.Resolution(projection, step);
            await Run(c, tx, "UPDATE alarms SET resolved_at=clock_timestamp(),resolution=@r WHERE id=@id", ct, ("id", id), ("r", reason));
            await Record(c, tx, id, "resolved", null, ct);
        }
        foreach (var condition in desired)
        {
            var previous = active.FirstOrDefault(x => (string)x["kind"]! == condition.Kind && (string)x["step_code"]! == condition.Step);
            if (previous is not null)
            {
                await Run(c, tx, "UPDATE alarms SET last_seen_at=clock_timestamp(),error=@e,deadline_at=CAST(@d AS timestamptz) WHERE id=@id", ct,
                    ("e", condition.Error), ("d", condition.DeadlineAt), ("id", previous["id"]));
                continue;
            }
            var id = Guid.CreateVersion7();
            await Run(c, tx, """
                INSERT INTO alarms(id,project_id,transaction_id,kind,step_code,error,deadline_at)
                VALUES(@id,@p,@t,@k,@s,@e,CAST(@d AS timestamptz))
                """, ct, ("id", id), ("p", flow.ProjectId), ("t", transaction), ("k", condition.Kind),
                ("s", condition.Step), ("e", condition.Error), ("d", condition.DeadlineAt));
            await Record(c, tx, id, "opened", null, ct);
        }
        await Run(c, tx, "UPDATE transactions SET alarm_checked_at=clock_timestamp() WHERE id=@t", ct, ("t", transaction));
    }

    internal static async Task Record(NpgsqlConnection c, NpgsqlTransaction tx, Guid alarm, string kind, Guid? actor, CancellationToken ct)
    {
        var eventId = Guid.CreateVersion7();
        await Run(c, tx, "INSERT INTO alarm_events(id,alarm_id,kind,actor_id) VALUES(@e,@a,@k,CAST(@u AS uuid))", ct,
            ("e", eventId), ("a", alarm), ("k", kind), ("u", actor));
        if (kind is not ("opened" or "resolved")) return; // Only lifecycle changes notify channels.
        var data = (await Rows(c, tx, """
            SELECT a.id alarm_id,a.project_id,f.code flow,t.business_id transaction_id,a.kind alarm_kind,
                a.step_code,a.error,a.deadline_at,a.opened_at,a.resolved_at,a.resolution
            FROM alarms a JOIN transactions t ON t.id=a.transaction_id JOIN flows f ON f.id=t.flow_id WHERE a.id=@a
            """, ct, ("a", alarm)))[0];
        data["event_id"] = eventId; data["event"] = kind; data["schema_version"] = 1;
        var body = JsonSerializer.Serialize(data);
        var channels = await Rows(c, tx, "SELECT id FROM notification_channels WHERE project_id=@p AND enabled", ct, ("p", data["project_id"]));
        foreach (var channel in channels)
            await Run(c, tx, "INSERT INTO notification_deliveries(id,event_id,channel_id,body) VALUES(@id,@e,@c,@b)", ct,
                ("id", Guid.CreateVersion7()), ("e", eventId), ("c", channel["id"]), ("b", body));
    }
}
