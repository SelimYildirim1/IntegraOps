using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntegraOps.Application;
using IntegraOps.Domain;
using Npgsql;

namespace IntegraOps.Infrastructure;

public sealed class PostgresTrackingStore(NpgsqlDataSource dataSource) : ITrackingStore
{
    public async Task<ProjectCredentials> CreateProjectAsync(CreateProjectRequest request, CancellationToken ct)
    {
        InputValidation.Project(request);
        var organizationId = Guid.CreateVersion7();
        var projectId = Guid.CreateVersion7();
        var rawKey = "io_dev_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = Sql.Command(connection, transaction, """
            INSERT INTO organizations (id, name) VALUES (@organizationId, @organizationName);
            INSERT INTO projects (id, organization_id, name) VALUES (@projectId, @organizationId, @projectName);
            INSERT INTO api_keys (id, project_id, key_hash, key_prefix) VALUES (@keyId, @projectId, @hash, @prefix);
            """, ("organizationId", organizationId), ("organizationName", request.OrganizationName.Trim()),
            ("projectId", projectId), ("projectName", request.ProjectName.Trim()),
            ("keyId", Guid.CreateVersion7()), ("hash", Hash(rawKey)), ("prefix", rawKey[..16]));
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return new ProjectCredentials(organizationId, projectId, rawKey);
    }

    public async Task<ProjectAccess?> AuthenticateAsync(string rawKey, CancellationToken ct)
    {
        if (rawKey.Length is < 32 or > 128) return null;
        await using var command = dataSource.CreateCommand("""
            SELECT p.organization_id, p.id FROM api_keys k JOIN projects p ON p.id = k.project_id
            WHERE k.key_hash = @hash AND k.revoked_at IS NULL
            """);
        command.Parameters.AddWithValue("hash", Hash(rawKey));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new ProjectAccess(reader.GetGuid(0), reader.GetGuid(1)) : null;
    }

    public async Task<FlowDefinition> CreateFlowAsync(Guid projectId, CreateFlowRequest request, CancellationToken ct)
    {
        InputValidation.Flow(request);
        var flowId = Guid.CreateVersion7();
        var steps = request.Steps.Select((s, i) => new FlowStep(s.Code, s.Name.Trim(), i, s.MaxDelaySeconds)).ToArray();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        try
        {
            await using (var insert = Sql.Command(connection, transaction, """
                INSERT INTO flows (id, project_id, code, name) VALUES (@id, @projectId, @code, @name)
                """, ("id", flowId), ("projectId", projectId), ("code", request.Code), ("name", request.Name.Trim())))
                await insert.ExecuteNonQueryAsync(ct);
            foreach (var step in steps)
            {
                await using var insert = Sql.Command(connection, transaction, """
                    INSERT INTO flow_steps (flow_id, code, name, position, max_delay_seconds)
                    VALUES (@flowId, @code, @name, @position, CAST(@delay AS integer))
                    """, ("flowId", flowId), ("code", step.Code), ("name", step.Name),
                    ("position", step.Position), ("delay", step.MaxDelaySeconds));
                await insert.ExecuteNonQueryAsync(ct);
            }
            await transaction.CommitAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ResourceConflictException("Bu projede aynı kodla bir akış zaten var.");
        }
        return new FlowDefinition(flowId, projectId, request.Code, request.Name.Trim(), steps);
    }

    public async Task<EventReceipt> AcceptAsync(Guid projectId, ValidatedEvent request, CancellationToken ct)
    {
        var fingerprint = Hash(JsonSerializer.Serialize(request));
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var flow = await LoadFlowAsync(connection, transaction, projectId, request.Flow, null, ct)
            ?? throw new ResourceNotFoundException("Akış bu projede bulunamadı.");
        if (!flow.Steps.Any(s => s.Code == request.Step))
            throw new RequestValidationException(new() { ["step"] = ["Bu adım akışta tanımlı değil."] });

        // The uniqueness constraint serializes simultaneous creation of the same business transaction.
        await using (var insert = Sql.Command(connection, transaction, """
            INSERT INTO transactions (id, project_id, flow_id, business_id)
            VALUES (@id, @projectId, @flowId, @businessId)
            ON CONFLICT (project_id, flow_id, business_id) DO NOTHING
            """, ("id", Guid.CreateVersion7()), ("projectId", projectId),
            ("flowId", flow.Id), ("businessId", request.TransactionId)))
            await insert.ExecuteNonQueryAsync(ct);

        Guid transactionId;
        await using (var find = Sql.Command(connection, transaction, """
            SELECT id FROM transactions WHERE project_id = @projectId AND flow_id = @flowId AND business_id = @businessId
            """, ("projectId", projectId), ("flowId", flow.Id), ("businessId", request.TransactionId)))
            transactionId = (Guid)(await find.ExecuteScalarAsync(ct))!;

        object? acceptedAt;
        await using (var insert = Sql.Command(connection, transaction, """
            INSERT INTO transaction_events
                (project_id, event_id, transaction_id, flow_id, step_code, status, occurred_at, error, fingerprint)
            VALUES (@projectId, @eventId, @transactionId, @flowId, @step, @status, @occurredAt, @error, @fingerprint)
            ON CONFLICT (project_id, event_id) DO NOTHING RETURNING received_at
            """, ("projectId", projectId), ("eventId", request.EventId), ("transactionId", transactionId),
            ("flowId", flow.Id), ("step", request.Step), ("status", request.Status.ToString()),
            ("occurredAt", request.OccurredAt), ("error", request.Error), ("fingerprint", fingerprint)))
            acceptedAt = await insert.ExecuteScalarAsync(ct);

        if (acceptedAt is null)
        {
            await using var find = Sql.Command(connection, transaction, """
                SELECT fingerprint, transaction_id, received_at FROM transaction_events
                WHERE project_id = @projectId AND event_id = @eventId
                """, ("projectId", projectId), ("eventId", request.EventId));
            EventReceipt receipt;
            await using (var reader = await find.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Tekrarlanan olay kaydı bulunamadı.");
                if (reader.GetString(0) != fingerprint)
                    throw new ResourceConflictException("Aynı eventId daha önce farklı içerikle kullanılmış.");
                receipt = new EventReceipt(request.EventId, reader.GetGuid(1), true,
                    new DateTimeOffset(reader.GetDateTime(2)));
            }
            await transaction.CommitAsync(ct);
            return receipt;
        }

        // Event and outbox are committed together. No RabbitMQ connection is needed on this path.
        await using (var outbox = Sql.Command(connection, transaction,
            "INSERT INTO outbox_messages (project_id, event_id) VALUES (@projectId, @eventId)",
            ("projectId", projectId), ("eventId", request.EventId)))
            await outbox.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return new EventReceipt(request.EventId, transactionId, false, new DateTimeOffset((DateTime)acceptedAt));
    }

    public async Task<TransactionView?> GetTransactionAsync(
    Guid projectId,
    string flowCode,
    string businessId,
    CancellationToken ct)
{
    await using var connection =
        await dataSource.OpenConnectionAsync(ct);

    await using var transaction =
        await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, ct);

    var flow = await LoadFlowAsync(
        connection,
        transaction,
        projectId,
        flowCode,
        null,
        ct);

    if (flow is null)
        return null;

    await using var find = Sql.Command(
        connection,
        transaction,
        """
        SELECT id
        FROM transactions
        WHERE project_id = @projectId
          AND flow_id = @flowId
          AND business_id = @businessId
        """,
        ("projectId", projectId),
        ("flowId", flow.Id),
        ("businessId", businessId));

    var foundId = await find.ExecuteScalarAsync(ct);

    if (foundId is not Guid id)
        return null;

    var events = await LoadEventsAsync(
        connection,
        transaction,
        projectId,
        id,
        ct);

    await using var count = Sql.Command(
        connection,
        transaction,
        """
        SELECT count(*)
        FROM transaction_events
        WHERE project_id = @projectId
          AND transaction_id = @id
        """,
        ("projectId", projectId),
        ("id", id));

    var acceptedCount =
        checked((int)(long)(await count.ExecuteScalarAsync(ct))!);

    var projection = TimelineProjector.Project(flow, events);

    var delay = await PostgresDelayStore.ReadAsync(
        connection,
        transaction,
        projectId,
        id,
        projection,
        acceptedCount > events.Count,
        ct);

    await transaction.CommitAsync(ct);

    return new TransactionView(
        id,
        flow.Code,
        businessId,
        projection.Status,
        acceptedCount,
        events.Count,
        projection.Steps,
        events,
        delay
    );
}

    public async Task ProcessAsync(EventPointer pointer, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        Guid transactionId;
        Guid flowId;
        await using (var find = Sql.Command(connection, transaction, """
            SELECT t.id, t.flow_id FROM transactions t
            JOIN transaction_events e ON e.transaction_id = t.id AND e.project_id = t.project_id
            WHERE e.project_id = @projectId AND e.event_id = @eventId FOR UPDATE OF t
            """, ("projectId", pointer.ProjectId), ("eventId", pointer.EventId)))
        await using (var reader = await find.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw new InvalidDataException("Kuyrukta karşılığı olmayan olay kimliği var.");
            transactionId = reader.GetGuid(0);
            flowId = reader.GetGuid(1);
        }

        await using (var apply = Sql.Command(connection, transaction, """
            UPDATE transaction_events SET processed_at = clock_timestamp()
            WHERE project_id = @projectId AND event_id = @eventId AND processed_at IS NULL
            """, ("projectId", pointer.ProjectId), ("eventId", pointer.EventId)))
        {
            if (await apply.ExecuteNonQueryAsync(ct) == 0)
            {
                await transaction.CommitAsync(ct);
                return; // Publisher/consumer redelivery is harmless.
            }
        }
        var flow = await LoadFlowAsync(connection, transaction, pointer.ProjectId, null, flowId, ct)
            ?? throw new InvalidDataException("Akış kaydı bulunamadı.");
        var events = await LoadEventsAsync(connection, transaction, pointer.ProjectId, transactionId, ct);
        var projection = TimelineProjector.Project(flow, events);
        await using (var update = Sql.Command(connection, transaction, """
            UPDATE transactions SET status = @status, updated_at = clock_timestamp()
            WHERE id = @id AND project_id = @projectId
            """, ("status", projection.Status.ToString()), ("id", transactionId), ("projectId", pointer.ProjectId)))
            await update.ExecuteNonQueryAsync(ct);

        await PostgresDelayStore.RefreshAsync(
            connection,
            transaction,
            flow,
            transactionId,
            projection,
            ct);

        await transaction.CommitAsync(ct);
    }

    internal static async Task<FlowDefinition?> LoadFlowAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid projectId, string? code, Guid? flowId, CancellationToken ct)
    {
        await using var find = Sql.Command(connection, transaction, """
            SELECT id, code, name FROM flows WHERE project_id = @projectId
            AND (code = @code OR id = CAST(@flowId AS uuid))
            """, ("projectId", projectId), ("code", code), ("flowId", flowId));
        Guid id;
        string name;
        string foundCode;
        await using (var reader = await find.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return null;
            id = reader.GetGuid(0); foundCode = reader.GetString(1); name = reader.GetString(2);
        }
        await using var stepsCommand = Sql.Command(connection, transaction, """
            SELECT code, name, position, max_delay_seconds FROM flow_steps WHERE flow_id = @id ORDER BY position
            """, ("id", id));
        var steps = new List<FlowStep>();
        await using (var reader = await stepsCommand.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                steps.Add(new FlowStep(reader.GetString(0), reader.GetString(1), reader.GetInt16(2),
                    reader.IsDBNull(3) ? null : reader.GetInt32(3)));
        }
        return new FlowDefinition(id, projectId, foundCode, name, steps);
    }

    internal static async Task<List<ObservedEvent>> LoadEventsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid projectId, Guid transactionId, CancellationToken ct)
    {
        await using var find = Sql.Command(connection, transaction, """
            SELECT event_id, step_code, status, occurred_at, received_at, error FROM transaction_events
            WHERE project_id = @projectId AND transaction_id = @id AND processed_at IS NOT NULL
            ORDER BY occurred_at, event_id
            """, ("projectId", projectId), ("id", transactionId));
        var events = new List<ObservedEvent>();
        await using var reader = await find.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            events.Add(new ObservedEvent(reader.GetGuid(0), reader.GetString(1), Enum.Parse<EventStatus>(reader.GetString(2)),
                new DateTimeOffset(reader.GetDateTime(3)), new DateTimeOffset(reader.GetDateTime(4)),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        return events;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
