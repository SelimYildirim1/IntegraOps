using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace OrderSource;

public sealed record SourceEvent(Guid EventId, string Flow, string TransactionId, string Step,
    string Status, string OccurredAt, string? Error = null);

public static class SourceDb
{
    public static NpgsqlCommand Command(NpgsqlConnection c, NpgsqlTransaction? tx, string sql, params (string, object?)[] args)
    {
        var command = new NpgsqlCommand(sql, c, tx);
        foreach (var (name, value) in args)
        {
            var p = new NpgsqlParameter { ParameterName = name, Value = value ?? DBNull.Value };
            if (value is null) p.NpgsqlDbType = NpgsqlDbType.Unknown;
            command.Parameters.Add(p);
        }
        return command;
    }
    public static async Task Enqueue(NpgsqlConnection c, NpgsqlTransaction tx, SourceEvent message, CancellationToken ct)
    {
        // Use the same connection and transaction as the business write.
        // Never call IntegraOps over HTTP inside that transaction.
        var body = JsonSerializer.Serialize(message, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await using var cmd = Command(c, tx, "INSERT INTO source_outbox(id,body) VALUES(@id,@body)", ("id", message.EventId), ("body", body));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

public sealed class SourceSender(NpgsqlDataSource source, IConfiguration config, ILogger<SourceSender> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await SendNext(client, stoppingToken)) await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("Kaynak gönderim kuyruğu bekliyor. ErrorType: {Type}", ex.GetType().Name);
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task<bool> SendNext(HttpClient client, CancellationToken ct)
    {
        Guid id; string body; int attempt; var lease = Guid.NewGuid();
        await using (var c = await source.OpenConnectionAsync(ct))
        await using (var tx = await c.BeginTransactionAsync(ct))
        {
            await using (var cmd = SourceDb.Command(c, tx, """
                SELECT id,body,attempts FROM source_outbox
                WHERE (state='pending' AND available_at<=clock_timestamp()) OR (state='sending' AND lease_until<clock_timestamp())
                ORDER BY created_at,id LIMIT 1 FOR UPDATE SKIP LOCKED
                """))
            await using (var r = await cmd.ExecuteReaderAsync(ct))
            {
                if (!await r.ReadAsync(ct)) return false;
                id = r.GetGuid(0); body = r.GetString(1); attempt = r.GetInt32(2) + 1;
            }
            await using var claim = SourceDb.Command(c, tx, """
                UPDATE source_outbox SET state='sending',attempts=attempts+1,lease_token=@l,lease_until=clock_timestamp()+interval '60 seconds'
                WHERE id=@id
                """, ("id", id), ("l", lease));
            await claim.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct);
        }
        var state = "pending"; string? error = null;
        try
        {
            var baseUrl = config["IntegraOps:Url"] ?? throw new InvalidOperationException("API adresi gerekli.");
            var key = config["IntegraOps:Key"] ?? throw new InvalidOperationException("Gönderim anahtarı gerekli.");
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl), "/api/v1/events"))
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            request.Headers.Add("X-API-Key", key);
            using var response = await client.SendAsync(request, ct);
            var code = (int)response.StatusCode;
            if (code is 200 or 202)
            {
                using var receipt = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (receipt.RootElement.GetProperty("eventId").GetGuid() != id) throw new InvalidDataException();
                state = "sent";
            }
            else
            {
                error = "http_" + code;
                if (code is >= 300 and < 500 && code is not (408 or 425 or 429)) state = "dead";
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { error = ex.GetType().Name; }
        await using var finishConnection = await source.OpenConnectionAsync(ct);
        await using var finish = SourceDb.Command(finishConnection, null, """
            UPDATE source_outbox SET state=@s,last_error=@e,lease_token=NULL,lease_until=NULL,
                available_at=clock_timestamp()+@delay WHERE id=@id AND lease_token=@l AND state='sending'
            """, ("s", state), ("e", error), ("delay", TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, Math.Min(attempt, 6))) + Random.Shared.Next(3))),
            ("id", id), ("l", lease));
        await finish.ExecuteNonQueryAsync(ct);
        return true;
    }
}
