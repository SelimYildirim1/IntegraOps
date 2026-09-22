using IntegraOps.Domain;
using Npgsql;
using static IntegraOps.Infrastructure.PilotDb;

namespace IntegraOps.Infrastructure;

/// <summary>Reads flow versions, calendars and group overrides into domain definitions.</summary>
public static class FlowVersions
{
    public static async Task<FlowDefinition?> LoadAsync(NpgsqlConnection c, NpgsqlTransaction? tx, Guid projectId,
        string? code, Guid? flowId, Guid? versionId, CancellationToken ct)
    {
        // Without a version id the newest published version whose effective time has passed applies.
        var rows = await Rows(c, tx, """
            SELECT f.id flow_id, f.code, f.name, v.id version_id, v.version, v.calendar_id
            FROM flows f JOIN flow_versions v ON v.flow_id = f.id
            WHERE f.project_id = @p AND (f.code = @code OR f.id = CAST(@flowId AS uuid) OR v.id = CAST(@versionId AS uuid))
              AND (v.id = CAST(@versionId AS uuid)
                   OR (CAST(@versionId AS uuid) IS NULL AND v.state = 'published' AND v.effective_from <= clock_timestamp()))
            ORDER BY v.version DESC LIMIT 1
            """, ct, ("p", projectId), ("code", code), ("flowId", flowId), ("versionId", versionId));
        if (rows.Count == 0) return null;
        var r = rows[0];
        var id = (Guid)r["version_id"]!;
        var steps = await LoadStepsAsync(c, tx, id, ct);
        var calendar = r["calendar_id"] is Guid calendarId ? await LoadCalendarAsync(c, tx, calendarId, ct) : null;
        return new FlowDefinition((Guid)r["flow_id"]!, projectId, (string)r["code"]!, (string)r["name"]!, steps, id, (int)r["version"]!, calendar);
    }

    public static async Task<IReadOnlyList<FlowStep>> LoadStepsAsync(NpgsqlConnection c, NpgsqlTransaction? tx, Guid versionId, CancellationToken ct)
    {
        var stepRows = await Rows(c, tx, "SELECT code,name,position,max_delay_seconds FROM flow_version_steps WHERE version_id=@v ORDER BY position", ct, ("v", versionId));
        var overrideRows = await Rows(c, tx, "SELECT step_code,group_code,max_delay_seconds FROM flow_step_overrides WHERE version_id=@v", ct, ("v", versionId));
        var overrides = overrideRows.GroupBy(o => (string)o["step_code"]!)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, int>)g.ToDictionary(o => (string)o["group_code"]!, o => (int)o["max_delay_seconds"]!));
        return stepRows.Select(s => new FlowStep((string)s["code"]!, (string)s["name"]!, (short)s["position"]!, s["max_delay_seconds"] as int?,
            overrides.GetValueOrDefault((string)s["code"]!))).ToArray();
    }

    public static async Task<BusinessCalendar?> LoadCalendarAsync(NpgsqlConnection c, NpgsqlTransaction? tx, Guid id, CancellationToken ct)
    {
        var rows = await Rows(c, tx, "SELECT id,name,timezone,work_days,start_time,end_time FROM business_calendars WHERE id=@id", ct, ("id", id));
        if (rows.Count == 0) return null;
        var holidays = await Rows(c, tx, "SELECT day FROM calendar_holidays WHERE calendar_id=@id", ct, ("id", id));
        var r = rows[0];
        return new BusinessCalendar(id, (string)r["name"]!, (string)r["timezone"]!,
            BusinessCalendar.Days(((short[])r["work_days"]!).Select(d => (int)d)),
            (TimeOnly)r["start_time"]!, (TimeOnly)r["end_time"]!,
            holidays.Select(h => (DateOnly)h["day"]!).ToHashSet());
    }

    public static async Task InsertStepsAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid versionId, IReadOnlyList<FlowStep> steps, CancellationToken ct)
    {
        foreach (var step in steps)
        {
            await Run(c, tx, """
                INSERT INTO flow_version_steps (version_id, code, name, position, max_delay_seconds)
                VALUES (@v, @code, @name, @position, CAST(@delay AS integer))
                """, ct, ("v", versionId), ("code", step.Code), ("name", step.Name), ("position", (short)step.Position), ("delay", step.MaxDelaySeconds));
            foreach (var (group, seconds) in step.Overrides ?? new Dictionary<string, int>())
                await Run(c, tx, "INSERT INTO flow_step_overrides (version_id, step_code, group_code, max_delay_seconds) VALUES (@v, @s, @g, @d)", ct,
                    ("v", versionId), ("s", step.Code), ("g", group), ("d", seconds));
        }
    }

    /// <summary>Records realised durations of completed steps; rerunning for the same transaction is idempotent.</summary>
    public static async Task UpsertStepStatsAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid projectId, Guid transactionId,
        FlowDefinition flow, Projection projection, CancellationToken ct)
    {
        foreach (var stat in StepStats.Compute(flow, projection))
            await Run(c, tx, """
                INSERT INTO transaction_step_stats (transaction_id, project_id, step_code, started_at, completed_at, elapsed_seconds, allowance_seconds, breached, out_of_order)
                VALUES (@t, @p, @s, @started, @completed, @elapsed, @allowance, @breached, @ooo)
                ON CONFLICT (transaction_id, step_code) DO UPDATE SET started_at = EXCLUDED.started_at, completed_at = EXCLUDED.completed_at,
                    elapsed_seconds = EXCLUDED.elapsed_seconds, allowance_seconds = EXCLUDED.allowance_seconds, breached = EXCLUDED.breached, out_of_order = EXCLUDED.out_of_order
                """, ct, ("t", transactionId), ("p", projectId), ("s", stat.Code), ("started", stat.StartedAt), ("completed", stat.CompletedAt),
                ("elapsed", stat.ElapsedSeconds), ("allowance", stat.AllowanceSeconds), ("breached", stat.Breached), ("ooo", stat.OutOfOrder));
    }
}
