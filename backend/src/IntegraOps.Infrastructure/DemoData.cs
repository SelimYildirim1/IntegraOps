using IntegraOps.Application;
using IntegraOps.Domain;
using Npgsql;
using static IntegraOps.Infrastructure.PilotDb;

namespace IntegraOps.Infrastructure;

public sealed record DemoInput(int? Count, int? Days);

/// <summary>
/// Fills a flow with realistic sample transactions (DEMO- prefix) so a new project shows a living board before
/// any source system is connected. Events go through the normal acceptance path; only the creation time is back-dated
/// so trends spread over the chosen window. Sample data is visible like real data and removed with one call.
/// </summary>
public sealed class DemoData(NpgsqlDataSource source, ITrackingStore tracking)
{
    private enum Scenario { Clean, Late, Stuck, Failed, Recovered, Cancelled, Skipped }

    private static readonly string[] GenericErrors =
    [
        "Bağlantı zaman aşımı: kaynak servis 30 sn içinde yanıt vermedi", "Doğrulama hatası: zorunlu alan eksik",
        "Yetki hatası: API anahtarı reddedildi (401)", "Kayıt bulunamadı: kaynak sistemde eşleşen belge yok", "Kuyruk dolu (429); yeniden denenecek",
    ];

    public async Task<object> Generate(ProjectIdentity actor, string flowCode, int count, int days, CancellationToken ct)
    {
        if (count is < 1 or > 200) throw new PilotHttpException(400, "Örnek işlem sayısı 1–200 arasında olmalı.");
        if (days is < 1 or > 30) throw new PilotHttpException(400, "Örnek veri dönemi 1–30 gün arasında olmalı.");
        FlowDefinition flow;
        await using (var c = await source.OpenConnectionAsync(ct))
        {
            flow = await FlowVersions.LoadAsync(c, null, actor.ProjectId, flowCode, null, null, ct) ?? throw new PilotHttpException(404, "Akış bu projede bulunamadı.");
            var existing = (long)(await Value(c, null, "SELECT count(*) FROM transactions WHERE flow_id=@f AND business_id LIKE 'DEMO-%'", ct, ("f", flow.Id)))!;
            if (existing + count > 1000) throw new PilotHttpException(409, "Bu akışta çok fazla örnek veri var. Önce mevcut örnek veriyi silin.");
        }
        var template = FlowTemplates.All.FirstOrDefault(t => t.Steps.Select(s => s.Code).SequenceEqual(flow.Steps.Select(s => s.Code)));
        var groups = flow.Steps.SelectMany(s => (s.Groups ?? []).Concat(s.Overrides?.Keys ?? [])).Distinct().ToArray();
        var stages = flow.Steps.GroupBy(s => s.EffectiveStage).OrderBy(g => g.Key).Select(g => g.OrderBy(s => s.Position).ToArray()).ToArray();
        var now = DateTimeOffset.UtcNow;
        var rnd = Random.Shared;
        var prefix = string.Concat(flow.Code.Split('-').Select(p => char.ToUpperInvariant(p[0])));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var created = new List<(string Id, DateTimeOffset Start)>();
        var events = 0;

        for (var i = 0; i < count; i++)
        {
            string id;
            do id = $"DEMO-{prefix}-{rnd.Next(100000, 999999)}"; while (!ids.Add(id));
            // Most of the sample spreads over the window; a share starts within the last 12 hours so the board shows live work.
            var ageMinutes = rnd.NextDouble() < 0.3 ? rnd.Next(5, 12 * 60) : rnd.Next(60, days * 1440);
            var start = now.AddMinutes(-ageMinutes);
            var group = groups.Length > 0 && rnd.NextDouble() < 0.25 ? groups[rnd.Next(groups.Length)] : null;
            var scenario = Roll(rnd);
            var troubleStage = stages.Length > 1 ? rnd.Next(1, stages.Length) : 0;
            var list = new List<(FlowStep Step, EventStatus Status, DateTimeOffset At, string? Error)> { (stages[0][0], EventStatus.Completed, start, null) };
            var stageStart = start; var stop = false;
            for (var si = 1; si < stages.Length && !stop; si++)
            {
                var stageEnd = stageStart;
                foreach (var step in stages[si])
                {
                    var expected = step.AppliesTo(group);
                    if (step.Kind == StepKind.Optional) { if (rnd.NextDouble() > 0.4) continue; }
                    else if (!expected) continue;
                    var allowance = step.AllowanceFor(group) ?? Hour;
                    var factor = scenario == Scenario.Late && si == troubleStage && expected ? 1.1 + rnd.NextDouble() * 0.9 : 0.1 + rnd.NextDouble() * 0.75;
                    var at = stageStart.AddSeconds(allowance * factor);
                    if (at > now) { stop = true; break; } // not happened yet: the transaction is still in progress
                    if (expected && si == troubleStage && scenario is Scenario.Stuck or Scenario.Failed or Scenario.Recovered or Scenario.Cancelled or Scenario.Skipped)
                    {
                        switch (scenario)
                        {
                            case Scenario.Stuck: stop = true; break;
                            case Scenario.Failed: list.Add((step, EventStatus.Failed, at, Error(template, step.Code, rnd))); stop = true; break;
                            case Scenario.Cancelled: list.Add((step, EventStatus.Cancelled, at, "Müşteri talebiyle iptal edildi")); stop = true; break;
                            case Scenario.Skipped: list.Add((step, EventStatus.Skipped, at, null)); break;
                            case Scenario.Recovered:
                                list.Add((step, EventStatus.Failed, at, Error(template, step.Code, rnd)));
                                var fixedAt = at.AddSeconds(allowance * (0.2 + rnd.NextDouble() * 0.3));
                                if (fixedAt > now) stop = true; else list.Add((step, EventStatus.Completed, fixedAt, null));
                                at = fixedAt;
                                break;
                        }
                        if (stop) break;
                    }
                    else list.Add((step, EventStatus.Completed, at, null));
                    if (expected && at > stageEnd) stageEnd = at;
                }
                stageStart = stageEnd;
            }
            foreach (var (step, status, at, error) in list.OrderBy(e => e.At))
            {
                await tracking.AcceptAsync(actor.ProjectId, new ValidatedEvent(Guid.CreateVersion7(), flow.Code, id, step.Code, status, at, error, group), ct);
                events++;
            }
            created.Add((id, start));
        }

        await using (var c = await source.OpenConnectionAsync(ct))
        await using (var tx = await c.BeginTransactionAsync(ct))
        {
            foreach (var (id, start) in created)
                await Run(c, tx, "UPDATE transactions SET created_at=@s WHERE project_id=@p AND flow_id=@f AND business_id=@b", ct,
                    ("s", start), ("p", actor.ProjectId), ("f", flow.Id), ("b", id));
            await Audit(c, tx, actor.ProjectId, actor.UserId, "demo.generate", $"{flow.Code}:{count}", ct);
            await tx.CommitAsync(ct);
        }
        return new { flow = flow.Code, transactions = count, events };
    }

    /// <summary>Removes every DEMO- transaction of the flow with its events, alarms and deliveries.</summary>
    public async Task<object> Delete(ProjectIdentity actor, string flowCode, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var flowId = await Value(c, tx, "SELECT id FROM flows WHERE project_id=@p AND code=@c", ct, ("p", actor.ProjectId), ("c", flowCode)) as Guid?
            ?? throw new PilotHttpException(404, "Akış bu projede bulunamadı.");
        await Run(c, tx, """
            CREATE TEMP TABLE doomed ON COMMIT DROP AS
            SELECT t.id FROM transactions t WHERE t.project_id=@p AND t.flow_id=@f AND t.business_id LIKE 'DEMO-%'
            """, ct, ("p", actor.ProjectId), ("f", flowId));
        var count = (int)(long)(await Value(c, tx, "SELECT count(*) FROM doomed", ct))!;
        if (count == 0) { await tx.CommitAsync(ct); return new { transactions = 0 }; }
        if (await Value(c, tx, "SELECT 1 FROM transaction_events e WHERE e.processed_at IS NULL AND e.transaction_id IN (SELECT id FROM doomed) LIMIT 1", ct) is not null)
            throw new PilotHttpException(409, "Örnek verinin bir kısmı hâlâ işleniyor. Birkaç saniye sonra tekrar deneyin.");
        await Run(c, tx, """
            DELETE FROM notification_deliveries WHERE event_id IN (SELECT e.id FROM alarm_events e JOIN alarms a ON a.id=e.alarm_id WHERE a.transaction_id IN (SELECT id FROM doomed));
            DELETE FROM alarm_events WHERE alarm_id IN (SELECT id FROM alarms WHERE transaction_id IN (SELECT id FROM doomed));
            DELETE FROM alarm_notes WHERE alarm_id IN (SELECT id FROM alarms WHERE transaction_id IN (SELECT id FROM doomed));
            DELETE FROM ai_analyses WHERE subject_kind='alarm' AND subject_id IN (SELECT id FROM alarms WHERE transaction_id IN (SELECT id FROM doomed));
            DELETE FROM ai_analyses WHERE subject_kind='transaction' AND subject_id IN (SELECT id FROM doomed);
            DELETE FROM alarms WHERE transaction_id IN (SELECT id FROM doomed);
            DELETE FROM transaction_delays WHERE transaction_id IN (SELECT id FROM doomed);
            DELETE FROM transaction_step_stats WHERE transaction_id IN (SELECT id FROM doomed);
            DELETE FROM outbox_messages o USING transaction_events e WHERE e.project_id=o.project_id AND e.event_id=o.event_id AND e.transaction_id IN (SELECT id FROM doomed);
            DELETE FROM transaction_events WHERE transaction_id IN (SELECT id FROM doomed);
            DELETE FROM transactions WHERE id IN (SELECT id FROM doomed)
            """, ct);
        await Audit(c, tx, actor.ProjectId, actor.UserId, "demo.delete", $"{flowCode}:{count}", ct);
        await tx.CommitAsync(ct);
        return new { transactions = count };
    }

    private const int Hour = 3600;

    private static Scenario Roll(Random rnd)
    {
        var r = rnd.NextDouble();
        return r < 0.58 ? Scenario.Clean : r < 0.70 ? Scenario.Late : r < 0.80 ? Scenario.Stuck : r < 0.88 ? Scenario.Failed
            : r < 0.94 ? Scenario.Recovered : r < 0.97 ? Scenario.Cancelled : Scenario.Skipped;
    }

    private static string Error(FlowTemplate? template, string step, Random rnd)
    {
        var pool = template is not null && template.Errors.TryGetValue(step, out var list) ? list : GenericErrors;
        return pool[rnd.Next(pool.Length)];
    }
}
