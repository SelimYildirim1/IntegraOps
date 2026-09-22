using IntegraOps.Application;
using Npgsql;

namespace IntegraOps.Infrastructure;

public sealed class PostgresPanelQueries(NpgsqlDataSource dataSource) : IPanelQueries
{
    public async Task<IReadOnlyList<FlowListItem>> ListFlowsAsync(
        Guid projectId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = Sql.Command(connection, null, """
            SELECT f.id, f.code, f.name, count(s.code)::integer AS step_count
            FROM flows f
            LEFT JOIN flow_steps s ON s.flow_id = f.id
            WHERE f.project_id = @projectId
            GROUP BY f.id, f.code, f.name
            ORDER BY f.name, f.code
            """, ("projectId", projectId));

        var items = new List<FlowListItem>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            items.Add(new FlowListItem(
                reader.GetGuid(0), reader.GetString(1),
                reader.GetString(2), reader.GetInt32(3)));
        }
        return items;
    }

    public async Task<TransactionSummary> GetSummaryAsync(
        Guid projectId, string? flow, string? test, CancellationToken ct)
    {
        var query = InputValidation.TransactionList(flow, null, null, test: test);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        // Reuse the list's project scope and effective delay classification.
        // One aggregate query counts every matching transaction, with no pagination.
        await using var command = Sql.Command(connection, null,
            PostgresTransactionQueries.FilteredSql + """

            SELECT
                count(*) AS total,
                count(*) FILTER (WHERE status = 'Pending') AS pending,
                count(*) FILTER (WHERE status = 'InProgress') AS in_progress,
                count(*) FILTER (WHERE status = 'Completed') AS completed,
                count(*) FILTER (WHERE status = 'Failed') AS failed,
                count(*) FILTER (WHERE delay_state = 'overdue') AS overdue,
                statement_timestamp() AS queried_at
            FROM filtered
            """,
            ("projectId", projectId), ("flow", query.Flow),
            ("status", null), ("delay", null), ("search", null), ("from", null), ("to", null), ("test", query.Test));

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException("İşlem özeti okunamadı.");

        return new TransactionSummary(
            reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2),
            reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5),
            new DateTimeOffset(reader.GetDateTime(6)));
    }
}
