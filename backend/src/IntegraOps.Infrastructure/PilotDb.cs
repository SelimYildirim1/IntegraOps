using Npgsql;

namespace IntegraOps.Infrastructure;

// New administration modules use dictionaries for small read models.
// SQL column names are the JSON contract (snake_case).
public static class PilotDb
{
    public static async Task<List<Dictionary<string, object?>>> Rows(NpgsqlConnection c,
        NpgsqlTransaction? tx, string sql, CancellationToken ct, params (string, object?)[] args)
    {
        await using var cmd = Sql.Command(c, tx, sql, args);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<Dictionary<string, object?>>();
        while (await r.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    public static async Task<int> Run(NpgsqlConnection c, NpgsqlTransaction? tx, string sql,
        CancellationToken ct, params (string, object?)[] args)
    {
        await using var cmd = Sql.Command(c, tx, sql, args);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public static async Task<object?> Value(NpgsqlConnection c, NpgsqlTransaction? tx, string sql,
        CancellationToken ct, params (string, object?)[] args)
    {
        await using var cmd = Sql.Command(c, tx, sql, args);
        return await cmd.ExecuteScalarAsync(ct);
    }

    public static Task<int> Audit(NpgsqlConnection c, NpgsqlTransaction? tx, Guid project, Guid? actor,
        string action, string target, CancellationToken ct) => Run(c, tx,
        "INSERT INTO audit_log(project_id,actor_id,action,target) VALUES (@p,CAST(@u AS uuid),@a,@t)", ct,
        ("p", project), ("u", actor), ("a", action), ("t", target));
}
