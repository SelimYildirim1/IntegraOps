using IntegraOps.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IntegraOps.Worker;

/// <summary>Sends the daily/weekly report digest at each project's configured local hour.</summary>
public sealed class DigestScheduler(PostgresReportQueries reports, Npgsql.NpgsqlDataSource source, ILogger<DigestScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try
            {
                // One replica sends digests per round; the others skip.
                await using var job = await JobLock.TryAcquire(source, "digests", stoppingToken);
                if (job is null) continue;
                foreach (var (project, period) in await reports.DueDigests(DateTimeOffset.UtcNow, stoppingToken))
                {
                    await reports.SendDigest(project, period, "schedule", null, stoppingToken);
                    logger.LogInformation("Rapor özeti kuyruğa alındı ({Period}).", period);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Rapor özeti gönderilemedi ({ErrorType}); bir sonraki turda yeniden denenecek.", ex.GetType().Name); }
        } while (await Wait(timer, stoppingToken));
    }

    private static async Task<bool> Wait(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); } catch (OperationCanceledException) { return false; }
    }
}
