using System.Text.Json;
using Npgsql;
using RabbitMQ.Client;

namespace IntegraOps.Infrastructure;

public sealed record EventPointer(Guid ProjectId, Guid EventId);

public static class Messaging
{
    public const string Queue = "integraops.events.v1";
    public const string DeadLetterQueue = "integraops.events.dead.v1";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task DeclareAsync(IChannel channel, CancellationToken ct)
    {
        await channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: null, cancellationToken: ct);
        await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = "",
                ["x-dead-letter-routing-key"] = DeadLetterQueue
            }, cancellationToken: ct);
    }
}

public sealed class OutboxDispatcher(NpgsqlDataSource dataSource)
{
    public Task<bool> PublishNextAsync(IChannel channel, CancellationToken ct) => PublishBatchAsync(channel, 1, ct).ContinueWith(t => t.Result > 0, ct);

    /// <summary>
    /// Publishes up to <paramref name="batchSize"/> pending rows in one database transaction. Publishes are
    /// pipelined and confirmed together; if any confirmation fails the rows stay pending and are retried,
    /// which the consumer tolerates because processing is idempotent per event id.
    /// </summary>
    public async Task<int> PublishBatchAsync(IChannel channel, int batchSize, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var ids = new List<long>(); var pointers = new List<EventPointer>();
        await using (var select = Sql.Command(connection, transaction, """
            SELECT id, project_id, event_id FROM outbox_messages
            WHERE published_at IS NULL ORDER BY id LIMIT @n FOR UPDATE SKIP LOCKED
            """, ("n", Math.Clamp(batchSize, 1, 500))))
        await using (var reader = await select.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                ids.Add(reader.GetInt64(0));
                pointers.Add(new EventPointer(reader.GetGuid(1), reader.GetGuid(2)));
            }
        }
        if (ids.Count == 0) return 0;

        // This channel must have publisher confirmations + confirmation tracking enabled.
        // Each await fails if the broker rejects or returns the mandatory message.
        var publishes = new List<ValueTask>(pointers.Count);
        foreach (var pointer in pointers)
        {
            var properties = new BasicProperties { Persistent = true, ContentType = "application/json", MessageId = $"{pointer.ProjectId}:{pointer.EventId}" };
            publishes.Add(channel.BasicPublishAsync(exchange: "", routingKey: Messaging.Queue, mandatory: true,
                basicProperties: properties, body: JsonSerializer.SerializeToUtf8Bytes(pointer, Messaging.Json), cancellationToken: ct));
        }
        foreach (var publish in publishes) await publish;

        await using var mark = Sql.Command(connection, transaction,
            "UPDATE outbox_messages SET published_at = clock_timestamp() WHERE id = ANY(@ids)", ("ids", ids.ToArray()));
        await mark.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return ids.Count;
    }
}
