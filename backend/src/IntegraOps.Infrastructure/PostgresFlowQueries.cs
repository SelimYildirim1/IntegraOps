using IntegraOps.Application;
using IntegraOps.Domain;
using Npgsql;

namespace IntegraOps.Infrastructure;

public sealed class PostgresFlowQueries(NpgsqlDataSource dataSource) : IFlowQueries
{
    public async Task<FlowDetails?> GetAsync(
        Guid projectId, string flow, CancellationToken ct)
    {
        var code = InputValidation.FlowCode(flow);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = Sql.Command(connection, null, """
            SELECT f.id, f.code, f.name,
                   s.code, s.name, s.position, s.max_delay_seconds
            FROM flows f
            LEFT JOIN flow_steps s ON s.flow_id = f.id
            WHERE f.project_id = @projectId AND f.code = @flow
            ORDER BY s.position
            """, ("projectId", projectId), ("flow", code));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var id = reader.GetGuid(0);
        var foundCode = reader.GetString(1);
        var name = reader.GetString(2);
        var steps = new List<FlowStep>();
        do
        {
            if (!reader.IsDBNull(3))
                steps.Add(new FlowStep(
                    reader.GetString(3), reader.GetString(4), reader.GetInt16(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6)));
        } while (await reader.ReadAsync(ct));
        return new FlowDetails(id, foundCode, name, steps);
    }
}
