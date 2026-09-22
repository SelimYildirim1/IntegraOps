using IntegraOps.Infrastructure;

namespace IntegraOps.Api;

/// <summary>Builds requested data exports on the API host (which also serves the download) and removes expired files.</summary>
public sealed class ExportRunner(DataExporter exporter, ILogger<ExportRunner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        var sweep = 0;
        do
        {
            try
            {
                while (await exporter.RunPending(stoppingToken)) logger.LogInformation("Veri dışa aktarımı tamamlandı.");
                if (++sweep % 240 == 0) await exporter.RemoveExpired(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Dışa aktarım turu tamamlanamadı ({ErrorType}).", ex.GetType().Name); }
        } while (await Wait(timer, stoppingToken));
    }

    private static async Task<bool> Wait(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); } catch (OperationCanceledException) { return false; }
    }
}
