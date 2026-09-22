using System.Data;
using IntegraOps.Application;
using IntegraOps.Domain;
using Npgsql;

namespace IntegraOps.Infrastructure;

public sealed class PostgresTransactionQueries(NpgsqlDataSource dataSource)
    : ITransactionQueries
{
    internal const string FilteredSql = """
        WITH candidates AS (
            SELECT t.id, f.code AS flow, t.business_id, t.status,
                   t.created_at, t.updated_at, t.is_test,
                   d.step_code, d.started_at, d.deadline_at, d.checked_at,
                   CASE
                       WHEN EXISTS (
                           SELECT 1 FROM transaction_events e
                           WHERE e.project_id = t.project_id
                             AND e.transaction_id = t.id
                             AND e.processed_at IS NULL
                       ) THEN 'pending_processing'
                       WHEN d.transaction_id IS NULL OR t.updated_at > d.checked_at
                           THEN CASE WHEN t.status = 'InProgress'
                               THEN 'pending_evaluation' ELSE 'none' END
                       ELSE d.state
                   END AS delay_state
            FROM transactions t
            JOIN flows f ON f.id = t.flow_id AND f.project_id = t.project_id
            LEFT JOIN transaction_delays d
                ON d.transaction_id = t.id AND d.project_id = t.project_id
            WHERE t.project_id = @projectId
              AND (CAST(@flow AS text) IS NULL OR f.code = @flow)
              AND (CAST(@status AS text) IS NULL OR t.status = @status)
              AND (CAST(@search AS text) IS NULL OR t.business_id ILIKE '%' || @search || '%')
              AND (CAST(@from AS timestamptz) IS NULL OR t.created_at >= @from)
              AND (CAST(@to AS timestamptz) IS NULL OR t.created_at < @to)
              AND (@test = 'include' OR (@test = 'only' AND t.is_test) OR (@test = 'exclude' AND NOT t.is_test))
        ), filtered AS (
            SELECT * FROM candidates
            WHERE CAST(@delay AS text) IS NULL OR delay_state = @delay
        )
        """;

    public async Task<TransactionPage> ListAsync(
        Guid projectId, TransactionListQuery query, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        // Count and rows must see the same state even if the worker changes data.
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, ct);

        long totalCount;
        await using (var count = Command(connection, transaction, projectId, query,
            FilteredSql + " SELECT count(*) FROM filtered"))
        {
            totalCount = (long)(await count.ExecuteScalarAsync(ct))!;
        }

        var items = await ReadItemsAsync(connection, transaction, projectId, query,
            query.PageSize, ((long)query.Page - 1) * query.PageSize, ct);
        await transaction.CommitAsync(ct);
        return new TransactionPage(query.Page, query.PageSize, totalCount, items);
    }

    /// <summary>Rows for CSV export with the same filters as the list; capped by <paramref name="limit"/>.</summary>
    public async Task<List<TransactionListItem>> ListForExportAsync(
        Guid projectId, TransactionListQuery query, int limit, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        return await ReadItemsAsync(connection, null, projectId, query, limit, 0, ct);
    }

    private static async Task<List<TransactionListItem>> ReadItemsAsync(NpgsqlConnection connection,
        NpgsqlTransaction? transaction, Guid projectId, TransactionListQuery query, int limit, long offset, CancellationToken ct)
    {
        var items = new List<TransactionListItem>();
        await using var find = Command(connection, transaction, projectId, query,
            FilteredSql + """

            SELECT id, flow, business_id, status, created_at, updated_at,
                   delay_state, step_code, started_at, deadline_at, checked_at, is_test
            FROM filtered
            ORDER BY created_at DESC, id DESC
            LIMIT @limit OFFSET @offset
            """);
        find.Parameters.AddWithValue("limit", limit);
        find.Parameters.AddWithValue("offset", offset);
        await using var reader = await find.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var updatedAt = new DateTimeOffset(reader.GetDateTime(5));
            var state = reader.GetString(6);
            var checkedAt = ReadTime(reader, 10);
            var current = state is not ("pending_processing" or "pending_evaluation")
                && checkedAt is not null && updatedAt <= checkedAt.Value;
            var delay = new DelayView(
                state,
                current && !reader.IsDBNull(7) ? reader.GetString(7) : null,
                current ? ReadTime(reader, 8) : null,
                current ? ReadTime(reader, 9) : null,
                current ? checkedAt : null);
            items.Add(new TransactionListItem(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                Enum.Parse<TransactionStatus>(reader.GetString(3)),
                new DateTimeOffset(reader.GetDateTime(4)), updatedAt, delay, reader.GetBoolean(11)));
        }
        return items;
    }

    private static NpgsqlCommand Command(
        NpgsqlConnection connection, NpgsqlTransaction? transaction,
        Guid projectId, TransactionListQuery query, string sql) =>
        Sql.Command(connection, transaction, sql,
            ("projectId", projectId), ("flow", query.Flow),
            ("status", query.Status?.ToString()), ("delay", query.Delay),
            ("search", query.Search is null ? null : EscapeLike(query.Search)),
            ("from", query.From), ("to", query.To), ("test", query.Test));

    public static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static DateTimeOffset? ReadTime(NpgsqlDataReader reader, int column) =>
        reader.IsDBNull(column) ? null : new DateTimeOffset(reader.GetDateTime(column));
}
