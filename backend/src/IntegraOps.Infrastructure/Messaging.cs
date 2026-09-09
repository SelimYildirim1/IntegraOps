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
    public async Task<bool> PublishNextAsync(IChannel channel, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        long id;
        EventPointer pointer;
        await using (var select = Sql.Command(connection, transaction, """
            SELECT id, project_id, event_id FROM outbox_messages
            WHERE published_at IS NULL ORDER BY id LIMIT 1 FOR UPDATE SKIP LOCKED
            """))
        await using (var reader = await select.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return false;
            id = reader.GetInt64(0);
            pointer = new EventPointer(reader.GetGuid(1), reader.GetGuid(2));
        }

        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = $"{pointer.ProjectId}:{pointer.EventId}"
        };
        // This channel must have publisher confirmations + confirmation tracking enabled.
        // The await fails if the broker rejects or returns the mandatory message.
        await channel.BasicPublishAsync(exchange: "", routingKey: Messaging.Queue, mandatory: true,
            basicProperties: properties, body: JsonSerializer.SerializeToUtf8Bytes(pointer, Messaging.Json),
            cancellationToken: ct);

        await using var mark = Sql.Command(connection, transaction,
            "UPDATE outbox_messages SET published_at = clock_timestamp() WHERE id = @id", ("id", id));
        await mark.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }
}
