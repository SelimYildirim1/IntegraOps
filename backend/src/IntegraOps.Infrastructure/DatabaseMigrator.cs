using System.Reflection;
using Npgsql;

namespace IntegraOps.Infrastructure;

public static class DatabaseMigrator
{
    /// <summary>Highest migration this build expects; readiness checks require it.</summary>
    public const int Latest = 7;

    public static async Task ApplyAsync(NpgsqlDataSource dataSource, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var initialize = Sql.Command(connection, transaction, """
            SELECT pg_advisory_xact_lock(71426458531001);
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version integer PRIMARY KEY,
                applied_at timestamptz NOT NULL DEFAULT clock_timestamp()
            );
            """))
            await initialize.ExecuteNonQueryAsync(ct);

        (int Version, string File)[] migrations =
            [(1, "001_initial.sql"), (2, "002_transaction_delays.sql"), (3, "003_pilot.sql"), (4, "004_operations.sql"), (5, "005_flow_versions.sql"), (6, "006_agents.sql"), (7, "007_reconciliation.sql")];
        foreach (var migration in migrations)
        {
            await using var exists = Sql.Command(connection, transaction,
                "SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE version = @version)",
                ("version", migration.Version));
            if ((bool)(await exists.ExecuteScalarAsync(ct))!) continue;

            await using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream($"IntegraOps.Infrastructure.Migrations.{migration.File}")
                ?? throw new InvalidOperationException($"Migration bulunamadı: {migration.File}");
            using var reader = new StreamReader(stream);
            await using var migrate = Sql.Command(connection, transaction, await reader.ReadToEndAsync(ct));
            await migrate.ExecuteNonQueryAsync(ct);
            await using var mark = Sql.Command(connection, transaction,
                "INSERT INTO schema_migrations (version) VALUES (@version)", ("version", migration.Version));
            await mark.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }
}
