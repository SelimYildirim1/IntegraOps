using Npgsql;
using NpgsqlTypes;

namespace IntegraOps.Infrastructure;

internal static class Sql
{
    public static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        string sql, params (string Name, object? Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters)
        {
            var parameter = new NpgsqlParameter { ParameterName = name, Value = value ?? DBNull.Value };
            if (value is null) parameter.NpgsqlDbType = NpgsqlDbType.Unknown;
            command.Parameters.Add(parameter);
        }
        return command;
    }
}
