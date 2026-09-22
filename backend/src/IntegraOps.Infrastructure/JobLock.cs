using Npgsql;

namespace IntegraOps.Infrastructure;

/// <summary>
/// Session-level advisory lock that makes a periodic job run on one worker replica at a time.
/// The lock lives on its own connection for the duration of the job; other replicas skip the round.
/// </summary>
public sealed class JobLock : IAsyncDisposable
{
    private readonly NpgsqlConnection connection;
    private readonly long key;
    private JobLock(NpgsqlConnection connection, long key) { this.connection = connection; this.key = key; }

    public static async Task<JobLock?> TryAcquire(NpgsqlDataSource source, string job, CancellationToken ct)
    {
        var key = 0x4A4F42_0000_0000L ^ (long)(uint)job.GetHashCode(StringComparison.Ordinal);
        var connection = await source.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = new NpgsqlCommand("SELECT pg_try_advisory_lock(@k)", connection);
            cmd.Parameters.AddWithValue("k", key);
            if (await cmd.ExecuteScalarAsync(ct) is true) return new JobLock(connection, key);
        }
        catch { await connection.DisposeAsync(); throw; }
        await connection.DisposeAsync();
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var cmd = new NpgsqlCommand("SELECT pg_advisory_unlock(@k)", connection);
            cmd.Parameters.AddWithValue("k", key);
            await cmd.ExecuteScalarAsync();
        }
        catch { /* Closing the connection releases the lock anyway. */ }
        await connection.DisposeAsync();
    }
}
