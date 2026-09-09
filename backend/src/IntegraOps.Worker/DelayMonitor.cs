using IntegraOps.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IntegraOps.Worker;

public sealed class DelayMonitor(
    PostgresDelayStore store,
    IConfiguration configuration,
    ILogger<DelayMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        var interval = configuration.GetValue(
            "DelayMonitor:PollIntervalSeconds", 5);

        var batchSize = configuration.GetValue(
            "DelayMonitor:BatchSize", 100);

        if (interval is < 1 or > 60
            || batchSize is < 1 or > 500)
        {
            throw new InvalidOperationException(
                "Gecikme tarama aralığı 1–60 saniye, " +
                "parti boyutu 1–500 olmalı.");
        }

        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(interval));

        try
        {
            do
            {
                try
                {
                    int count;

                    do
                    {
                        count = await store.CheckDueAsync(
                            batchSize,
                            stoppingToken);
                    }
                    while (
                        count == batchSize
                        && !stoppingToken.IsCancellationRequested
                    );
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        "Gecikme kontrolü tamamlanamadı " +
                        "({ErrorType}); yeniden denenecek.",
                        ex.GetType().Name);
                }
            }
            while (
                await timer.WaitForNextTickAsync(stoppingToken)
            );
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}