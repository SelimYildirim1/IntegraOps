using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IntegraOps.Application;
using Microsoft.Extensions.Logging;
using Npgsql;
using static IntegraOps.Infrastructure.PilotDb;

namespace IntegraOps.Infrastructure;

/// <summary>
/// Evidence-based analysis of an alarm or a transaction. The backend gathers the facts (alarm, timeline, deliveries,
/// notes, similar alarms), the model only explains them, and every answer is stored with the evidence it was given.
/// Calls are reserved in PostgreSQL before the provider is contacted, so the daily cap, spacing and concurrency
/// limits hold across API replicas and restarts; a failed call still counts because the provider may have processed it.
/// </summary>
public sealed class PostgresAiStore(NpgsqlDataSource source, ITrackingStore tracking, IAiModelClient model, AiLimits limits,
    ILogger<PostgresAiStore> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static readonly string[] Ratings = ["useful", "wrong", "incomplete"];

    /// <summary>What the model is asked about; the transaction's synthetic marker decides test-mode eligibility.</summary>
    private sealed record Subject(string Kind, Guid Id, Guid ProjectId, string Flow, string BusinessId, bool IsTest);

    public async Task<object> Status(Guid organization, Guid project, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var plan = await PostgresOrganizationStore.AiEnabledFor(c, organization, ct);
        DateTime? nextAllowed = null;
        if (model.Enabled && limits.MinInterval > TimeSpan.Zero && await LastRequestAt(c, null, project, ct) is DateTime last)
            nextAllowed = last + limits.MinInterval > DateTime.UtcNow ? last + limits.MinInterval : null;
        // enabled = usable right now; configured, planEnabled and reason explain why not.
        return new
        {
            enabled = model.Enabled && plan, configured = model.Enabled, planEnabled = plan, reason = model.Enabled ? null : model.Reason,
            provider = model.Provider, model = model.Model, testMode = limits.TestMode,
            dailyLimit = limits.DailyPerProject, usedToday = model.Enabled ? await UsedToday(c, null, project, ct) : 0,
            minSecondsBetweenRequests = (int)limits.MinInterval.TotalSeconds, cacheSeconds = (int)limits.CacheTtl.TotalSeconds, nextAllowedAt = nextAllowed,
        };
    }

    public Task<Dictionary<string, object?>> AnalyzeAlarm(ProjectIdentity actor, Guid alarmId, CancellationToken ct) =>
        Analyze(actor, (c, t) => AlarmSubject(c, actor.ProjectId, alarmId, t), ct);

    public Task<Dictionary<string, object?>> AnalyzeTransaction(ProjectIdentity actor, string flow, string transactionId, CancellationToken ct) =>
        Analyze(actor, (c, t) => TransactionSubject(c, actor.ProjectId, flow, transactionId, t), ct);

    public async Task<List<Dictionary<string, object?>>> AlarmAnalyses(ProjectIdentity actor, Guid alarmId, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        return await History(c, actor, await AlarmSubject(c, actor.ProjectId, alarmId, ct), ct);
    }

    public async Task<List<Dictionary<string, object?>>> TransactionAnalyses(ProjectIdentity actor, string flow, string transactionId, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        return await History(c, actor, await TransactionSubject(c, actor.ProjectId, flow, transactionId, ct), ct);
    }

    public async Task Feedback(ProjectIdentity actor, Guid analysisId, string? rating, string? comment, CancellationToken ct)
    {
        if (actor.UserId is null) throw new PilotHttpException(403, "Geri bildirim için oturum gerekli.");
        if (rating is null || !Ratings.Contains(rating)) throw new PilotHttpException(400, "Değerlendirme useful, wrong veya incomplete olmalı.");
        comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (comment is { Length: > 1000 }) throw new PilotHttpException(400, "Açıklama en fazla 1000 karakter olabilir.");
        await using var c = await source.OpenConnectionAsync(ct);
        var changed = await Run(c, null, """
            INSERT INTO ai_feedback(analysis_id,user_id,rating,comment)
            SELECT id,@u,@r,@m FROM ai_analyses WHERE id=@id AND project_id=@p AND status='completed'
            ON CONFLICT (analysis_id,user_id) DO UPDATE SET rating=excluded.rating,comment=excluded.comment,created_at=clock_timestamp()
            """, ct, ("u", actor.UserId), ("r", rating), ("m", comment), ("id", analysisId), ("p", actor.ProjectId));
        if (changed == 0) throw new PilotHttpException(404, "Analiz bulunamadı.");
    }

    private async Task<Dictionary<string, object?>> Analyze(ProjectIdentity actor, Func<NpgsqlConnection, CancellationToken, Task<Subject>> resolve, CancellationToken ct)
    {
        if (!model.Enabled) throw new PilotHttpException(503, model.Reason ?? "Yapay zekâ henüz yapılandırılmadı.");
        await using var c = await source.OpenConnectionAsync(ct);
        if (!await PostgresOrganizationStore.AiEnabledFor(c, actor.OrganizationId, ct))
            throw new PilotHttpException(403, "Yapay zekâ asistanı bu organizasyonun planında yok.");
        var subject = await resolve(c, ct);
        // Test profile: only server-recognized synthetic transactions (TEST- prefix) may leave the system.
        if (limits.TestMode && !subject.IsTest)
            throw new PilotHttpException(403, "Deneme profilinde yalnızca sentetik işlemler (TEST- ile başlayan) incelenebilir; gerçek veri sağlayıcıya gönderilmez.");
        var evidence = await Evidence(c, subject, ct);
        var ids = evidence.Select(e => e!["id"]!.GetValue<string>()).ToHashSet();
        var hash = ContextHash(actor, subject, evidence);
        var prompt = new AiPrompt(InsightPrompt.System, new JsonObject { ["subject"] = subject.Kind, ["evidence"] = evidence }.ToJsonString(), limits.MaxOutputTokens);

        // Reservation: one project at a time checks its limits and inserts a pending row; the provider is called afterwards, outside any transaction.
        var id = Guid.CreateVersion7();
        var grace = limits.Timeout + TimeSpan.FromSeconds(30);
        await using (var tx = await c.BeginTransactionAsync(ct))
        {
            await Value(c, tx, "SELECT pg_advisory_xact_lock(hashtext(@k))", ct, ("k", "ai:" + actor.ProjectId));
            if (limits.CacheTtl > TimeSpan.Zero && await Value(c, tx, """
                SELECT id FROM ai_analyses WHERE project_id=@p AND context_hash=@h AND status='completed'
                  AND created_at > clock_timestamp() - CAST(@ttl AS interval) ORDER BY created_at DESC LIMIT 1
                """, ct, ("p", actor.ProjectId), ("h", hash), ("ttl", limits.CacheTtl)) is Guid cached)
            {
                await tx.CommitAsync(ct);
                logger.LogInformation("AI incelemesi önbellekten verildi. Proje: {Project} Konu: {Kind}/{Subject}", actor.ProjectId, subject.Kind, subject.Id);
                return (await Listing(c, actor, subject, cached, hash, ct))[0];
            }
            var pending = await Rows(c, tx, """
                SELECT context_hash FROM ai_analyses WHERE project_id=@p AND status='pending' AND created_at > clock_timestamp() - CAST(@grace AS interval)
                """, ct, ("p", actor.ProjectId), ("grace", grace));
            if (pending.Any(r => (string?)r["context_hash"] == hash))
                throw new PilotHttpException(409, "Aynı bağlam için inceleme zaten sürüyor; birkaç saniye sonra sayfayı yenileyin.");
            if (pending.Count >= limits.MaxConcurrent)
                throw new PilotHttpException(429, $"Aynı anda en fazla {limits.MaxConcurrent} yapay zekâ incelemesi çalışabilir; sürenin bitmesini bekleyin.");
            if (await UsedToday(c, tx, actor.ProjectId, ct) >= limits.DailyPerProject)
                throw new PilotHttpException(429, $"Bu proje için son 24 saatteki yapay zekâ çağrı sınırına ({limits.DailyPerProject}) ulaşıldı.");
            if (limits.MinInterval > TimeSpan.Zero && await LastRequestAt(c, tx, actor.ProjectId, ct) is DateTime last)
            {
                var wait = last + limits.MinInterval - DateTime.UtcNow;
                if (wait > TimeSpan.Zero)
                    throw new PilotHttpException(429, $"Yapay zekâ çağrıları arasında en az {(int)limits.MinInterval.TotalSeconds} saniye bekleniyor; {Math.Ceiling(wait.TotalSeconds):0} saniye sonra tekrar deneyin.");
            }
            await Run(c, tx, """
                INSERT INTO ai_analyses(id,project_id,subject_kind,subject_id,requested_by,provider,model,prompt_version,status,evidence,context_hash,duration_ms)
                VALUES (@id,@p,@kind,@s,CAST(@u AS uuid),@provider,@model,@v,'pending',CAST(@e AS jsonb),@h,0)
                """, ct, ("id", id), ("p", actor.ProjectId), ("kind", subject.Kind), ("s", subject.Id), ("u", actor.UserId), ("provider", model.Provider),
                ("model", model.Model), ("v", InsightPrompt.Version), ("e", evidence.ToJsonString()), ("h", hash));
            await tx.CommitAsync(ct);
        }

        var watch = Stopwatch.StartNew();
        AiCompletion? completion = null; AiInsight? insight = null; string? error = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(limits.Timeout);
            completion = await model.CompleteAsync(prompt, timeout.Token);
            insight = InsightPrompt.Parse(completion.Text, ids);
        }
        catch (Exception ex) when (ex is not PilotHttpException)
        {
            // Store the failure class only; provider responses may echo the prompt or carry the key.
            error = ex switch
            {
                AiProviderException p => p.Message,
                FormatException or JsonException => "Model yanıtı beklenen biçimde değil.",
                OperationCanceledException when ct.IsCancellationRequested => "İstek iptal edildi.",
                OperationCanceledException => "Model zaman aşımına uğradı.",
                _ => "Model çağrısı başarısız: " + ex.GetType().Name,
            };
            logger.LogWarning("AI incelemesi başarısız. Proje: {Project} Konu: {Kind}/{Subject} Neden: {Reason}", actor.ProjectId, subject.Kind, subject.Id, error);
        }
        // The reservation is always closed, even when the caller went away.
        var done = ct.IsCancellationRequested ? CancellationToken.None : ct;
        await using (var tx = await c.BeginTransactionAsync(done))
        {
            await Run(c, tx, """
                UPDATE ai_analyses SET status=@status,result=CAST(@r AS jsonb),error=@err,input_tokens=@in,output_tokens=@out,duration_ms=@ms WHERE id=@id
                """, done, ("id", id), ("status", insight is null ? "failed" : "completed"),
                ("r", insight is null ? null : JsonSerializer.Serialize(insight, Json)), ("err", error),
                ("in", completion?.InputTokens), ("out", completion?.OutputTokens), ("ms", (int)watch.ElapsedMilliseconds));
            await Audit(c, tx, actor.ProjectId, actor.UserId, "ai." + subject.Kind + "_analysis", subject.Id.ToString(), done);
            await tx.CommitAsync(done);
        }
        if (insight is null) throw new PilotHttpException(502, "Yapay zekâ yanıtı alınamadı. " + error);
        return (await Listing(c, actor, subject, id, hash, ct))[0];
    }

    private async Task<List<Dictionary<string, object?>>> History(NpgsqlConnection c, ProjectIdentity actor, Subject subject, CancellationToken ct)
    {
        // The current context hash marks analyses whose evidence has since changed as stale.
        var current = ContextHash(actor, subject, await Evidence(c, subject, ct));
        return await Listing(c, actor, subject, null, current, ct);
    }

    private static async Task<int> UsedToday(NpgsqlConnection c, NpgsqlTransaction? tx, Guid project, CancellationToken ct) => (int)(long)(await Value(c, tx,
        "SELECT count(*) FROM ai_analyses WHERE project_id=@p AND created_at > clock_timestamp() - interval '1 day'", ct, ("p", project)))!;

    private static async Task<DateTime?> LastRequestAt(NpgsqlConnection c, NpgsqlTransaction? tx, Guid project, CancellationToken ct) =>
        await Value(c, tx, "SELECT max(created_at) FROM ai_analyses WHERE project_id=@p", ct, ("p", project)) as DateTime?;

    private static async Task<Subject> AlarmSubject(NpgsqlConnection c, Guid project, Guid alarmId, CancellationToken ct)
    {
        var rows = await Rows(c, null, """
            SELECT f.code flow,t.business_id,t.is_test FROM alarms a JOIN transactions t ON t.id=a.transaction_id JOIN flows f ON f.id=t.flow_id
            WHERE a.project_id=@p AND a.id=@id
            """, ct, ("p", project), ("id", alarmId));
        if (rows.Count == 0) throw new PilotHttpException(404, "Alarm bu projede bulunamadı.");
        var r = rows[0];
        return new Subject("alarm", alarmId, project, (string)r["flow"]!, (string)r["business_id"]!, (bool)r["is_test"]!);
    }

    private static async Task<Subject> TransactionSubject(NpgsqlConnection c, Guid project, string flow, string transactionId, CancellationToken ct)
    {
        var rows = await Rows(c, null, """
            SELECT t.id,t.is_test FROM transactions t JOIN flows f ON f.id=t.flow_id WHERE t.project_id=@p AND f.code=@flow AND t.business_id=@b
            """, ct, ("p", project), ("flow", flow), ("b", transactionId));
        if (rows.Count == 0) throw new PilotHttpException(404, "İşlem bu projede bulunamadı.");
        var id = (Guid)rows[0]["id"]!;
        return new Subject("transaction", id, project, flow, transactionId, (bool)rows[0]["is_test"]!);
    }

    private static async Task<List<Dictionary<string, object?>>> Listing(NpgsqlConnection c, ProjectIdentity actor, Subject subject, Guid? only, string currentHash, CancellationToken ct)
    {
        var rows = await Rows(c, null, """
            SELECT a.id,a.created_at,a.subject_kind,a.provider,a.model,a.prompt_version,a.status,a.error,a.duration_ms,a.input_tokens,a.output_tokens,
                a.context_hash,a.evidence::text evidence,a.result::text result,
                u.name requested_by,f.rating my_rating,
                (SELECT count(*) FILTER (WHERE rating='useful') FROM ai_feedback WHERE analysis_id=a.id) useful_count,
                (SELECT count(*) FILTER (WHERE rating<>'useful') FROM ai_feedback WHERE analysis_id=a.id) critical_count
            FROM ai_analyses a LEFT JOIN app_users u ON u.id=a.requested_by
            LEFT JOIN ai_feedback f ON f.analysis_id=a.id AND f.user_id=CAST(@u AS uuid)
            WHERE a.project_id=@p AND a.subject_kind=@kind AND a.subject_id=@s AND (CAST(@only AS uuid) IS NULL OR a.id=@only)
            ORDER BY a.created_at DESC LIMIT 10
            """, ct, ("p", actor.ProjectId), ("kind", subject.Kind), ("s", subject.Id), ("u", actor.UserId), ("only", only));
        foreach (var row in rows)
        {
            row["evidence"] = JsonNode.Parse((string)row["evidence"]!);
            row["result"] = row["result"] is string r ? JsonNode.Parse(r) : null;
            // A completed answer belongs to the evidence it saw; new events make it stale rather than silently current.
            row["stale"] = (string)row["status"]! == "completed" && (string?)row["context_hash"] != currentHash;
            row.Remove("context_hash");
        }
        return rows;
    }

    /// <summary>Same organization, project, subject, provider, model, prompt and (non-volatile) evidence → same hash.</summary>
    private string ContextHash(ProjectIdentity actor, Subject subject, JsonArray evidence)
    {
        var stable = (JsonArray)JsonNode.Parse(evidence.ToJsonString())!;
        foreach (var item in stable) (item as JsonObject)?.Remove("last_checked_at");
        var text = string.Join('|', actor.OrganizationId, actor.ProjectId, subject.Kind, subject.Id, model.Provider, model.Model, InsightPrompt.Version, stable.ToJsonString());
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>The facts the model may use, each with a short id it must cite.</summary>
    private async Task<JsonArray> Evidence(NpgsqlConnection c, Subject subject, CancellationToken ct)
    {
        var evidence = new JsonArray();
        var alarms = await Rows(c, null, subject.Kind == "alarm" ? """
            SELECT a.id,a.kind,a.step_code,a.error,a.deadline_at,a.opened_at,a.last_seen_at,a.resolved_at,a.resolution,a.acknowledged_at,a.assigned_to IS NOT NULL assigned,t.flow_id
            FROM alarms a JOIN transactions t ON t.id=a.transaction_id WHERE a.project_id=@p AND a.id=@id
            """ : """
            SELECT a.id,a.kind,a.step_code,a.error,a.deadline_at,a.opened_at,a.last_seen_at,a.resolved_at,a.resolution,a.acknowledged_at,a.assigned_to IS NOT NULL assigned,t.flow_id
            FROM alarms a JOIN transactions t ON t.id=a.transaction_id WHERE a.project_id=@p AND a.transaction_id=@id
            ORDER BY a.resolved_at IS NOT NULL, a.opened_at DESC LIMIT 5
            """, ct, ("p", subject.ProjectId), ("id", subject.Id));
        var n = 0;
        foreach (var a in alarms)
            evidence.Add(Item($"A{++n}", "alarm", new
            {
                kind = a["kind"], step = a["step_code"], error = Clip(a["error"], 500), deadline_at = a["deadline_at"], opened_at = a["opened_at"],
                last_checked_at = a["last_seen_at"], resolved_at = a["resolved_at"], resolution = a["resolution"],
                acknowledged = a["acknowledged_at"] is not null, assigned = a["assigned"],
            }));

        var view = await tracking.GetTransactionAsync(subject.ProjectId, subject.Flow, subject.BusinessId, ct);
        if (view is not null)
        {
            evidence.Add(Item("T1", "transaction", new
            {
                flow = view.Flow, transaction_id = view.TransactionId, status = view.Status.ToString(), group = view.Group, flow_version = view.FlowVersion,
                calendar = view.Calendar, accepted_events = view.AcceptedEventCount, processed_events = view.ProcessedEventCount, is_test = subject.IsTest,
                delay = view.Delay is null ? null : new { state = view.Delay.State, step = view.Delay.Step, started_at = view.Delay.StartedAt, deadline_at = view.Delay.DeadlineAt },
            }));
            n = 0;
            foreach (var s in view.Steps)
                evidence.Add(Item($"S{++n}", "step", new
                {
                    code = s.Code, name = s.Name, stage = s.EffectiveStage, kind = s.Kind, max_delay_seconds = s.MaxDelaySeconds,
                    observation = s.Observation, occurred_at = s.OccurredAt, error = Clip(s.Error, 300),
                }));
            n = 0;
            foreach (var e in view.Events.OrderByDescending(e => e.OccurredAt).Take(30).Reverse())
                evidence.Add(Item($"E{++n}", "event", new
                {
                    step = e.Step, status = e.Status.ToString(), occurred_at = e.OccurredAt, received_at = e.ReceivedAt,
                    report_lag_seconds = (long)(e.ReceivedAt - e.OccurredAt).TotalSeconds, error = Clip(e.Error, 300),
                }));
        }
        if (subject.Kind != "alarm" || alarms.Count == 0) return evidence;

        var alarm = alarms[0];
        var i = 0;
        foreach (var d in await Rows(c, null, """
            SELECT ch.kind channel_kind,e.kind event,d.state,d.attempts,d.last_error,d.sent_at
            FROM notification_deliveries d JOIN alarm_events e ON e.id=d.event_id JOIN notification_channels ch ON ch.id=d.channel_id
            WHERE e.alarm_id=@id ORDER BY d.created_at LIMIT 20
            """, ct, ("id", subject.Id)))
            evidence.Add(Item($"D{++i}", "delivery", new { channel_kind = d["channel_kind"], @event = d["event"], state = d["state"],
                attempts = d["attempts"], last_error = Clip(d["last_error"], 300), sent_at = d["sent_at"] }));

        i = 0;
        foreach (var note in await Rows(c, null, "SELECT body,created_at FROM alarm_notes WHERE alarm_id=@id ORDER BY created_at DESC LIMIT 5", ct, ("id", subject.Id)))
            evidence.Add(Item($"N{++i}", "note", new { body = Clip(note["body"], 500), created_at = note["created_at"] }));

        var similar = await Rows(c, null, """
            SELECT x.kind,x.opened_at,x.resolved_at,x.error,t.business_id FROM alarms x JOIN transactions t ON t.id=x.transaction_id
            WHERE x.project_id=@p AND t.flow_id=@f AND x.step_code=@s AND x.id<>@id
                AND x.opened_at BETWEEN CAST(@at AS timestamptz) - interval '6 hours' AND CAST(@at AS timestamptz) + interval '6 hours'
            ORDER BY x.opened_at DESC
            """, ct, ("p", subject.ProjectId), ("f", alarm["flow_id"]), ("s", alarm["step_code"]), ("id", subject.Id), ("at", alarm["opened_at"]));
        evidence.Add(Item("B1", "similar_alarms", new
        {
            window_hours = 6, count = similar.Count,
            failed = similar.Count(x => (string)x["kind"]! == "failed"), overdue = similar.Count(x => (string)x["kind"]! == "overdue"),
            still_open = similar.Count(x => x["resolved_at"] is null),
            samples = similar.Take(5).Select(x => new { transaction_id = x["business_id"], kind = x["kind"], opened_at = x["opened_at"],
                resolved = x["resolved_at"] is not null, error = Clip(x["error"], 200) }),
        }));
        return evidence;
    }

    private static JsonNode Item(string id, string type, object data)
    {
        var node = JsonSerializer.SerializeToNode(data, Json)!.AsObject();
        node.Insert(0, "type", type); node.Insert(0, "id", id);
        return node;
    }

    private static string? Clip(object? value, int max) => value is string s ? (s.Length <= max ? s : s[..max] + "…") : null;
}
