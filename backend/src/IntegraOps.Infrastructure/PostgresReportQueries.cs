using System.Globalization;
using IntegraOps.Application;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using static IntegraOps.Infrastructure.PilotDb;

namespace IntegraOps.Infrastructure;

public sealed record DigestSettingsInput(string? Period, int? Hour, string? TimeZone);
public sealed record DigestRequest(string? Period);

/// <summary>V2 analytics: durations, compliance, alarm response, recurring problems, data quality, trends and digests.</summary>
public sealed class PostgresReportQueries(NpgsqlDataSource source, IDataProtectionProvider protection, IConfigurationReader panel)
{
    private readonly IDataProtector protector = protection.CreateProtector("notification-channels.v1");

    private static (DateTimeOffset From, DateTimeOffset To, string? Flow, string Test) Window(string? from, string? to, string? flow, string? test, int defaultDays)
    {
        var q = InputValidation.TransactionList(flow, null, null, 1, 25, null, from, to, test);
        var end = q.To ?? DateTimeOffset.UtcNow;
        var start = q.From ?? end.AddDays(-defaultDays);
        if (end - start > TimeSpan.FromDays(400)) throw new PilotHttpException(400, "Rapor aralığı en fazla 400 gün olabilir.");
        return (start, end, q.Flow, q.Test);
    }

    private const string TestFilter = "(@test='include' OR (@test='only' AND t.is_test) OR (@test='exclude' AND NOT t.is_test))";

    public async Task<object> Overview(Guid project, string? from, string? to, string? flow, string? test, CancellationToken ct)
    {
        var (start, end, flowCode, testMode) = Window(from, to, flow, test, 30);
        (string, object?)[] args = [("p", project), ("from", start), ("to", end), ("flow", flowCode), ("test", testMode)];
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var totals = (await Rows(c, tx, $"""
            SELECT count(*) transactions, count(*) FILTER (WHERE t.status='Completed') completed, count(*) FILTER (WHERE t.status='Failed') failed,
                count(*) FILTER (WHERE t.status='InProgress') in_progress
            FROM transactions t JOIN flows f ON f.id=t.flow_id
            WHERE t.project_id=@p AND t.created_at>=@from AND t.created_at<@to AND (CAST(@flow AS text) IS NULL OR f.code=@flow) AND {TestFilter}
            """, ct, args))[0];
        var alarms = (await Rows(c, tx, $"""
            SELECT count(*) FILTER (WHERE a.kind='overdue') overdue, count(*) FILTER (WHERE a.kind='failed') failed,
                count(*) FILTER (WHERE a.resolved_at IS NULL) still_open,
                round(avg(EXTRACT(EPOCH FROM a.acknowledged_at-a.opened_at)) FILTER (WHERE a.acknowledged_at IS NOT NULL)) mtta_seconds,
                round(percentile_cont(0.5) WITHIN GROUP (ORDER BY EXTRACT(EPOCH FROM a.acknowledged_at-a.opened_at)) FILTER (WHERE a.acknowledged_at IS NOT NULL)) mtta_p50_seconds,
                round(avg(EXTRACT(EPOCH FROM a.resolved_at-a.opened_at)) FILTER (WHERE a.resolved_at IS NOT NULL)) mttr_seconds,
                round(percentile_cont(0.5) WITHIN GROUP (ORDER BY EXTRACT(EPOCH FROM a.resolved_at-a.opened_at)) FILTER (WHERE a.resolved_at IS NOT NULL)) mttr_p50_seconds,
                count(*) FILTER (WHERE a.acknowledged_at IS NOT NULL) acknowledged, count(*) FILTER (WHERE a.resolved_at IS NOT NULL) resolved
            FROM alarms a JOIN transactions t ON t.id=a.transaction_id JOIN flows f ON f.id=t.flow_id
            WHERE a.project_id=@p AND a.opened_at>=@from AND a.opened_at<@to AND (CAST(@flow AS text) IS NULL OR f.code=@flow) AND {TestFilter}
            """, ct, args))[0];
        var steps = await Rows(c, tx, $"""
            SELECT f.code flow, s.step_code, count(*) measured, count(*) FILTER (WHERE s.breached) breached,
                round(100.0*count(*) FILTER (WHERE NOT s.breached)/count(*),1) compliance,
                round(percentile_cont(0.5) WITHIN GROUP (ORDER BY s.elapsed_seconds)) p50, round(percentile_cont(0.9) WITHIN GROUP (ORDER BY s.elapsed_seconds)) p90,
                round(percentile_cont(0.95) WITHIN GROUP (ORDER BY s.elapsed_seconds)) p95, max(s.elapsed_seconds) max, max(s.allowance_seconds) allowance
            FROM transaction_step_stats s JOIN transactions t ON t.id=s.transaction_id JOIN flows f ON f.id=t.flow_id
            WHERE s.project_id=@p AND s.completed_at>=@from AND s.completed_at<@to AND (CAST(@flow AS text) IS NULL OR f.code=@flow) AND {TestFilter}
            GROUP BY f.code, s.step_code ORDER BY breached DESC, measured DESC LIMIT 100
            """, ct, args);
        var compliance = (await Rows(c, tx, $"""
            SELECT count(*) measured, count(*) FILTER (WHERE s.breached) breached, count(*) FILTER (WHERE s.out_of_order) out_of_order
            FROM transaction_step_stats s JOIN transactions t ON t.id=s.transaction_id JOIN flows f ON f.id=t.flow_id
            WHERE s.project_id=@p AND s.completed_at>=@from AND s.completed_at<@to AND (CAST(@flow AS text) IS NULL OR f.code=@flow) AND {TestFilter}
            """, ct, args))[0];
        var topFailed = await Rows(c, tx, $"""
            SELECT f.code flow, a.step_code, left(coalesce(a.error,''),80) error, count(*) count, count(DISTINCT a.transaction_id) transactions
            FROM alarms a JOIN transactions t ON t.id=a.transaction_id JOIN flows f ON f.id=t.flow_id
            WHERE a.project_id=@p AND a.kind='failed' AND a.opened_at>=@from AND a.opened_at<@to AND (CAST(@flow AS text) IS NULL OR f.code=@flow) AND {TestFilter}
            GROUP BY f.code, a.step_code, left(coalesce(a.error,''),80) ORDER BY count DESC LIMIT 10
            """, ct, args);
        var topOverdue = await Rows(c, tx, $"""
            SELECT f.code flow, a.step_code, count(*) count,
                round(avg(EXTRACT(EPOCH FROM coalesce(a.resolved_at, clock_timestamp())-a.deadline_at)) FILTER (WHERE a.deadline_at IS NOT NULL)) avg_late_seconds
            FROM alarms a JOIN transactions t ON t.id=a.transaction_id JOIN flows f ON f.id=t.flow_id
            WHERE a.project_id=@p AND a.kind='overdue' AND a.opened_at>=@from AND a.opened_at<@to AND (CAST(@flow AS text) IS NULL OR f.code=@flow) AND {TestFilter}
            GROUP BY f.code, a.step_code ORDER BY count DESC LIMIT 10
            """, ct, args);
        var recurring = await Rows(c, tx, $"""
            SELECT f.code flow, t.business_id, count(*) alarms, count(*) FILTER (WHERE a.kind='failed') failed, count(*) FILTER (WHERE a.kind='overdue') overdue, max(a.opened_at) last_opened_at
            FROM alarms a JOIN transactions t ON t.id=a.transaction_id JOIN flows f ON f.id=t.flow_id
            WHERE a.project_id=@p AND a.opened_at>=@from AND a.opened_at<@to AND (CAST(@flow AS text) IS NULL OR f.code=@flow) AND {TestFilter}
            GROUP BY f.code, t.business_id HAVING count(*)>=2 ORDER BY alarms DESC, last_opened_at DESC LIMIT 20
            """, ct, args);
        var byGroup = await Rows(c, tx, $"""
            SELECT coalesce(t.group_code,'') "group", count(DISTINCT t.id) transactions, count(s.step_code) measured, count(*) FILTER (WHERE s.breached) breached,
                round(100.0*count(*) FILTER (WHERE s.step_code IS NOT NULL AND NOT s.breached)/NULLIF(count(s.step_code),0),1) compliance
            FROM transactions t JOIN flows f ON f.id=t.flow_id LEFT JOIN transaction_step_stats s ON s.transaction_id=t.id AND s.completed_at>=@from AND s.completed_at<@to
            WHERE t.project_id=@p AND t.created_at>=@from AND t.created_at<@to AND (CAST(@flow AS text) IS NULL OR f.code=@flow) AND {TestFilter}
            GROUP BY coalesce(t.group_code,'') ORDER BY transactions DESC LIMIT 20
            """, ct, args);
        var quality = (await Rows(c, tx, $"""
            SELECT count(*) events, count(*) FILTER (WHERE e.received_at-e.occurred_at > interval '5 minutes') late_events,
                round(percentile_cont(0.95) WITHIN GROUP (ORDER BY EXTRACT(EPOCH FROM e.received_at-e.occurred_at))) lag_p95_seconds,
                count(*) FILTER (WHERE e.occurred_at > e.received_at + interval '1 minute') future_events
            FROM transaction_events e JOIN transactions t ON t.id=e.transaction_id JOIN flows f ON f.id=t.flow_id
            WHERE e.project_id=@p AND e.received_at>=@from AND e.received_at<@to AND (CAST(@flow AS text) IS NULL OR f.code=@flow) AND {TestFilter}
            """, ct, args))[0];
        var discrepancies = (await Rows(c, tx, """
            SELECT count(*) FILTER (WHERE resolved_at IS NULL) open, count(*) FILTER (WHERE first_seen_at>=@from AND first_seen_at<@to) found_in_period
            FROM reconciliation_discrepancies WHERE project_id=@p AND (CAST(@flow AS text) IS NULL OR flow=@flow)
            """, ct, ("p", project), ("from", start), ("to", end), ("flow", flowCode)))[0];
        await tx.CommitAsync(ct);
        return new
        {
            from = start, to = end, flow = flowCode, test = testMode,
            totals, alarms, compliance, steps, topFailed, topOverdue, recurring, byGroup,
            quality = new Dictionary<string, object?>(quality) { ["out_of_order_steps"] = compliance["out_of_order"], ["open_discrepancies"] = discrepancies["open"], ["discrepancies_found"] = discrepancies["found_in_period"] },
        };
    }

    public async Task<object> Trend(Guid project, string? flow, string? test, string? period, int? buckets, CancellationToken ct)
    {
        period = period is "week" ? "week" : "day";
        var n = Math.Clamp(buckets ?? (period == "week" ? 12 : 30), 2, 90);
        var q = InputValidation.TransactionList(flow, null, null, test: test);
        await using var c = await source.OpenConnectionAsync(ct);
        var rows = await Rows(c, null, $"""
            WITH buckets AS (
                SELECT date_trunc(@period, clock_timestamp()) - (n || ' {(period == "week" ? "weeks" : "days")}')::interval AS start
                FROM generate_series(@n - 1, 0, -1) n)
            SELECT b.start,
                (SELECT count(*) FROM transactions t JOIN flows f ON f.id=t.flow_id WHERE t.project_id=@p AND t.created_at>=b.start AND t.created_at<b.start+('1 ' || @period)::interval
                    AND (CAST(@flow AS text) IS NULL OR f.code=@flow) AND {TestFilter}) transactions,
                (SELECT count(*) FROM transaction_step_stats s JOIN transactions t ON t.id=s.transaction_id JOIN flows f ON f.id=t.flow_id WHERE s.project_id=@p AND s.completed_at>=b.start AND s.completed_at<b.start+('1 ' || @period)::interval
                    AND (CAST(@flow AS text) IS NULL OR f.code=@flow) AND {TestFilter}) measured,
                (SELECT count(*) FROM transaction_step_stats s JOIN transactions t ON t.id=s.transaction_id JOIN flows f ON f.id=t.flow_id WHERE s.project_id=@p AND s.breached AND s.completed_at>=b.start AND s.completed_at<b.start+('1 ' || @period)::interval
                    AND (CAST(@flow AS text) IS NULL OR f.code=@flow) AND {TestFilter}) breached,
                (SELECT count(*) FROM alarms a JOIN transactions t ON t.id=a.transaction_id JOIN flows f ON f.id=t.flow_id WHERE a.project_id=@p AND a.opened_at>=b.start AND a.opened_at<b.start+('1 ' || @period)::interval
                    AND (CAST(@flow AS text) IS NULL OR f.code=@flow) AND {TestFilter}) alarms,
                (SELECT round(percentile_cont(0.5) WITHIN GROUP (ORDER BY s.elapsed_seconds)) FROM transaction_step_stats s JOIN transactions t ON t.id=s.transaction_id JOIN flows f ON f.id=t.flow_id
                    WHERE s.project_id=@p AND s.completed_at>=b.start AND s.completed_at<b.start+('1 ' || @period)::interval AND (CAST(@flow AS text) IS NULL OR f.code=@flow) AND {TestFilter}) p50_seconds
            FROM buckets b ORDER BY b.start
            """, ct, ("p", project), ("period", period), ("n", n), ("flow", q.Flow), ("test", q.Test));
        return new { period, buckets = rows };
    }

    // ---------- digests ----------

    public async Task<object> DigestSettings(Guid project, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var rows = await Rows(c, null, "SELECT digest_period,digest_hour,digest_timezone,updated_at FROM project_settings WHERE project_id=@p", ct, ("p", project));
        var sent = await Rows(c, null, "SELECT period,period_from,period_to,recipients,sent_at,triggered_by FROM report_digests WHERE project_id=@p ORDER BY sent_at DESC LIMIT 10", ct, ("p", project));
        var settings = rows.Count > 0 ? rows[0] : new Dictionary<string, object?> { ["digest_period"] = "none", ["digest_hour"] = (short)7, ["digest_timezone"] = "Europe/Istanbul", ["updated_at"] = null };
        return new { settings, sent };
    }

    public async Task SetDigestSettings(ProjectIdentity actor, DigestSettingsInput input, CancellationToken ct)
    {
        if (input.Period is not ("none" or "daily" or "weekly")) throw new PilotHttpException(400, "Özet dönemi none, daily veya weekly olmalı.");
        if (input.Hour is null or < 0 or > 23) throw new PilotHttpException(400, "Saat 0–23 arasında olmalı.");
        var tz = (input.TimeZone ?? "Europe/Istanbul").Trim();
        try { TimeZoneInfo.FindSystemTimeZoneById(tz); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { throw new PilotHttpException(400, "Saat dilimi tanınmadı."); }
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await Run(c, tx, """
            INSERT INTO project_settings(project_id,digest_period,digest_hour,digest_timezone,updated_by) VALUES(@p,@period,@h,@tz,@u)
            ON CONFLICT(project_id) DO UPDATE SET digest_period=EXCLUDED.digest_period,digest_hour=EXCLUDED.digest_hour,digest_timezone=EXCLUDED.digest_timezone,
                updated_at=clock_timestamp(),updated_by=EXCLUDED.updated_by
            """, ct, ("p", actor.ProjectId), ("period", input.Period), ("h", (short)input.Hour.Value), ("tz", tz), ("u", actor.UserId));
        await Audit(c, tx, actor.ProjectId, actor.UserId, "digest.settings", $"{input.Period}@{input.Hour} {tz}", ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>Composes and queues the daily or weekly summary for every enabled e-mail channel.</summary>
    public async Task<object> SendDigest(Guid project, string? period, string triggeredBy, Guid? actor, CancellationToken ct)
    {
        if (period is not ("daily" or "weekly")) throw new PilotHttpException(400, "Dönem daily veya weekly olmalı.");
        var end = DateTimeOffset.UtcNow;
        var start = end.AddDays(period == "daily" ? -1 : -7);
        var overview = await Overview(project, start.ToString("O"), end.ToString("O"), null, null, ct);
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var channels = await Rows(c, tx, "SELECT target_protected FROM notification_channels WHERE project_id=@p AND enabled AND kind='email'", ct, ("p", project));
        var name = (string)(await Value(c, tx, "SELECT name FROM projects WHERE id=@p", ct, ("p", project)))!;
        var body = DigestText(name, period, start, end, overview);
        foreach (var ch in channels)
            await PostgresOperationsQueries.QueueEmail(c, tx, protector.Unprotect((string)ch["target_protected"]!),
                $"IntegraOps · {(period == "daily" ? "Günlük" : "Haftalık")} özet · {name}", body, ct);
        await Run(c, tx, "INSERT INTO report_digests(id,project_id,period,period_from,period_to,recipients,triggered_by) VALUES(@id,@p,@period,@from,@to,@r,@by)", ct,
            ("id", Guid.CreateVersion7()), ("p", project), ("period", period), ("from", start), ("to", end), ("r", channels.Count), ("by", triggeredBy));
        await Audit(c, tx, project, actor, "digest.send", $"{period} recipients={channels.Count}", ct);
        await tx.CommitAsync(ct);
        return new { period, from = start, to = end, recipients = channels.Count, preview = body };
    }

    private string DigestText(string project, string period, DateTimeOffset from, DateTimeOffset to, object overviewObject)
    {
        dynamic o = overviewObject;
        var totals = (Dictionary<string, object?>)o.totals; var alarms = (Dictionary<string, object?>)o.alarms;
        var compliance = (Dictionary<string, object?>)o.compliance; var quality = (Dictionary<string, object?>)o.quality;
        var steps = (List<Dictionary<string, object?>>)o.steps; var failed = (List<Dictionary<string, object?>>)o.topFailed;
        var recurring = (List<Dictionary<string, object?>>)o.recurring;
        long measured = (long)compliance["measured"]!, breached = (long)compliance["breached"]!;
        string Pct(long part, long whole) => whole == 0 ? "—" : (100.0 * part / whole).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        string Dur(object? seconds) => seconds is null ? "—" : Format(Convert.ToDouble(seconds, CultureInfo.InvariantCulture));
        var lines = new List<string>
        {
            $"{(period == "daily" ? "Günlük" : "Haftalık")} IntegraOps özeti · {project}",
            $"Dönem: {from:yyyy-MM-dd HH:mm}Z – {to:yyyy-MM-dd HH:mm}Z", "",
            $"İşlemler: {totals["transactions"]} yeni · {totals["completed"]} tamamlandı · {totals["failed"]} hata · {totals["in_progress"]} devam ediyor",
            $"Süre uyumu: {Pct(measured - breached, measured)} ({measured} ölçülen adım, {breached} aşım)",
            $"Alarmlar: {alarms["overdue"]} süre aşımı · {alarms["failed"]} hata · {alarms["still_open"]} hâlâ açık",
            $"Alarm yanıtı: görülme ort. {Dur(alarms["mtta_seconds"])} · çözüm ort. {Dur(alarms["mttr_seconds"])}",
            $"Veri kalitesi: {quality["late_events"]} geç bildirim · {quality["out_of_order_steps"]} tutarsız zaman · {quality["open_discrepancies"]} açık mutabakat uyuşmazlığı", "",
        };
        if (steps.Count > 0)
        {
            lines.Add("En çok süre aşan adımlar:");
            lines.AddRange(steps.Take(5).Select(s => $"- {s["flow"]}/{s["step_code"]}: {s["breached"]}/{s["measured"]} aşım, p95 {Dur(s["p95"])}, tanınan {Dur(s["allowance"])}"));
            lines.Add("");
        }
        if (failed.Count > 0)
        {
            lines.Add("En sık hata veren adımlar:");
            lines.AddRange(failed.Take(5).Select(f => $"- {f["flow"]}/{f["step_code"]}: {f["count"]} alarm{(string.IsNullOrEmpty((string?)f["error"]) ? "" : " · " + f["error"])}"));
            lines.Add("");
        }
        if (recurring.Count > 0)
        {
            lines.Add("Tekrarlayan sorunlar (2+ alarm):");
            lines.AddRange(recurring.Take(5).Select(r => $"- {r["flow"]}/{r["business_id"]}: {r["alarms"]} alarm"));
            lines.Add("");
        }
        lines.Add($"Ayrıntı: {panel.PanelUrl}/reports");
        return string.Join("\n", lines);
    }

    public static string Format(double seconds)
    {
        if (seconds < 60) return $"{seconds:0} sn";
        if (seconds < 3600) return $"{seconds / 60:0} dk";
        if (seconds < 86400) return $"{seconds / 3600:0.#} sa";
        return $"{seconds / 86400:0.#} gün";
    }

    /// <summary>Projects whose scheduled digest is due now, in their own time zone; used by the worker.</summary>
    public async Task<List<(Guid Project, string Period)>> DueDigests(DateTimeOffset now, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var rows = await Rows(c, null, """
            SELECT s.project_id, s.digest_period, s.digest_hour, s.digest_timezone,
                (SELECT max(sent_at) FROM report_digests d WHERE d.project_id=s.project_id AND d.period=s.digest_period AND d.triggered_by='schedule') last_sent
            FROM project_settings s WHERE s.digest_period<>'none'
            """, ct);
        var due = new List<(Guid, string)>();
        foreach (var r in rows)
        {
            TimeZoneInfo zone;
            try { zone = TimeZoneInfo.FindSystemTimeZoneById((string)r["digest_timezone"]!); } catch { continue; }
            var local = TimeZoneInfo.ConvertTime(now, zone);
            var period = (string)r["digest_period"]!;
            if (local.Hour != (short)r["digest_hour"]!) continue;
            if (period == "weekly" && local.DayOfWeek != DayOfWeek.Monday) continue;
            if (r["last_sent"] is DateTime last && TimeZoneInfo.ConvertTime(new DateTimeOffset(DateTime.SpecifyKind(last, DateTimeKind.Utc)), zone).Date == local.Date) continue;
            due.Add(((Guid)r["project_id"]!, period));
        }
        return due;
    }
}
