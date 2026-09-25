using Npgsql;
using static IntegraOps.Infrastructure.PilotDb;

namespace IntegraOps.Infrastructure;

/// <summary>
/// Data-driven review of a flow's step allowances. Realised step durations (transaction_step_stats) and the overdue
/// alarms they produced show which limits raise false alarms and which never fire. The recommendation, the suggested
/// value and its estimated effect are computed here; the AI assistant only puts them into words.
/// </summary>
public sealed class PostgresThresholdAdvisor(NpgsqlDataSource source)
{
    public const int MinSamples = 20;
    public static readonly string[] Recommendations = ["raise", "lower", "calendar", "keep", "wait"];
    // "Nice" allowances operators actually type: 5 min … 7 days.
    private static readonly int[] Ladder = [300, 600, 900, 1200, 1800, 2700, 3600, 5400, 7200, 10800, 14400, 21600, 28800, 43200, 86400, 172800, 259200, 604800];

    public sealed record StepAdvice(string Code, string Name, int Position, int? AllowanceSeconds, long N, long? P50Seconds, long? P90Seconds, long? P95Seconds,
        long? MaxSeconds, long Breached, double BreachRate, long? MedianLateSeconds, double OffHoursShare, long OverdueAlarms, long OverdueResolvedByCompletion,
        string Recommendation, int? SuggestedSeconds, long Impact, string Reason);

    public sealed record Result(string Flow, string Name, int? Version, string? Calendar, int Days, string Test, DateTime ComputedAt, List<StepAdvice> Steps);

    public async Task<Result> Advise(Guid project, string code, int days, string test, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        return await Compute(c, project, code, days, test, ct);
    }

    internal static async Task<Result> Compute(NpgsqlConnection c, Guid project, string code, int days, string test, CancellationToken ct)
    {
        days = Math.Clamp(days, 7, 90);
        test = test is "include" or "only" ? test : "exclude";
        var flows = await Rows(c, null, """
            SELECT f.id,f.name,v.id version_id,v.version,cal.name calendar,cal.timezone,cal.work_days,cal.start_time,cal.end_time
            FROM flows f
            LEFT JOIN LATERAL (SELECT id,version,calendar_id FROM flow_versions WHERE flow_id=f.id AND state='published' AND effective_from<=clock_timestamp()
                               ORDER BY effective_from DESC LIMIT 1) v ON true
            LEFT JOIN business_calendars cal ON cal.id=v.calendar_id
            WHERE f.project_id=@p AND f.code=@c
            """, ct, ("p", project), ("c", code));
        if (flows.Count == 0) throw new PilotHttpException(404, "Akış bu projede bulunamadı.");
        var flow = flows[0]; var flowId = (Guid)flow["id"]!;
        var tz = flow["timezone"] as string ?? "Europe/Istanbul";
        var workDays = flow["work_days"] as short[] ?? [1, 2, 3, 4, 5];
        var start = flow["start_time"] as TimeSpan? ?? new TimeSpan(9, 0, 0);
        var end = flow["end_time"] as TimeSpan? ?? new TimeSpan(18, 0, 0);
        var result = new Result(code, (string)flow["name"]!, flow["version"] as int?, flow["calendar"] as string, days, test, DateTime.UtcNow, []);
        if (flow["version_id"] is not Guid versionId) return result;

        var steps = await Rows(c, null, "SELECT code,name,position,max_delay_seconds FROM flow_version_steps WHERE version_id=@v AND position>0 ORDER BY position", ct, ("v", versionId));
        foreach (var step in steps)
        {
            var stepCode = (string)step["code"]!; var allowance = step["max_delay_seconds"] as int?;
            var st = (await Rows(c, null, """
                SELECT count(*) n,
                    CAST(percentile_cont(0.5) WITHIN GROUP (ORDER BY s.elapsed_seconds) AS bigint) p50,
                    CAST(percentile_cont(0.9) WITHIN GROUP (ORDER BY s.elapsed_seconds) AS bigint) p90,
                    CAST(percentile_cont(0.95) WITHIN GROUP (ORDER BY s.elapsed_seconds) AS bigint) p95,
                    max(s.elapsed_seconds) max_seconds,count(*) FILTER (WHERE s.breached) breached,
                    CAST(percentile_cont(0.5) WITHIN GROUP (ORDER BY s.elapsed_seconds-s.allowance_seconds) FILTER (WHERE s.breached) AS bigint) median_late,
                    count(*) FILTER (WHERE s.breached AND (
                        NOT (CAST(extract(isodow FROM s.completed_at AT TIME ZONE @tz) AS smallint) = ANY(@days))
                        OR CAST(s.completed_at AT TIME ZONE @tz AS time) < @start OR CAST(s.completed_at AT TIME ZONE @tz AS time) >= @end)) off_hours
                FROM transaction_step_stats s JOIN transactions t ON t.id=s.transaction_id
                WHERE s.project_id=@p AND t.flow_id=@f AND s.step_code=@s AND s.completed_at > clock_timestamp()-make_interval(days => @window)
                  AND (@test='include' OR (@test='only' AND t.is_test) OR (@test='exclude' AND NOT t.is_test))
                """, ct, ("p", project), ("f", flowId), ("s", stepCode), ("window", days), ("test", test), ("tz", tz), ("days", workDays), ("start", start), ("end", end)))[0];
            var alarms = (await Rows(c, null, """
                SELECT count(*) opened,count(*) FILTER (WHERE a.resolution='step_completed') completed
                FROM alarms a JOIN transactions t ON t.id=a.transaction_id
                WHERE a.project_id=@p AND t.flow_id=@f AND a.step_code=@s AND a.kind='overdue' AND a.opened_at > clock_timestamp()-make_interval(days => @window)
                  AND (@test='include' OR (@test='only' AND t.is_test) OR (@test='exclude' AND NOT t.is_test))
                """, ct, ("p", project), ("f", flowId), ("s", stepCode), ("window", days), ("test", test)))[0];

            var n = (long)st["n"]!; var breached = (long)st["breached"]!;
            var breachRate = n == 0 ? 0 : breached / (double)n;
            var offHours = breached == 0 ? 0 : (long)st["off_hours"]! / (double)breached;
            var p95 = st["p95"] as long?;
            string rec; int? suggested = null; long impact = 0; string reason;
            if (allowance is null) { rec = "keep"; reason = "Bu adımın süresi ölçülmüyor."; }
            else if (n < MinSamples) { rec = "wait"; reason = $"Son {days} günde yalnızca {n} tamamlanma var; öneri için en az {MinSamples} gerekir."; }
            else if (breachRate >= 0.2 && p95 is { } high && high <= 3L * allowance.Value)
            {
                if (offHours > 0.6 && flow["calendar"] is null)
                {
                    rec = "calendar";
                    reason = $"Sınırı aşan tamamlanmaların %{Math.Round(offHours * 100)} kadarı mesai dışında; akışa çalışma takvimi bağlamak bu alarmların çoğunu kaldırır.";
                    impact = (long)st["off_hours"]!;
                }
                else
                {
                    var candidate = Nice((long)Math.Ceiling(high * 1.2));
                    if (candidate > allowance.Value)
                    {
                        rec = "raise"; suggested = candidate;
                        impact = await Avoided(c, project, flowId, stepCode, days, test, candidate, ct);
                        reason = $"Tamamlanmaların %{Math.Round(breachRate * 100)} kadarı sınırı aştıktan sonra bitiyor (p95 {Human(high)}, sınır {Human(allowance.Value)}); sınır {Human(candidate)} olsaydı son {days} günde {impact} alarm açılmazdı.";
                    }
                    else { rec = "keep"; reason = "Sınır zaten gerçekleşen sürelerle uyumlu."; }
                }
            }
            else if (breachRate < 0.05 && p95 is { } low && low * 3 < allowance.Value)
            {
                var candidate = Nice((long)Math.Ceiling(low * 1.2));
                if (candidate < allowance.Value)
                {
                    rec = "lower"; suggested = candidate;
                    impact = await Slower(c, project, flowId, stepCode, days, test, candidate, ct);
                    reason = $"Adım neredeyse hep {Human(low)} içinde bitiyor ama sınır {Human(allowance.Value)}; gerçek bir gecikme saatlerce fark edilmez. {Human(candidate)} sınırı son {days} günde {impact} işlemi daha erken yakalardı.";
                }
                else { rec = "keep"; reason = "Sınır gerçekleşen sürelerle uyumlu."; }
            }
            else { rec = "keep"; reason = breachRate == 0 ? "Sınır hiç aşılmadı; süre dağılımı sınırla uyumlu." : $"Aşım oranı %{Math.Round(breachRate * 100)}; sınır makul aralıkta."; }

            result.Steps.Add(new StepAdvice(stepCode, (string)step["name"]!, (short)step["position"]!, allowance, n, st["p50"] as long?, st["p90"] as long?, p95,
                st["max_seconds"] as long?, breached, Math.Round(breachRate, 3), st["median_late"] as long?, Math.Round(offHours, 2),
                (long)alarms["opened"]!, (long)alarms["completed"]!, rec, suggested, impact, reason));
        }
        return result;
    }

    private static async Task<long> Avoided(NpgsqlConnection c, Guid project, Guid flow, string step, int days, string test, int suggested, CancellationToken ct) =>
        (long)(await Value(c, null, """
            SELECT count(*) FROM transaction_step_stats s JOIN transactions t ON t.id=s.transaction_id
            WHERE s.project_id=@p AND t.flow_id=@f AND s.step_code=@s AND s.breached AND s.elapsed_seconds<=@limit
              AND s.completed_at > clock_timestamp()-make_interval(days => @window)
              AND (@test='include' OR (@test='only' AND t.is_test) OR (@test='exclude' AND NOT t.is_test))
            """, ct, ("p", project), ("f", flow), ("s", step), ("limit", (long)suggested), ("window", days), ("test", test)))!;

    private static async Task<long> Slower(NpgsqlConnection c, Guid project, Guid flow, string step, int days, string test, int suggested, CancellationToken ct) =>
        (long)(await Value(c, null, """
            SELECT count(*) FROM transaction_step_stats s JOIN transactions t ON t.id=s.transaction_id
            WHERE s.project_id=@p AND t.flow_id=@f AND s.step_code=@s AND NOT s.breached AND s.elapsed_seconds>@limit
              AND s.completed_at > clock_timestamp()-make_interval(days => @window)
              AND (@test='include' OR (@test='only' AND t.is_test) OR (@test='exclude' AND NOT t.is_test))
            """, ct, ("p", project), ("f", flow), ("s", step), ("limit", (long)suggested), ("window", days), ("test", test)))!;

    /// <summary>Rounds a duration up to a value an operator would set by hand.</summary>
    public static int Nice(long seconds)
    {
        foreach (var rung in Ladder) if (rung >= seconds) return rung;
        return (int)Math.Min(2_592_000, (long)Math.Ceiling(seconds / 86400.0) * 86400);
    }

    public static string Human(long seconds) => seconds % 86400 == 0 && seconds >= 86400 ? $"{seconds / 86400} gün"
        : seconds % 3600 == 0 && seconds >= 3600 ? $"{seconds / 3600} sa" : seconds >= 3600 ? $"{seconds / 3600.0:0.#} sa" : $"{Math.Max(1, seconds / 60)} dk";

    /// <summary>Writes suggested allowances into the flow's draft version, creating the draft first when there is none.</summary>
    public async Task<object> Apply(ProjectIdentity actor, string code, Dictionary<string, int>? steps, PostgresFlowVersionStore versions, CancellationToken ct)
    {
        if (steps is null || steps.Count == 0) throw new PilotHttpException(400, "Uygulanacak adım seçin.");
        if (steps.Values.Any(v => v is < 1 or > 2_592_000)) throw new PilotHttpException(400, "Süre 1 saniye ile 30 gün arasında olmalı.");
        await using var c = await source.OpenConnectionAsync(ct);
        var flows = await Rows(c, null, "SELECT id FROM flows WHERE project_id=@p AND code=@c", ct, ("p", actor.ProjectId), ("c", code));
        if (flows.Count == 0) throw new PilotHttpException(404, "Akış bu projede bulunamadı.");
        var flowId = (Guid)flows[0]["id"]!;
        // Validate against the newest version before touching anything, so a bad request never leaves an empty draft behind.
        var known = (await Rows(c, null, """
            SELECT s.code FROM flow_version_steps s JOIN flow_versions v ON v.id=s.version_id
            WHERE v.flow_id=@f AND s.position>0 AND v.version=(SELECT max(version) FROM flow_versions WHERE flow_id=@f)
            """, ct, ("f", flowId))).Select(r => (string)r["code"]!).ToHashSet();
        var unknown = steps.Keys.Where(k => !known.Contains(k)).ToList();
        if (unknown.Count > 0) throw new PilotHttpException(400, $"Bu adımlar akışta yok: {string.Join(", ", unknown)}.");
        var draft = await Rows(c, null, "SELECT id,version FROM flow_versions WHERE flow_id=@f AND state='draft'", ct, ("f", flowId));
        var created = false;
        if (draft.Count == 0) { await versions.CreateDraft(actor, code, ct); created = true; draft = await Rows(c, null, "SELECT id,version FROM flow_versions WHERE flow_id=@f AND state='draft'", ct, ("f", flowId)); }
        var versionId = (Guid)draft[0]["id"]!; var number = (int)draft[0]["version"]!;
        await using var tx = await c.BeginTransactionAsync(ct);
        var changed = 0;
        foreach (var (step, seconds) in steps)
            changed += await Run(c, tx, "UPDATE flow_version_steps SET max_delay_seconds=@d WHERE version_id=@v AND code=@s AND position>0", ct, ("d", seconds), ("v", versionId), ("s", step));
        await Run(c, tx, "UPDATE flow_versions SET note=left(concat_ws(' ', note, 'Süre sınırı önerisi uygulandı.'), 500) WHERE id=@v", ct, ("v", versionId));
        await Audit(c, tx, actor.ProjectId, actor.UserId, "flow.threshold_apply", $"{code}@{number} {changed} adım", ct);
        await tx.CommitAsync(ct);
        return new { version = number, changed, created };
    }
}
