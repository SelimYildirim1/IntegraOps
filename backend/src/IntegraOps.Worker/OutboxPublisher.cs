using IntegraOps.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace IntegraOps.Worker;

public sealed class OutboxPublisher(ConnectionFactory factory, OutboxDispatcher dispatcher,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
                    if (!await dispatcher.PublishNextAsync(channel, deadline.Token))
                        await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
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
