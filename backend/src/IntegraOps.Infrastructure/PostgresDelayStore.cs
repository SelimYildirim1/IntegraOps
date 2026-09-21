using IntegraOps.Application;
using IntegraOps.Domain;
using Npgsql;

namespace IntegraOps.Infrastructure;

public sealed class PostgresDelayStore(NpgsqlDataSource dataSource)
{
    public async Task<int> CheckDueAsync(int batchSize, CancellationToken ct)
    {
        if (batchSize is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(batchSize));
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var candidates = new List<(Guid Id, Guid ProjectId, Guid FlowId)>();
        // The consumer locks the same transaction row. SKIP LOCKED also allows
        // multiple workers without having them process the same candidate together.
        // Missing rows backfill in-progress transactions created before this module.
        await using (var find = Sql.Command(connection, transaction, """
            SELECT t.id, t.project_id, t.flow_id
            FROM transactions t LEFT JOIN transaction_delays d ON d.transaction_id = t.id
            WHERE (t.alarm_checked_at IS NULL OR (t.status = 'InProgress'
              AND (d.transaction_id IS NULL OR d.state = 'pending_processing' OR t.updated_at > d.checked_at
                   OR (d.state = 'waiting' AND d.deadline_at <= clock_timestamp()))))
              AND NOT EXISTS (
                  SELECT 1 FROM transaction_events e
                  WHERE e.transaction_id = t.id AND e.processed_at IS NULL)
            ORDER BY COALESCE(d.deadline_at, t.created_at), t.id
            LIMIT @batchSize FOR UPDATE OF t SKIP LOCKED
            """, ("batchSize", batchSize)))
        await using (var reader = await find.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                candidates.Add((reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2)));
        }

        foreach (var candidate in candidates)
        {
            var flow = await PostgresTrackingStore.LoadFlowAsync(connection, transaction,
                candidate.ProjectId, null, candidate.FlowId, ct)
                ?? throw new InvalidDataException("Gecikme kontrolü için akış bulunamadı.");
            var events = await PostgresTrackingStore.LoadEventsAsync(connection, transaction,
                candidate.ProjectId, candidate.Id, ct);
            await RefreshAsync(connection, transaction, flow, candidate.Id,
                TimelineProjector.Project(flow, events), ct);
        }
        await transaction.CommitAsync(ct);
        return candidates.Count;
    }

    // Called inside the consumer's transaction as well as by the periodic scan.
    internal static async Task RefreshAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        FlowDefinition flow, Guid transactionId, Projection projection, CancellationToken ct)
    {
        await using var pending = Sql.Command(connection, transaction, """
            SELECT EXISTS (SELECT 1 FROM transaction_events
                WHERE project_id = @projectId AND transaction_id = @id AND processed_at IS NULL)
            """, ("projectId", flow.ProjectId), ("id", transactionId));
        var hasPendingEvents = (bool)(await pending.ExecuteScalarAsync(ct))!;
        // Use the database clock consistently, even when API and worker run on different hosts.
        await using var clock = Sql.Command(connection, transaction, "SELECT clock_timestamp()");
        var now = new DateTimeOffset((DateTime)(await clock.ExecuteScalarAsync(ct))!);
        var assessment = hasPendingEvents
            ? new DelayAssessment("pending_processing", null, null, null)
            : DelayEvaluator.Evaluate(projection, now);

        await using var update = Sql.Command(connection, transaction, """
            INSERT INTO transaction_delays
                (transaction_id, project_id, flow_id, state, step_code, started_at, deadline_at, checked_at)
            VALUES (@id, @projectId, @flowId, @state, @step,
                CAST(@startedAt AS timestamptz), CAST(@deadlineAt AS timestamptz), @checkedAt)
            ON CONFLICT (transaction_id) DO UPDATE SET
                state = EXCLUDED.state, step_code = EXCLUDED.step_code,
                started_at = EXCLUDED.started_at, deadline_at = EXCLUDED.deadline_at,
                checked_at = EXCLUDED.checked_at
            """, ("id", transactionId), ("projectId", flow.ProjectId), ("flowId", flow.Id),
            ("state", assessment.State), ("step", assessment.Step), ("startedAt", assessment.StartedAt),
            ("deadlineAt", assessment.DeadlineAt), ("checkedAt", now));
        await update.ExecuteNonQueryAsync(ct);
        await PostgresAlarmStore.Sync(connection, transaction, flow, transactionId, projection, assessment, ct);
    }

    internal static async Task<DelayView> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid projectId, Guid transactionId, Projection projection, bool hasPendingEvents, CancellationToken ct)
    {
        // An accepted notification may already contain the missing completion.
        // Until the consumer catches up, do not present its backlog as a business delay.
        if (hasPendingEvents) return new DelayView("pending_processing", null, null, null, null);

        await using var find = Sql.Command(connection, transaction, """
            SELECT d.state, d.step_code, d.started_at, d.deadline_at, d.checked_at, t.updated_at
            FROM transaction_delays d JOIN transactions t ON t.id = d.transaction_id
            WHERE d.project_id = @projectId AND d.transaction_id = @id
            """, ("projectId", projectId), ("id", transactionId));
        await using var reader = await find.ExecuteReaderAsync(ct);
        // A briefly running older worker (during an upgrade or rollback) may have
        // changed the transaction without refreshing the new delay table.
        if (!await reader.ReadAsync(ct) || reader.GetDateTime(5) > reader.GetDateTime(4))
            return new DelayView(projection.Status == TransactionStatus.InProgress ? "pending_evaluation" : "none",
                null, null, null, null);
        return new DelayView(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : new DateTimeOffset(reader.GetDateTime(2)),
            reader.IsDBNull(3) ? null : new DateTimeOffset(reader.GetDateTime(3)),
            new DateTimeOffset(reader.GetDateTime(4)));
    }
}
