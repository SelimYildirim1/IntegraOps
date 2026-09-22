using IntegraOps.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using static IntegraOps.Infrastructure.PilotDb;

namespace IntegraOps.Worker;

public sealed class NotificationDispatcher(NpgsqlDataSource source, IDataProtectionProvider protection,
    NotificationTransport transport, IConfiguration configuration, ILogger<NotificationDispatcher> logger) : BackgroundService
{
    private readonly IDataProtector protector = protection.CreateProtector("notification-channels.v1");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await Next(stoppingToken)) await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("Bildirim kuyruğuna erişilemedi. ErrorType: {ErrorType}", ex.GetType().Name);
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task<bool> Next(CancellationToken ct)
    {
        var lease = Guid.NewGuid(); Dictionary<string, object?> job;
        await using (var c = await source.OpenConnectionAsync(ct))
        await using (var tx = await c.BeginTransactionAsync(ct))
        {
            await Run(c, tx, """
                UPDATE notification_deliveries SET state='dead',last_error='attempt_limit_after_restart'
                WHERE state='sending' AND lease_until<clock_timestamp() AND attempts>=8;
                UPDATE notification_deliveries d SET state='cancelled' FROM notification_channels c
                WHERE c.id=d.channel_id AND NOT c.enabled AND (d.state='pending' OR (d.state='sending' AND d.lease_until<clock_timestamp()))
                """, ct);
            var rows = await Rows(c, tx, """
                SELECT d.id,d.body,c.kind,c.target_protected,c.secret_protected,d.attempts
                FROM notification_deliveries d JOIN notification_channels c ON c.id=d.channel_id
                JOIN alarm_events e ON e.id=d.event_id
                WHERE c.enabled AND d.attempts<8 AND ((d.state='pending' AND d.available_at<=clock_timestamp())
                    OR (d.state='sending' AND d.lease_until<clock_timestamp()))
                    AND NOT EXISTS(SELECT 1 FROM notification_deliveries old JOIN alarm_events oe ON oe.id=old.event_id
                        WHERE old.channel_id=d.channel_id AND oe.alarm_id=e.alarm_id
                            AND old.state IN ('pending','sending') AND (old.created_at,old.id)<(d.created_at,d.id))
                ORDER BY d.available_at,d.id LIMIT 1 FOR UPDATE OF d SKIP LOCKED
                """, ct);
            if (rows.Count == 0) { await tx.CommitAsync(ct); return false; }
            job = rows[0];
            await Run(c, tx, """
                UPDATE notification_deliveries SET state='sending',attempts=attempts+1,lease_token=@l,
                    lease_until=clock_timestamp()+interval '60 seconds' WHERE id=@id
                """, ct, ("l", lease), ("id", job["id"]));
            await tx.CommitAsync(ct);
        }
        string? error = null; var permanent = false;
        try
        {
            await transport.Send((Guid)job["id"]!, (string)job["kind"]!, protector.Unprotect((string)job["target_protected"]!),
                job["secret_protected"] is string s ? protector.Unprotect(s) : null, (string)job["body"]!, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DeliveryException ex) { error = ex.Message; permanent = ex.Permanent; }
        catch (PilotHttpException) { error = "webhook_target_rejected"; permanent = true; }
        catch (Exception ex) { error = "transport_" + ex.GetType().Name; }
        var attempts = (int)job["attempts"]! + 1;
        var state = error is null ? "sent" : permanent || attempts >= 8 ? "dead" : "pending";
        IntegraOpsMetrics.Deliveries.Add(1, new KeyValuePair<string, object?>("kind", (string)job["kind"]!),
            new KeyValuePair<string, object?>("outcome", state == "pending" ? "retry" : state));
        var seconds = Math.Min(3600, Math.Max(1, configuration.GetValue("Notifications:RetryBaseSeconds", 30)) * Math.Pow(2, attempts - 1))
            + Random.Shared.Next(0, 4);
        await using var finish = await source.OpenConnectionAsync(ct);
        await Run(finish, null, """
            UPDATE notification_deliveries SET state=@s,last_error=@e,available_at=clock_timestamp()+@delay,
                lease_token=NULL,lease_until=NULL,sent_at=CASE WHEN @s='sent' THEN clock_timestamp() ELSE NULL END
            WHERE id=@id AND lease_token=@l AND state='sending'
            """, ct, ("s", state), ("e", error), ("delay", TimeSpan.FromSeconds(seconds)), ("id", job["id"]), ("l", lease));
        return true;
    }
}
