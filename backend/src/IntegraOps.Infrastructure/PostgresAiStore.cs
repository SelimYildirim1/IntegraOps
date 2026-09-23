using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using IntegraOps.Application;
using Microsoft.Extensions.Logging;
using Npgsql;
using static IntegraOps.Infrastructure.PilotDb;

namespace IntegraOps.Infrastructure;

/// <summary>
/// Evidence-based alarm analysis. The backend gathers the facts (alarm, timeline, deliveries, notes, similar alarms),
/// the model only explains them, and every answer is stored with the evidence it was given.
/// </summary>
public sealed class PostgresAiStore(NpgsqlDataSource source, ITrackingStore tracking, IAiModelClient model, AiLimits limits,
    ILogger<PostgresAiStore> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static readonly string[] Ratings = ["useful", "wrong", "incomplete"];

    public async Task<object> Status(Guid organization, Guid project, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var plan = await PostgresOrganizationStore.AiEnabledFor(c, organization, ct);
        // enabled = usable right now; configured and planEnabled explain why not.
        return new { enabled = model.Enabled && plan, configured = model.Enabled, planEnabled = plan, provider = model.Provider, model = model.Model,
            dailyLimit = limits.DailyPerProject, usedToday = model.Enabled ? await UsedToday(c, project, ct) : 0 };
    }

    public async Task<object> AnalyzeAlarm(ProjectIdentity actor, Guid alarmId, CancellationToken ct)
    {
        if (!model.Enabled) throw new PilotHttpException(503, "Yapay zekâ henüz yapılandırılmadı.");
        await using var c = await source.OpenConnectionAsync(ct);
        if (!await PostgresOrganizationStore.AiEnabledFor(c, actor.OrganizationId, ct))
            throw new PilotHttpException(403, "Yapay zekâ asistanı bu organizasyonun planında yok.");
        if (await UsedToday(c, actor.ProjectId, ct) >= limits.DailyPerProject)
            throw new PilotHttpException(429, "Bu proje için günlük yapay zekâ inceleme sınırına ulaşıldı.");
        var evidence = await AlarmEvidence(c, actor.ProjectId, alarmId, ct);
        var ids = evidence.Select(e => e!["id"]!.GetValue<string>()).ToHashSet();
        var prompt = new AiPrompt(AlarmInsightPrompt.System, new JsonObject { ["evidence"] = evidence }.ToJsonString(), limits.MaxOutputTokens);

        var watch = Stopwatch.StartNew();
        AiCompletion? completion = null; AlarmInsight? insight = null; string? error = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(limits.Timeout);
            completion = await model.CompleteAsync(prompt, timeout.Token);
            insight = AlarmInsightPrompt.Parse(completion.Text, ids);
        }
        catch (Exception ex) when (ex is not PilotHttpException && !ct.IsCancellationRequested)
        {
            // Store the failure class only; provider responses may echo the prompt.
            error = ex is FormatException or JsonException ? "Model yanıtı beklenen biçimde değil." :
                ex is OperationCanceledException ? "Model zaman aşımına uğradı." : "Model çağrısı başarısız: " + ex.GetType().Name;
            logger.LogWarning("AI alarm analizi başarısız. Proje: {Project} Alarm: {Alarm} Neden: {Reason}", actor.ProjectId, alarmId, error);
        }
        var id = Guid.CreateVersion7();
        await using (var tx = await c.BeginTransactionAsync(ct))
        {
            await Run(c, tx, """
                INSERT INTO ai_analyses(id,project_id,subject_kind,subject_id,requested_by,provider,model,prompt_version,status,evidence,result,error,input_tokens,output_tokens,duration_ms)
                VALUES (@id,@p,'alarm',@s,CAST(@u AS uuid),@provider,@model,@v,@status,CAST(@e AS jsonb),CAST(@r AS jsonb),@err,@in,@out,@ms)
                """, ct, ("id", id), ("p", actor.ProjectId), ("s", alarmId), ("u", actor.UserId), ("provider", model.Provider), ("model", model.Model),
                ("v", AlarmInsightPrompt.Version), ("status", insight is null ? "failed" : "completed"), ("e", evidence.ToJsonString()),
                ("r", insight is null ? null : JsonSerializer.Serialize(insight, Json)), ("err", error),
                ("in", completion?.InputTokens), ("out", completion?.OutputTokens), ("ms", (int)watch.ElapsedMilliseconds));
            await Audit(c, tx, actor.ProjectId, actor.UserId, "ai.alarm_analysis", alarmId.ToString(), ct);
            await tx.CommitAsync(ct);
        }
        if (insight is null) throw new PilotHttpException(502, "Yapay zekâ yanıtı alınamadı. " + error);
        return (await Analyses(c, actor, alarmId, id, ct))[0];
    }

    public async Task<List<Dictionary<string, object?>>> AlarmAnalyses(ProjectIdentity actor, Guid alarmId, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        return await Analyses(c, actor, alarmId, null, ct);
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

    private static async Task<int> UsedToday(NpgsqlConnection c, Guid project, CancellationToken ct) => (int)(long)(await Value(c, null,
        "SELECT count(*) FROM ai_analyses WHERE project_id=@p AND created_at > clock_timestamp() - interval '1 day'", ct, ("p", project)))!;

    private static async Task<List<Dictionary<string, object?>>> Analyses(NpgsqlConnection c, ProjectIdentity actor, Guid alarmId, Guid? only, CancellationToken ct)
    {
        var rows = await Rows(c, null, """
            SELECT a.id,a.created_at,a.provider,a.model,a.prompt_version,a.status,a.error,a.duration_ms,a.evidence::text evidence,a.result::text result,
                u.name requested_by,f.rating my_rating,
                (SELECT count(*) FILTER (WHERE rating='useful') FROM ai_feedback WHERE analysis_id=a.id) useful_count,
                (SELECT count(*) FILTER (WHERE rating<>'useful') FROM ai_feedback WHERE analysis_id=a.id) critical_count
            FROM ai_analyses a LEFT JOIN app_users u ON u.id=a.requested_by
            LEFT JOIN ai_feedback f ON f.analysis_id=a.id AND f.user_id=CAST(@u AS uuid)
            WHERE a.project_id=@p AND a.subject_kind='alarm' AND a.subject_id=@s AND (CAST(@only AS uuid) IS NULL OR a.id=@only)
            ORDER BY a.created_at DESC LIMIT 10
            """, ct, ("p", actor.ProjectId), ("s", alarmId), ("u", actor.UserId), ("only", only));
        foreach (var row in rows)
        {
            row["evidence"] = JsonNode.Parse((string)row["evidence"]!);
            row["result"] = row["result"] is string r ? JsonNode.Parse(r) : null;
        }
        return rows;
    }

    /// <summary>The facts the model may use, each with a short id it must cite.</summary>
    private async Task<JsonArray> AlarmEvidence(NpgsqlConnection c, Guid project, Guid alarmId, CancellationToken ct)
    {
        var alarms = await Rows(c, null, """
            SELECT a.kind,a.step_code,a.error,a.deadline_at,a.opened_at,a.last_seen_at,a.resolved_at,a.resolution,a.acknowledged_at,
                a.assigned_to IS NOT NULL assigned,a.transaction_id,t.flow_id,f.code flow,t.business_id
            FROM alarms a JOIN transactions t ON t.id=a.transaction_id JOIN flows f ON f.id=t.flow_id
            WHERE a.project_id=@p AND a.id=@id
            """, ct, ("p", project), ("id", alarmId));
        if (alarms.Count == 0) throw new PilotHttpException(404, "Alarm bu projede bulunamadı.");
        var a = alarms[0];
        var evidence = new JsonArray(Item("A1", "alarm", new
        {
            kind = a["kind"], step = a["step_code"], error = Clip(a["error"], 500), deadline_at = a["deadline_at"], opened_at = a["opened_at"],
            last_checked_at = a["last_seen_at"], resolved_at = a["resolved_at"], resolution = a["resolution"],
            acknowledged = a["acknowledged_at"] is not null, assigned = a["assigned"],
        }));

        var view = await tracking.GetTransactionAsync(project, (string)a["flow"]!, (string)a["business_id"]!, ct);
        if (view is not null)
        {
            evidence.Add(Item("T1", "transaction", new
            {
                flow = view.Flow, transaction_id = view.TransactionId, status = view.Status.ToString(), group = view.Group, flow_version = view.FlowVersion,
                calendar = view.Calendar, accepted_events = view.AcceptedEventCount, processed_events = view.ProcessedEventCount,
                delay = view.Delay is null ? null : new { state = view.Delay.State, step = view.Delay.Step, started_at = view.Delay.StartedAt, deadline_at = view.Delay.DeadlineAt },
            }));
            var n = 0;
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

        var i = 0;
        foreach (var d in await Rows(c, null, """
            SELECT ch.kind channel_kind,e.kind event,d.state,d.attempts,d.last_error,d.sent_at
            FROM notification_deliveries d JOIN alarm_events e ON e.id=d.event_id JOIN notification_channels ch ON ch.id=d.channel_id
            WHERE e.alarm_id=@id ORDER BY d.created_at LIMIT 20
            """, ct, ("id", alarmId)))
            evidence.Add(Item($"D{++i}", "delivery", new { channel_kind = d["channel_kind"], @event = d["event"], state = d["state"],
                attempts = d["attempts"], last_error = Clip(d["last_error"], 300), sent_at = d["sent_at"] }));

        i = 0;
        foreach (var note in await Rows(c, null, "SELECT body,created_at FROM alarm_notes WHERE alarm_id=@id ORDER BY created_at DESC LIMIT 5", ct, ("id", alarmId)))
            evidence.Add(Item($"N{++i}", "note", new { body = Clip(note["body"], 500), created_at = note["created_at"] }));

        var similar = await Rows(c, null, """
            SELECT x.kind,x.opened_at,x.resolved_at,x.error,t.business_id FROM alarms x JOIN transactions t ON t.id=x.transaction_id
            WHERE x.project_id=@p AND t.flow_id=@f AND x.step_code=@s AND x.id<>@id
                AND x.opened_at BETWEEN CAST(@at AS timestamptz) - interval '6 hours' AND CAST(@at AS timestamptz) + interval '6 hours'
            ORDER BY x.opened_at DESC
            """, ct, ("p", project), ("f", a["flow_id"]), ("s", a["step_code"]), ("id", alarmId), ("at", a["opened_at"]));
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
