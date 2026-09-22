using System.Diagnostics.Metrics;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace IntegraOps.Infrastructure;

/// <summary>
/// Application-level instruments. Names follow OpenTelemetry conventions; the Prometheus OTLP
/// receiver turns "integraops.events.processing.duration" (unit s) into
/// "integraops_events_processing_duration_seconds".
/// Labels never carry project, transaction or user identifiers to keep cardinality bounded
/// and to keep customer data out of the monitoring store.
/// </summary>
public static class IntegraOpsMetrics
{
    public const string MeterName = "IntegraOps";
    public static readonly Meter Meter = new(MeterName, Version);

    public static readonly Counter<long> EventsAccepted = Meter.CreateCounter<long>(
        "integraops.events.accepted", "{event}", "Events accepted by the API; duplicate=true for idempotent repeats.");

    public static readonly Counter<long> EventsProcessed = Meter.CreateCounter<long>(
        "integraops.events.processed", "{event}", "Queue deliveries handled by the worker, by outcome.");

    public static readonly Histogram<double> EventProcessingDuration = Meter.CreateHistogram<double>(
        "integraops.events.processing.duration", "s", "Time from API acceptance to worker processing.",
        advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 300, 900, 3600] });

    public static readonly Counter<long> OutboxPublished = Meter.CreateCounter<long>(
        "integraops.outbox.published", "{event}", "Outbox records confirmed by RabbitMQ.");

    public static readonly Counter<long> DelayScans = Meter.CreateCounter<long>(
        "integraops.delay_scan.runs", "{run}", "Delay scanner runs, by outcome.");

    public static readonly Counter<long> Deliveries = Meter.CreateCounter<long>(
        "integraops.notifications.deliveries", "{attempt}", "Notification delivery attempts, by channel kind and outcome.");

    private static long lastDelayScanSuccess;

    static IntegraOpsMetrics()
    {
        Meter.CreateObservableGauge("integraops.delay_scan.last_success",
            () => Interlocked.Read(ref lastDelayScanSuccess) is var v and > 0 ? [new Measurement<long>(v)] : Array.Empty<Measurement<long>>(),
            "s", "Unix time of the last completed delay scan.");
    }

    public static void DelayScanSucceeded(DateTimeOffset at)
    {
        Interlocked.Exchange(ref lastDelayScanSuccess, at.ToUnixTimeSeconds());
        DelayScans.Add(1, new KeyValuePair<string, object?>("outcome", "success"));
    }

    public static string Version =>
        Environment.GetEnvironmentVariable("APP_VERSION") is { Length: > 0 } v ? v
        : typeof(IntegraOpsMetrics).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";

    public static string Revision => Environment.GetEnvironmentVariable("APP_REVISION") is { Length: > 0 } r ? r : "unknown";
}

public static class TelemetrySetup
{
    /// <summary>
    /// Enables OTLP export only when an endpoint is configured, so local runs without a monitoring
    /// stack do not log export failures. Endpoints are full OTLP/HTTP URLs, for example
    /// http://prometheus:9090/api/v1/otlp/v1/metrics and http://jaeger:4318/v1/traces.
    /// </summary>
    public static IServiceCollection AddIntegraOpsTelemetry(this IServiceCollection services, IConfiguration configuration,
        string serviceName, Action<MeterProviderBuilder>? metrics = null, Action<TracerProviderBuilder>? traces = null)
    {
        var metricsEndpoint = configuration["Telemetry:MetricsEndpoint"];
        var tracesEndpoint = configuration["Telemetry:TracesEndpoint"];
        if (string.IsNullOrWhiteSpace(metricsEndpoint) && string.IsNullOrWhiteSpace(tracesEndpoint)) return services;
        var interval = Math.Clamp(configuration.GetValue("Telemetry:ExportIntervalSeconds", 15), 5, 300);
        var builder = services.AddOpenTelemetry().ConfigureResource(r => r
            .AddService(serviceName, serviceNamespace: "integraops", serviceVersion: IntegraOpsMetrics.Version)
            .AddAttributes([new("deployment.environment.name", configuration["Telemetry:Environment"] ?? "local"),
                new("vcs.ref.head.revision", IntegraOpsMetrics.Revision)]));
        if (!string.IsNullOrWhiteSpace(metricsEndpoint))
            builder.WithMetrics(m =>
            {
                m.AddMeter(IntegraOpsMetrics.MeterName, "System.Runtime", "Npgsql", "System.Net.Http");
                metrics?.Invoke(m);
                m.AddOtlpExporter((o, reader) =>
                {
                    o.Endpoint = new Uri(metricsEndpoint);
                    o.Protocol = OtlpExportProtocol.HttpProtobuf;
                    reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = interval * 1000;
                    reader.TemporalityPreference = MetricReaderTemporalityPreference.Cumulative;
                });
            });
        if (!string.IsNullOrWhiteSpace(tracesEndpoint))
            builder.WithTracing(t =>
            {
                t.AddSource("Npgsql").AddHttpClientInstrumentation();
                traces?.Invoke(t);
                t.AddOtlpExporter(o =>
                {
                    o.Endpoint = new Uri(tracesEndpoint);
                    o.Protocol = OtlpExportProtocol.HttpProtobuf;
                });
            });
        return services;
    }
}
