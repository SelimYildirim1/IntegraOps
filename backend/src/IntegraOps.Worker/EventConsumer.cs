using System.Text.Json;
using IntegraOps.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace IntegraOps.Worker;

public sealed class EventConsumer(ConnectionFactory factory, PostgresTrackingStore store,
    Microsoft.Extensions.Configuration.IConfiguration configuration, ILogger<EventConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Events of one transaction serialize on the transaction row; unrelated transactions process in parallel.
        var concurrency = (ushort)Math.Clamp(Microsoft.Extensions.Configuration.ConfigurationBinder.GetValue(configuration, "EventConsumer:Concurrency", 1), 1, 64);
        var prefetch = (ushort)Math.Clamp(Microsoft.Extensions.Configuration.ConfigurationBinder.GetValue(configuration, "EventConsumer:Prefetch", 32), 1, 1000);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await factory.CreateConnectionAsync(stoppingToken);
                await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(false, false, consumerDispatchConcurrency: concurrency), stoppingToken);
                await Messaging.DeclareAsync(channel, stoppingToken);
                await channel.BasicQosAsync(0, prefetch, false, stoppingToken);
                var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                channel.ChannelShutdownAsync += (_, _) => { disconnected.TrySetResult(); return Task.CompletedTask; };
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.UnregisteredAsync += (_, _) => { disconnected.TrySetResult(); return Task.CompletedTask; };
                consumer.ReceivedAsync += async (_, delivery) =>
                {
                    try
                    {
                        // Deserialize while the RabbitMQ-owned memory is still valid.
                        var pointer = JsonSerializer.Deserialize<EventPointer>(delivery.Body.Span, Messaging.Json);
                        if (pointer is null || pointer.ProjectId == Guid.Empty || pointer.EventId == Guid.Empty)
                            throw new InvalidDataException("Geçersiz olay işaretçisi.");
                        var receivedAt = await store.ProcessAsync(pointer, stoppingToken);
                        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                        if (receivedAt is { } accepted)
                        {
                            IntegraOpsMetrics.EventsProcessed.Add(1, new KeyValuePair<string, object?>("outcome", "processed"));
                            IntegraOpsMetrics.EventProcessingDuration.Record(Math.Max(0, (DateTimeOffset.UtcNow - accepted).TotalSeconds));
                        }
                        else IntegraOpsMetrics.EventsProcessed.Add(1, new KeyValuePair<string, object?>("outcome", "redelivery"));
                    }
                    catch (Exception ex) when (ex is JsonException or InvalidDataException)
                    {
                        logger.LogError("Geçersiz kuyruk kaydı inceleme kuyruğuna ayrıldı ({ErrorType}).", ex.GetType().Name);
                        IntegraOpsMetrics.EventsProcessed.Add(1, new KeyValuePair<string, object?>("outcome", "rejected"));
                        await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
                    catch (Exception ex)
                    {
                        IntegraOpsMetrics.EventsProcessed.Add(1, new KeyValuePair<string, object?>("outcome", "error"));
                        logger.LogWarning("Olay işlenemedi ({ErrorType}); bağlantı yenilenip tekrar denenecek.", ex.GetType().Name);
                        // Leave the delivery unacknowledged; closing this channel safely requeues it.
                        disconnected.TrySetResult();
                    }
                };
                await channel.BasicConsumeAsync(Messaging.Queue, autoAck: false, consumer, stoppingToken);
                logger.LogInformation("Olay işleyicisi hazır.");
                await disconnected.Task.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("Olay işleyicisi bağlantı bekliyor ({ErrorType}).", ex.GetType().Name);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
