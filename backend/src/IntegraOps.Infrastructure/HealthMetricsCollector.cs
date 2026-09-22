using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace IntegraOps.Infrastructure;

public sealed record HealthSnapshot(
    long OutboxPending, double? OutboxOldestAgeSeconds,
    long EventsUnprocessed, double? EventsOldestUnprocessedAgeSeconds,
    double? WorkerHeartbeatAgeSeconds,
    long NotificationsPending, long NotificationsDead, long AlarmsOpen,
    long DbConnections, long DbMaxConnections, long DbSizeBytes,
    IReadOnlyList<(string Table, long Rows)> TableRows);

/// <summary>
/// Reads system-wide backlog and database health from PostgreSQL and exposes it as gauges.
/// It runs in the API process on purpose: when the worker stops, these values keep being
/// reported and show the backlog growing, which is the "worker looks alive but nothing moves"
/// signal. The values are totals across projects; no identifiers are exported.
/// </summary>
public sealed class HealthMetricsCollector : BackgroundService
{
    private static readonly string[] Tables =
        ["transactions", "transaction_events", "outbox_messages", "alarms", "alarm_events",
         "notification_deliveries", "audit_log", "transaction_delays"];

    private readonly NpgsqlDataSource source;
    private readonly ILogger<HealthMetricsCollector> logger;
    private volatile HealthSnapshot? snapshot;
    private long lastSuccess;

    public HealthMetricsCollector(NpgsqlDataSource source, ILogger<HealthMetricsCollector> logger)
    {
        this.source = source;
        this.logger = logger;
        var m = IntegraOpsMetrics.Meter;
        Gauge(m, "integraops.outbox.pending", "{event}", "Outbox records not yet confirmed by RabbitMQ.", s => s.OutboxPending);
        GaugeOptional(m, "integraops.outbox.oldest_age", "s", "Age of the oldest unpublished outbox record.", s => s.OutboxOldestAgeSeconds);
        Gauge(m, "integraops.events.unprocessed", "{event}", "Accepted events not yet processed by the worker.", s => s.EventsUnprocessed);
        GaugeOptional(m, "integraops.events.oldest_unprocessed_age", "s", "Age of the oldest accepted but unprocessed event.", s => s.EventsOldestUnprocessedAgeSeconds);
        GaugeOptional(m, "integraops.worker.heartbeat_age", "s", "Seconds since the worker last wrote its heartbeat.", s => s.WorkerHeartbeatAgeSeconds, zeroWhenMissing: false);
        Gauge(m, "integraops.notifications.pending", "{delivery}", "Alarm notifications waiting to be sent.", s => s.NotificationsPending);
        Gauge(m, "integraops.notifications.dead", "{delivery}", "Alarm notifications that stopped retrying.", s => s.NotificationsDead);
        Gauge(m, "integraops.alarms.open", "{alarm}", "Open alarms across all projects.", s => s.AlarmsOpen);
        Gauge(m, "integraops.db.connections", "{connection}", "Connections to the IntegraOps database.", s => s.DbConnections);
        Gauge(m, "integraops.db.max_connections", "{connection}", "PostgreSQL max_connections setting.", s => s.DbMaxConnections);
        Gauge(m, "integraops.db.size", "By", "Size of the IntegraOps database.", s => s.DbSizeBytes);
        m.CreateObservableGauge("integraops.db.table_rows", () => snapshot is { } s
                ? s.TableRows.Select(t => new Measurement<long>(t.Rows, new KeyValuePair<string, object?>("table", t.Table)))
                : [],
            "{row}", "Estimated live rows per table, for data growth.");
        m.CreateObservableGauge("integraops.health.collector_last_success",
            () => Interlocked.Read(ref lastSuccess) is var v and > 0 ? [new Measurement<long>(v)] : Array.Empty<Measurement<long>>(),
            "s", "Unix time of the last successful database health read.");
    }

    public HealthSnapshot? Current => snapshot;

    private void Gauge(Meter m, string name, string unit, string description, Func<HealthSnapshot, long> read) =>
        m.CreateObservableGauge(name, () => snapshot is { } s ? [new Measurement<long>(read(s))] : Array.Empty<Measurement<long>>(), unit, description);

    // A missing age (no pending record) is reported as 0 so alerts resolve. A worker that never
    // wrote a heartbeat stays absent instead, so the absence alert fires.
    private void GaugeOptional(Meter m, string name, string unit, string description, Func<HealthSnapshot, double?> read,
        bool zeroWhenMissing = true) =>
        m.CreateObservableGauge(name, () => snapshot is { } s && (read(s) ?? (zeroWhenMissing ? 0 : null)) is double v
            ? [new Measurement<double>(v)] : Array.Empty<Measurement<double>>(), unit, description);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        do
        {
            try
            {
                snapshot = await ReadAsync(stoppingToken);
                Interlocked.Exchange(ref lastSuccess, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // Keep the previous snapshot out of the export so stale values do not hide an outage.
                snapshot = null;
                logger.LogWarning("Sağlık ölçümleri okunamadı ({ErrorType}).", ex.GetType().Name);
            }
        } while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    public async Task<HealthSnapshot> ReadAsync(CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var cmd = Sql.Command(c, null, """
            SELECT
              (SELECT count(*) FROM outbox_messages WHERE published_at IS NULL),
              (SELECT extract(epoch FROM clock_timestamp() - min(created_at))::float8 FROM outbox_messages WHERE published_at IS NULL),
              (SELECT count(*) FROM transaction_events WHERE processed_at IS NULL),
              (SELECT extract(epoch FROM clock_timestamp() - min(received_at))::float8 FROM transaction_events WHERE processed_at IS NULL),
              (SELECT extract(epoch FROM clock_timestamp() - max(seen_at))::float8 FROM worker_heartbeats),
              (SELECT count(*) FROM notification_deliveries WHERE state IN ('pending','sending')),
              (SELECT count(*) FROM notification_deliveries WHERE state = 'dead'),
              (SELECT count(*) FROM alarms WHERE resolved_at IS NULL),
              (SELECT count(*) FROM pg_stat_activity WHERE datname = current_database()),
              current_setting('max_connections')::bigint,
              pg_database_size(current_database())
            """);
        long outboxPending, eventsUnprocessed, notificationsPending, notificationsDead, alarmsOpen, connections, maxConnections, size;
        double? outboxAge, eventAge, heartbeatAge;
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            await r.ReadAsync(ct);
            outboxPending = r.GetInt64(0);
            outboxAge = r.IsDBNull(1) ? null : r.GetDouble(1);
            eventsUnprocessed = r.GetInt64(2);
            eventAge = r.IsDBNull(3) ? null : r.GetDouble(3);
            heartbeatAge = r.IsDBNull(4) ? null : r.GetDouble(4);
            notificationsPending = r.GetInt64(5);
            notificationsDead = r.GetInt64(6);
            alarmsOpen = r.GetInt64(7);
            connections = r.GetInt64(8);
            maxConnections = r.GetInt64(9);
            size = r.GetInt64(10);
        }
        var rows = new List<(string, long)>();
        await using (var tables = Sql.Command(c, null,
            "SELECT relname::text, n_live_tup FROM pg_stat_user_tables WHERE relname = ANY(@t) ORDER BY relname", ("t", Tables)))
        await using (var r = await tables.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) rows.Add((r.GetString(0), r.GetInt64(1)));
        return new HealthSnapshot(outboxPending, outboxAge, eventsUnprocessed, eventAge, heartbeatAge,
            notificationsPending, notificationsDead, alarmsOpen, connections, maxConnections, size, rows);
    }
}
