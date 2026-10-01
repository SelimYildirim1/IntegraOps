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
            WHERE k.key_hash = @hash AND k.revoked_at IS NULL AND (k.expires_at IS NULL OR k.expires_at > clock_timestamp())
            """);
        command.Parameters.AddWithValue("hash", Hash(rawKey));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new ProjectAccess(reader.GetGuid(0), reader.GetGuid(1)) : null;
    }

    public async Task<FlowDefinition> CreateFlowAsync(Guid projectId, CreateFlowRequest request, CancellationToken ct)
    {
        InputValidation.Flow(request);
        var flowId = Guid.CreateVersion7();
        var steps = request.Steps.Select((s, i) => new FlowStep(s.Code, s.Name.Trim(), i, s.MaxDelaySeconds,
            Instructions: (s.Instructions ?? "").Trim(), LinkTemplate: (s.Link ?? "").Trim())).ToArray();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        try
        {
            var versionId = Guid.CreateVersion7();
            await using (var insert = Sql.Command(connection, transaction, """
                INSERT INTO flows (id, project_id, code, name) VALUES (@id, @projectId, @code, @name);
                INSERT INTO flow_versions (id, flow_id, version, state, effective_from, published_at)
                VALUES (@versionId, @id, 1, 'published', clock_timestamp(), clock_timestamp())
                """, ("id", flowId), ("projectId", projectId), ("code", request.Code), ("name", request.Name.Trim()), ("versionId", versionId)))
                await insert.ExecuteNonQueryAsync(ct);
            await FlowVersions.InsertStepsAsync(connection, transaction, versionId, steps, ct);
            await transaction.CommitAsync(ct);
            return new FlowDefinition(flowId, projectId, request.Code, request.Name.Trim(), steps, versionId, 1);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ResourceConflictException("Bu projede aynı kodla bir akış zaten var.");
        }
    }

    public async Task<EventReceipt> AcceptAsync(Guid projectId, ValidatedEvent request, CancellationToken ct)
    {
        var fingerprint = Hash(JsonSerializer.Serialize(request));
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        // New transactions bind to the version effective now; existing ones keep theirs.
        var effective = await LoadFlowAsync(connection, transaction, projectId, request.Flow, null, ct)
            ?? throw new ResourceNotFoundException("Akış bu projede bulunamadı.");

        // The uniqueness constraint serializes simultaneous creation of the same business transaction.
        await using (var insert = Sql.Command(connection, transaction, """
            INSERT INTO transactions (id, project_id, flow_id, business_id, is_test, flow_version_id, group_code)
            VALUES (@id, @projectId, @flowId, @businessId, @businessId LIKE 'TEST-%', @versionId, @group)
            ON CONFLICT (project_id, flow_id, business_id) DO NOTHING
            """, ("id", Guid.CreateVersion7()), ("projectId", projectId),
            ("flowId", effective.Id), ("businessId", request.TransactionId), ("versionId", effective.VersionId), ("group", request.Group)))
            await insert.ExecuteNonQueryAsync(ct);

        Guid transactionId; Guid versionId; string? group;
        await using (var find = Sql.Command(connection, transaction, """
            SELECT id, flow_version_id, group_code FROM transactions
            WHERE project_id = @projectId AND flow_id = @flowId AND business_id = @businessId FOR UPDATE
            """, ("projectId", projectId), ("flowId", effective.Id), ("businessId", request.TransactionId)))
        await using (var reader = await find.ExecuteReaderAsync(ct))
        {
            await reader.ReadAsync(ct);
            transactionId = reader.GetGuid(0); versionId = reader.GetGuid(1); group = reader.IsDBNull(2) ? null : reader.GetString(2);
        }
        var flow = versionId == effective.VersionId ? effective
            : await LoadFlowAsync(connection, transaction, projectId, null, effective.Id, ct, versionId)
                ?? throw new InvalidDataException("İşlemin akış sürümü bulunamadı.");
        if (!flow.Steps.Any(s => s.Code == request.Step))
            throw new RequestValidationException(new() { ["step"] = [flow.Version == effective.Version
                ? "Bu adım akışta tanımlı değil."
                : $"Bu adım işlemin başladığı {flow.Version}. sürümde tanımlı değil."] });
        // The first group reported for a transaction sticks; later events cannot move it.
        if (group is null && request.Group is not null)
            await using (var setGroup = Sql.Command(connection, transaction,
                "UPDATE transactions SET group_code = @group WHERE id = @id AND group_code IS NULL", ("group", request.Group), ("id", transactionId)))
                await setGroup.ExecuteNonQueryAsync(ct);

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

    public async Task<TransactionView?> GetTransactionAsync(Guid projectId, string flowCode, string businessId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        // Keep the accepted count, processed events and flow definition in one consistent snapshot.
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await using var find = Sql.Command(connection, transaction, """
            SELECT t.id, t.flow_version_id, t.group_code FROM transactions t JOIN flows f ON f.id = t.flow_id
            WHERE t.project_id = @projectId AND f.code = @flow AND t.business_id = @businessId
            """, ("projectId", projectId), ("flow", flowCode), ("businessId", businessId));
        Guid id; Guid versionId; string? group;
        await using (var reader = await find.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return null;
            id = reader.GetGuid(0); versionId = reader.GetGuid(1); group = reader.IsDBNull(2) ? null : reader.GetString(2);
        }
        var flow = await LoadFlowAsync(connection, transaction, projectId, null, null, ct, versionId)
            ?? throw new InvalidDataException("İşlemin akış sürümü bulunamadı.");
        var events = await LoadEventsAsync(connection, transaction, projectId, id, ct);
        await using var count = Sql.Command(connection, transaction,
            "SELECT count(*) FROM transaction_events WHERE project_id = @projectId AND transaction_id = @id",
            ("projectId", projectId), ("id", id));
        var acceptedCount = checked((int)(long)(await count.ExecuteScalarAsync(ct))!);
        var projection = TimelineProjector.Project(flow, events, group, businessId);
        var delay = await PostgresDelayStore.ReadAsync(connection, transaction, projectId, id,
            projection, acceptedCount > events.Count, ct);
        await transaction.CommitAsync(ct);
        return new TransactionView(id, flow.Code, businessId, projection.Status,
            acceptedCount, events.Count, projection.Steps, events, delay, flow.Version, group, flow.Calendar?.Name);
    }

    /// <returns>The API acceptance time when this call processed the event; null for a harmless redelivery.</returns>
    public async Task<DateTimeOffset?> ProcessAsync(EventPointer pointer, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        Guid transactionId; Guid versionId; string? group;
        await using (var find = Sql.Command(connection, transaction, """
            SELECT t.id, t.flow_version_id, t.group_code FROM transactions t
            JOIN transaction_events e ON e.transaction_id = t.id AND e.project_id = t.project_id
            WHERE e.project_id = @projectId AND e.event_id = @eventId FOR UPDATE OF t
            """, ("projectId", pointer.ProjectId), ("eventId", pointer.EventId)))
        await using (var reader = await find.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw new InvalidDataException("Kuyrukta karşılığı olmayan olay kimliği var.");
            transactionId = reader.GetGuid(0);
            versionId = reader.GetGuid(1);
            group = reader.IsDBNull(2) ? null : reader.GetString(2);
        }

        DateTimeOffset receivedAt;
        await using (var apply = Sql.Command(connection, transaction, """
            UPDATE transaction_events SET processed_at = clock_timestamp()
            WHERE project_id = @projectId AND event_id = @eventId AND processed_at IS NULL
            RETURNING received_at
            """, ("projectId", pointer.ProjectId), ("eventId", pointer.EventId)))
        {
            if (await apply.ExecuteScalarAsync(ct) is not DateTime received)
            {
                await transaction.CommitAsync(ct);
                return null; // Publisher/consumer redelivery is harmless.
            }
            receivedAt = new DateTimeOffset(DateTime.SpecifyKind(received, DateTimeKind.Utc));
        }
        var flow = await LoadFlowAsync(connection, transaction, pointer.ProjectId, null, null, ct, versionId)
            ?? throw new InvalidDataException("Akış sürümü bulunamadı.");
        var events = await LoadEventsAsync(connection, transaction, pointer.ProjectId, transactionId, ct);
        var projection = TimelineProjector.Project(flow, events, group);
        await using (var update = Sql.Command(connection, transaction, """
            UPDATE transactions SET status = @status, updated_at = clock_timestamp()
            WHERE id = @id AND project_id = @projectId
            """, ("status", projection.Status.ToString()), ("id", transactionId), ("projectId", pointer.ProjectId)))
            await update.ExecuteNonQueryAsync(ct);
        await PostgresDelayStore.RefreshAsync(connection, transaction, flow, transactionId, projection, events, ct);
        await FlowVersions.UpsertStepStatsAsync(connection, transaction, pointer.ProjectId, transactionId, flow, projection, ct);
        await transaction.CommitAsync(ct);
        return receivedAt;
    }

    /// <summary>Loads a flow with one version: the given one, or the version effective now.</summary>
    internal static Task<FlowDefinition?> LoadFlowAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        Guid projectId, string? code, Guid? flowId, CancellationToken ct, Guid? versionId = null) =>
        FlowVersions.LoadAsync(connection, transaction, projectId, code, flowId, versionId, ct);

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
