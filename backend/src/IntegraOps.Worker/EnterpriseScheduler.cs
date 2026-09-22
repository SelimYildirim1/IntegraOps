using IntegraOps.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IntegraOps.Worker;

/// <summary>Hourly commercial housekeeping: trial reminders and purging organizations whose deletion grace period ended.</summary>
public sealed class EnterpriseScheduler(PostgresOrganizationStore organizations, PostgresSecurityStore security, ILogger<EnterpriseScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                var reminders = await organizations.SendTrialReminders(stoppingToken);
                if (reminders > 0) logger.LogInformation("{Count} deneme süresi hatırlatması kuyruğa alındı.", reminders);
                var purged = await security.PurgeDue(stoppingToken);
                if (purged > 0) logger.LogInformation("{Count} proje silme isteği gereği kalıcı olarak silindi.", purged);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Kurumsal bakım turu tamamlanamadı ({ErrorType}).", ex.GetType().Name); }
        } while (await Wait(timer, stoppingToken));
    }

    private static async Task<bool> Wait(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); } catch (OperationCanceledException) { return false; }
    }
}
