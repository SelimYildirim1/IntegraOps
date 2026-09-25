using System.Text;
using Npgsql;
using static IntegraOps.Infrastructure.PilotDb;

namespace IntegraOps.Infrastructure;

/// <summary>
/// Open alarms grouped into probable problems. Everything here is computed: the group key, the spike ratio against the
/// last week, the source-health flags and the priority score. The AI assistant only narrates this result; the panel
/// shows it without any model.
/// </summary>
public sealed class PostgresTriageQueries(NpgsqlDataSource source)
{
    public const int MaxGroups = 20;

    public sealed record Group(string Key, string Flow, string FlowName, string Step, string Kind, string Fingerprint, string? Error,
        long Count, DateTime FirstOpenedAt, DateTime LastOpenedAt, long Unacknowledged, long Unassigned, string[] Samples, Guid[] Ids,
        double Baseline6h, double Ratio, long StatsCount, long? P50Seconds, long? P95Seconds, long? AllowanceSeconds, double BreachRate,
        string? DominantCause, long DominantCauseCount, long MemoryTotal, string? LastFix,
        string[] Signals, int Priority, string Draft);

    public sealed record Source(string Kind, string Name, DateTime? LastSeenAt, string? LastError, bool Silent);

    public sealed record Result(DateTime ComputedAt, string Test, long OpenAlarms, List<Group> Groups, List<Source> Sources);

    public async Task<Result> Triage(Guid project, string test, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        return await Compute(c, project, test, ct);
    }

    internal static async Task<Result> Compute(NpgsqlConnection c, Guid project, string test, CancellationToken ct)
    {
        test = test is "include" or "only" ? test : "exclude";
        var rows = await Rows(c, null, """
            WITH open AS (
                SELECT a.id,a.kind,a.step_code,a.error,a.opened_at,a.acknowledged_at,a.assigned_to,f.code flow,f.name flow_name,t.flow_id,t.business_id,
                    regexp_replace(left(coalesce(a.error,''),80),'[0-9A-Fa-f]{6,}|[0-9]+','#','g') fingerprint
                FROM alarms a JOIN transactions t ON t.id=a.transaction_id JOIN flows f ON f.id=t.flow_id
                WHERE a.project_id=@p AND a.resolved_at IS NULL
                  AND (@test='include' OR (@test='only' AND t.is_test) OR (@test='exclude' AND NOT t.is_test))
            )
            SELECT flow,flow_name,flow_id,step_code,kind,fingerprint,count(*) count,min(opened_at) first_opened_at,max(opened_at) last_opened_at,
                count(*) FILTER (WHERE acknowledged_at IS NULL) unacknowledged,count(*) FILTER (WHERE assigned_to IS NULL) unassigned,
                (array_agg(business_id ORDER BY opened_at DESC))[1:5] samples,array_agg(id ORDER BY opened_at DESC) ids,
                (array_agg(error ORDER BY opened_at DESC) FILTER (WHERE error IS NOT NULL))[1] error,
                (SELECT count(*) FROM alarms b JOIN transactions tb ON tb.id=b.transaction_id
                 WHERE b.project_id=@p AND tb.flow_id=open.flow_id AND b.step_code=open.step_code AND b.kind=open.kind
                   AND b.opened_at BETWEEN clock_timestamp()-interval '8 days' AND clock_timestamp()-interval '1 day') week_before
            FROM open GROUP BY flow,flow_name,flow_id,step_code,kind,fingerprint ORDER BY count DESC,first_opened_at
            """, ct, ("p", project), ("test", test));
        var openTotal = rows.Sum(r => (long)r["count"]!);

        var stats = (await Rows(c, null, """
            SELECT t.flow_id,s.step_code,count(*) n,
                CAST(percentile_cont(0.5) WITHIN GROUP (ORDER BY s.elapsed_seconds) AS bigint) p50,
                CAST(percentile_cont(0.95) WITHIN GROUP (ORDER BY s.elapsed_seconds) AS bigint) p95,
                max(s.allowance_seconds) allowance,count(*) FILTER (WHERE s.breached) breached
            FROM transaction_step_stats s JOIN transactions t ON t.id=s.transaction_id
            WHERE s.project_id=@p AND s.completed_at > clock_timestamp()-interval '14 days'
              AND (@test='include' OR (@test='only' AND t.is_test) OR (@test='exclude' AND NOT t.is_test))
            GROUP BY t.flow_id,s.step_code
            """, ct, ("p", project), ("test", test))).ToDictionary(r => ((Guid)r["flow_id"]!, (string)r["step_code"]!));

        var sources = new List<Source>();
        foreach (var co in await Rows(c, null, """
            SELECT name,last_event_at,last_error,
                last_event_at IS NOT NULL AND last_event_at < clock_timestamp()-interval '15 minutes' AND last_event_at > clock_timestamp()-interval '24 hours' silent
            FROM connections WHERE project_id=@p ORDER BY last_event_at DESC NULLS LAST LIMIT 10
            """, ct, ("p", project)))
            sources.Add(new("connection", (string)co["name"]!, co["last_event_at"] as DateTime?, co["last_error"] as string, (bool)co["silent"]!));
        foreach (var ag in await Rows(c, null, """
            SELECT name,seen_at,last_error,seen_at < clock_timestamp()-interval '5 minutes' AND seen_at > clock_timestamp()-interval '24 hours' silent
            FROM agent_heartbeats WHERE project_id=@p ORDER BY seen_at DESC LIMIT 10
            """, ct, ("p", project)))
            sources.Add(new("agent", (string)ag["name"]!, ag["seen_at"] as DateTime?, ag["last_error"] as string, (bool)ag["silent"]!));
        var anySilent = sources.Any(s => s.Silent);

        var groups = new List<Group>();
        foreach (var r in rows.Take(MaxGroups))
        {
            var flowId = (Guid)r["flow_id"]!; var step = (string)r["step_code"]!; var kind = (string)r["kind"]!;
            var count = (long)r["count"]!;
            var baseline = (long)r["week_before"]! / 28.0; // six-hour buckets in the prior week
            var ratio = count / Math.Max(baseline, 0.5);
            stats.TryGetValue((flowId, step), out var st);
            var n = st is null ? 0 : (long)st["n"]!;
            var breachRate = n == 0 ? 0 : (long)st!["breached"]! / (double)n;
            var memory = await PostgresPilotQueries.MemorySummary(c, project, flowId, step, kind, test == "only", ct);
            var firstOpened = (DateTime)r["first_opened_at"]!;
            var ageHours = (DateTime.UtcNow - firstOpened).TotalHours;
            var unassigned = (long)r["unassigned"]!;

            var signals = new List<string>();
            if (ratio >= 3 && count >= 3) signals.Add("spike");
            if (anySilent && kind == "overdue") signals.Add("source_silent");
            var thresholdSuspect = kind == "overdue" && n >= 20 && breachRate >= 0.2;
            if (thresholdSuspect) signals.Add("threshold_suspect");
            if (unassigned > count / 2) signals.Add("unassigned");
            if (ageHours > 24) signals.Add("stale");

            var priority = 20;
            if (kind == "failed") priority += 30;
            if (signals.Contains("spike")) priority += 25;
            if (count >= 10) priority += 15;
            if (ageHours > 2) priority += 10;
            if (signals.Contains("unassigned")) priority += 10;
            if (thresholdSuspect) priority -= 20;
            priority = Math.Clamp(priority, 0, 100);

            var samples = ((string[])r["samples"]!);
            var flow = (string)r["flow"]!; var flowName = (string)r["flow_name"]!;
            var key = string.Join('|', flow, step, kind, (string)r["fingerprint"]!);
            groups.Add(new Group(key, flow, flowName, step, kind, (string)r["fingerprint"]!, r["error"] as string,
                count, firstOpened, (DateTime)r["last_opened_at"]!, (long)r["unacknowledged"]!, unassigned, samples, (Guid[])r["ids"]!,
                Math.Round(baseline, 2), Math.Round(ratio, 1), n, st is null ? null : st["p50"] as long?, st is null ? null : st["p95"] as long?,
                st is null ? null : st["allowance"] as long?, Math.Round(breachRate, 2),
                memory.Cause, memory.CauseCount, memory.Total, memory.LastFix, [.. signals], priority,
                Draft(flowName, step, kind, count, firstOpened, (DateTime)r["last_opened_at"]!, samples, r["error"] as string, sources.Where(s => s.Silent).ToList())));
        }
        return new Result(DateTime.UtcNow, test, openTotal, groups.OrderByDescending(g => g.Priority).ThenByDescending(g => g.Count).ToList(), sources);
    }

    /// <summary>Escalation message for the team that owns the source system: facts only, ready to paste.</summary>
    private static string Draft(string flowName, string step, string kind, long count, DateTime first, DateTime last, string[] samples, string? error, List<Source> silent)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        string At(DateTime t) => TimeZoneInfo.ConvertTimeFromUtc(t, tz).ToString("dd.MM.yyyy HH:mm");
        var b = new StringBuilder();
        b.AppendLine($"Konu: {flowName} akışı, \"{step}\" adımı — {count} işlemde {(kind == "failed" ? "hata" : "gecikme")}");
        b.AppendLine();
        b.AppendLine($"Merhaba,");
        b.AppendLine();
        b.AppendLine(kind == "failed"
            ? $"{At(first)} ile {At(last)} arasında {flowName} akışında \"{step}\" adımı {count} işlem için hata bildirdi."
            : $"{At(first)} ile {At(last)} arasında {flowName} akışında \"{step}\" adımının tamamlandı bildirimi {count} işlem için beklenen sürede ulaşmadı.");
        if (error is not null) b.AppendLine($"Bildirilen hata: {error}");
        b.AppendLine($"Örnek işlemler: {string.Join(", ", samples)}{(count > samples.Length ? $" (+{count - samples.Length})" : "")}");
        foreach (var s in silent) b.AppendLine($"Not: {s.Name} ({(s.Kind == "agent" ? "ajan" : "bağlantı")}) {(s.LastSeenAt is { } t ? At(t) : "?")} tarihinden beri olay göndermiyor{(s.LastError is null ? "" : $"; son hata: {s.LastError}")}.");
        b.AppendLine();
        b.AppendLine(kind == "failed"
            ? "Bu işlemlerin sizin tarafınızda hangi durumda olduğunu ve hatanın giderilip giderilmediğini paylaşabilir misiniz?"
            : "Bu işlemler sizin tarafınızda tamamlandı mı, tamamlandıysa olay gönderimi neden gecikti? Bilgi verebilir misiniz?");
        b.AppendLine();
        b.Append("Teşekkürler.");
        return b.ToString();
    }
}
