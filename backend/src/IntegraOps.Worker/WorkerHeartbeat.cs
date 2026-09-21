using IntegraOps.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace IntegraOps.Worker;

public sealed class WorkerHeartbeat(NpgsqlDataSource source, ILogger<WorkerHeartbeat> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var c = await source.OpenConnectionAsync(stoppingToken);
                await PilotDb.Run(c, null, """
                    INSERT INTO worker_heartbeats(name,seen_at) VALUES('worker',clock_timestamp())
                    ON CONFLICT(name) DO UPDATE SET seen_at=EXCLUDED.seen_at
                    """, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Yaşam sinyali yazılamadı. ErrorType: {Type}", ex.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
