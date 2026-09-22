using IntegraOps.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using static IntegraOps.Infrastructure.PilotDb;

namespace IntegraOps.Worker;

/// <summary>
/// Sends queued account e-mails (invitations, password resets) with the alarm SMTP settings.
/// The API only queues; a mail server outage therefore never blocks the request that created the account.
/// </summary>
public sealed class MailDispatcher(NpgsqlDataSource source, NotificationTransport transport, IConfiguration configuration,
    ILogger<MailDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await Next(stoppingToken)) await Task.Delay(2000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("E-posta kuyruğuna erişilemedi. ErrorType: {ErrorType}", ex.GetType().Name);
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
            var rows = await Rows(c, tx, """
                SELECT id,recipient,subject,body,attempts FROM outbound_emails
                WHERE attempts<8 AND ((state='pending' AND available_at<=clock_timestamp()) OR (state='sending' AND lease_until<clock_timestamp()))
                ORDER BY available_at,id LIMIT 1 FOR UPDATE SKIP LOCKED
                """, ct);
            if (rows.Count == 0) { await tx.CommitAsync(ct); return false; }
            job = rows[0];
            await Run(c, tx, """
                UPDATE outbound_emails SET state='sending',attempts=attempts+1,lease_token=@l,lease_until=clock_timestamp()+interval '60 seconds' WHERE id=@id
                """, ct, ("l", lease), ("id", job["id"]));
            await tx.CommitAsync(ct);
        }
        string? error = null; var permanent = false;
        try { await transport.SendMail((string)job["recipient"]!, (string)job["subject"]!, (string)job["body"]!, (Guid)job["id"]!, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DeliveryException ex) { error = ex.Message; permanent = ex.Permanent; }
        catch (Exception ex) { error = "smtp_" + ex.GetType().Name; }
        var attempts = (int)job["attempts"]! + 1;
        var state = error is null ? "sent" : permanent || attempts >= 8 ? "dead" : "pending";
        var seconds = Math.Min(3600, Math.Max(1, configuration.GetValue("Notifications:RetryBaseSeconds", 30)) * Math.Pow(2, attempts - 1));
        if (state == "dead") logger.LogError("Hesap e-postası gönderilemedi ve denemeler durdu. Kayıt: {Id} Hata: {Error}", job["id"], error);
        IntegraOpsMetrics.Deliveries.Add(1, new KeyValuePair<string, object?>("kind", "account_email"),
            new KeyValuePair<string, object?>("outcome", state == "pending" ? "retry" : state));
        await using var finish = await source.OpenConnectionAsync(ct);
        await Run(finish, null, """
            UPDATE outbound_emails SET state=@s,last_error=@e,available_at=clock_timestamp()+@delay,lease_token=NULL,lease_until=NULL,
                sent_at=CASE WHEN @s='sent' THEN clock_timestamp() ELSE NULL END
            WHERE id=@id AND lease_token=@l AND state='sending'
            """, ct, ("s", state), ("e", error), ("delay", TimeSpan.FromSeconds(seconds)), ("id", job["id"]), ("l", lease));
        return true;
    }
}
