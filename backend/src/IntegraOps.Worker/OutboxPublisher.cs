using IntegraOps.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace IntegraOps.Worker;

public sealed class OutboxPublisher(ConnectionFactory factory, OutboxDispatcher dispatcher,
    Microsoft.Extensions.Configuration.IConfiguration configuration, ILogger<OutboxPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Rows are published in batches so one confirmation round trip covers many events (docs/olcek.md).
        var batchSize = Math.Clamp(Microsoft.Extensions.Configuration.ConfigurationBinder.GetValue(configuration, "Outbox:BatchSize", 50), 1, 500);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await factory.CreateConnectionAsync(stoppingToken);
                await using var channel = await connection.CreateChannelAsync(
                    new CreateChannelOptions(publisherConfirmationsEnabled: true,
                        publisherConfirmationTrackingEnabled: true), stoppingToken);
                await Messaging.DeclareAsync(channel, stoppingToken);
                logger.LogInformation("Olay aktarımı hazır.");
                while (!stoppingToken.IsCancellationRequested && channel.IsOpen)
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    deadline.CancelAfter(TimeSpan.FromSeconds(10));
                    var published = await dispatcher.PublishBatchAsync(channel, batchSize, deadline.Token);
                    if (published == 0) await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
                    else IntegraOpsMetrics.OutboxPublished.Add(published);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // Do not log connection URIs, event bodies or database parameter values.
                logger.LogWarning("Olay aktarımı bekliyor ({ErrorType}); kalıcı kayıtlar tekrar denenecek.", ex.GetType().Name);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
