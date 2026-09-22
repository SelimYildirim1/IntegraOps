using IntegraOps.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using static IntegraOps.Infrastructure.PilotDb;

namespace IntegraOps.Worker;

/// <summary>
/// Applies each project's retention policy. Runs once at start-up and then daily, in small batches,
/// so long-running deletes never block event processing. Open alarms and unfinished transactions are never removed.
/// </summary>
public sealed class RetentionSweeper(NpgsqlDataSource source, IConfiguration configuration, ILogger<RetentionSweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(Math.Clamp(configuration.GetValue("Retention:IntervalHours", 24), 1, 168));
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { return; }
        using var timer = new PeriodicTimer(interval);
        do
        {
            try { await SweepAllAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Saklama süpürmesi tamamlanamadı ({ErrorType}); bir sonraki turda yeniden denenecek.", ex.GetType().Name); }
        } while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); } catch (OperationCanceledException) { return false; }
    }

    public async Task SweepAllAsync(CancellationToken ct)
    {
        List<Dictionary<string, object?>> policies;
        await using (var c = await source.OpenConnectionAsync(ct))
            policies = await Rows(c, null, """
                SELECT project_id,finished_transaction_days,notification_days,audit_days FROM retention_policies
                WHERE finished_transaction_days IS NOT NULL OR notification_days IS NOT NULL OR audit_days IS NOT NULL
                """, ct);
        foreach (var p in policies)
            await SweepProjectAsync((Guid)p["project_id"]!, p["finished_transaction_days"] as int?, p["notification_days"] as int?, p["audit_days"] as int?, ct);
    }

    public async Task<(int Transactions, int Deliveries, int Audit)> SweepProjectAsync(Guid project, int? transactionDays, int? notificationDays, int? auditDays, CancellationToken ct)
    {
        int transactions = 0, deliveries = 0, audit = 0;
        if (transactionDays is { } td)
            while (true)
            {
                var deleted = await DeleteTransactionBatch(project, td, ct);
                transactions += deleted;
                if (deleted < 200) break;
            }
        await using var c = await source.OpenConnectionAsync(ct);
        if (notificationDays is { } nd)
            deliveries = await Run(c, null, """
                DELETE FROM notification_deliveries d USING notification_channels ch
                WHERE ch.id=d.channel_id AND ch.project_id=@p AND d.state IN ('sent','dead','cancelled')
                    AND d.created_at < clock_timestamp() - make_interval(days => @d)
                """, ct, ("p", project), ("d", nd));
        if (auditDays is { } ad)
            audit = await Run(c, null, "DELETE FROM audit_log WHERE project_id=@p AND created_at < clock_timestamp() - make_interval(days => @d)", ct,
                ("p", project), ("d", ad));
        if (transactions + deliveries + audit > 0)
        {
            await Run(c, null, "INSERT INTO retention_runs(project_id,transactions_deleted,deliveries_deleted,audit_deleted) VALUES(@p,@t,@d,@a)", ct,
                ("p", project), ("t", transactions), ("d", deliveries), ("a", audit));
            logger.LogInformation("Saklama süpürmesi: {Transactions} işlem, {Deliveries} bildirim, {Audit} denetim kaydı silindi.", transactions, deliveries, audit);
        }
        return (transactions, deliveries, audit);
    }

    // One batch of finished transactions with every dependent row, in one transaction.
    private async Task<int> DeleteTransactionBatch(Guid project, int days, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        // CREATE TABLE AS reports no affected-row count through Npgsql; count the batch explicitly.
        await Run(c, tx, """
            CREATE TEMP TABLE doomed ON COMMIT DROP AS
            SELECT t.id FROM transactions t
            WHERE t.project_id=@p AND t.status IN ('Completed','Failed')
                AND t.updated_at < clock_timestamp() - make_interval(days => @d)
                AND NOT EXISTS (SELECT 1 FROM alarms a WHERE a.transaction_id=t.id AND a.resolved_at IS NULL)
                AND NOT EXISTS (SELECT 1 FROM transaction_events e WHERE e.transaction_id=t.id AND e.processed_at IS NULL)
            ORDER BY t.updated_at LIMIT 200
            """, ct, ("p", project), ("d", days));
        var count = (int)(long)(await Value(c, tx, "SELECT count(*) FROM doomed", ct))!;
        if (count == 0) { await tx.CommitAsync(ct); return 0; }
        await Run(c, tx, """
            DELETE FROM notification_deliveries WHERE event_id IN (SELECT e.id FROM alarm_events e JOIN alarms a ON a.id=e.alarm_id WHERE a.transaction_id IN (SELECT id FROM doomed));
            DELETE FROM alarm_events WHERE alarm_id IN (SELECT id FROM alarms WHERE transaction_id IN (SELECT id FROM doomed));
            DELETE FROM alarm_notes WHERE alarm_id IN (SELECT id FROM alarms WHERE transaction_id IN (SELECT id FROM doomed));
            DELETE FROM alarms WHERE transaction_id IN (SELECT id FROM doomed);
            DELETE FROM transaction_delays WHERE transaction_id IN (SELECT id FROM doomed);
            DELETE FROM transaction_step_stats WHERE transaction_id IN (SELECT id FROM doomed);
            DELETE FROM outbox_messages o USING transaction_events e WHERE e.project_id=o.project_id AND e.event_id=o.event_id AND e.transaction_id IN (SELECT id FROM doomed);
            DELETE FROM transaction_events WHERE transaction_id IN (SELECT id FROM doomed);
            DELETE FROM transactions WHERE id IN (SELECT id FROM doomed)
            """, ct);
        await tx.CommitAsync(ct);
        return count;
    }
}
