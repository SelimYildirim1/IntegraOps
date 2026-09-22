using IntegraOps.Infrastructure;
using IntegraOps.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Npgsql;
using RabbitMQ.Client;

var builder = Host.CreateApplicationBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings__Postgres gerekli.");
var rabbitUri = builder.Configuration["RabbitMQ:Uri"]
    ?? throw new InvalidOperationException("RabbitMQ__Uri gerekli.");
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<PostgresTrackingStore>();
builder.Services.AddSingleton<PostgresDelayStore>();
builder.Services.AddPilot(builder.Configuration);
builder.Services.AddHostedService<NotificationDispatcher>();
builder.Services.AddHostedService<WorkerHeartbeat>();
builder.Services.AddSingleton<OutboxDispatcher>();
builder.Services.AddSingleton(new ConnectionFactory
{
    Uri = new Uri(rabbitUri),
    // The hosted services own reconnect loops, including initial connection failures.
    AutomaticRecoveryEnabled = false,
    RequestedConnectionTimeout = TimeSpan.FromSeconds(10),
    RequestedHeartbeat = TimeSpan.FromSeconds(10),
    ConsumerDispatchConcurrency = 1,
    ClientProvidedName = "integraops-worker"
});
builder.Services.AddHostedService<OutboxPublisher>();
builder.Services.AddHostedService<EventConsumer>();
builder.Services.AddHostedService<DelayMonitor>();
builder.Services.AddHostedService<MailDispatcher>();
builder.Services.AddSingleton<IConfigurationReader, ConfigurationReader>();
builder.Services.AddHostedService<DigestScheduler>();
builder.Services.AddHostedService<EnterpriseScheduler>();
builder.Services.AddSingleton<RetentionSweeper>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RetentionSweeper>());
builder.Services.AddIntegraOpsTelemetry(builder.Configuration, "worker");
await builder.Build().RunAsync();
