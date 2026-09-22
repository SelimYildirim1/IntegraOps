using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IntegraOps.Application;
using IntegraOps.Infrastructure;
using IntegraOps.Api;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 64 * 1024);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)));
builder.Services.AddProblemDetails();
builder.Services.AddPilot(builder.Configuration);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddSingleton(TimeProvider.System);
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings__Postgres gerekli.");
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<ITrackingStore, PostgresTrackingStore>();
builder.Services.AddSingleton<PostgresTransactionQueries>();
builder.Services.AddSingleton<ITransactionQueries>(sp => sp.GetRequiredService<PostgresTransactionQueries>());
builder.Services.AddSingleton<PostgresOperationsQueries>();
builder.Services.AddSingleton<IPanelQueries, PostgresPanelQueries>();
builder.Services.AddSingleton<IFlowQueries, PostgresFlowQueries>();
builder.Services.AddSingleton<HealthMetricsCollector>();
builder.Services.AddSingleton<DataExporter>();
builder.Services.AddHostedService<ExportRunner>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<HealthMetricsCollector>());
builder.Services.AddIntegraOpsTelemetry(builder.Configuration, "api",
    metrics: m => m.AddAspNetCoreInstrumentation(),
    // Health probes are frequent and carry no user traffic.
    traces: t => t.AddAspNetCoreInstrumentation(o => o.Filter = c => !c.Request.Path.StartsWithSegments("/health")));

var app = builder.Build();
if (args.Contains("--migrate", StringComparer.Ordinal))
{
    await DatabaseMigrator.ApplyAsync(app.Services.GetRequiredService<NpgsqlDataSource>(), CancellationToken.None);
    await app.DisposeAsync();
    return;
}

var bootstrapKey = builder.Configuration["Bootstrap:Key"];
if (string.IsNullOrWhiteSpace(bootstrapKey) || bootstrapKey.Length < 32)
    throw new InvalidOperationException("Yerel proje oluşturmak için en az 32 karakterli Bootstrap__Key gerekli.");
var expectedBootstrapHash = SHA256.HashData(Encoding.UTF8.GetBytes(bootstrapKey));

app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (PilotHttpException ex)
    {
        await Results.Problem(statusCode: ex.Status, title: ex.Message).ExecuteAsync(context);
    }
    catch (RequestValidationException ex)
    {
        await Results.ValidationProblem(ex.Errors).ExecuteAsync(context);
    }
    catch (ResourceNotFoundException ex)
    {
        await Results.Problem(statusCode: 404, title: ex.Message).ExecuteAsync(context);
    }
    catch (ResourceConflictException ex)
    {
        await Results.Problem(statusCode: 409, title: ex.Message).ExecuteAsync(context);
    }
    catch (NpgsqlException ex) when (!context.RequestAborted.IsCancellationRequested)
    {
        // SqlState and the server's message name the failing statement without echoing parameter values.
        app.Logger.LogError("Veritabanı işlemi tamamlanamadı. TraceId: {TraceId} SqlState: {SqlState} Hata: {Message}",
            context.TraceIdentifier, ex.SqlState, (ex as PostgresException)?.MessageText ?? ex.GetType().Name);
        await Results.Problem(statusCode: 503, title: "Kayıt servisine şu anda ulaşılamıyor. Aynı eventId ile tekrar deneyin.")
            .ExecuteAsync(context);
    }
});
app.UseRateLimiter();

app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/version", () => Results.Ok(new { version = IntegraOpsMetrics.Version, revision = IntegraOpsMetrics.Revision }));
app.MapGet("/health/ready", async (NpgsqlDataSource dataSource, CancellationToken ct) =>
{
    await using var command = dataSource.CreateCommand($"SELECT 1 FROM schema_migrations WHERE version = {DatabaseMigrator.Latest}");
    return await command.ExecuteScalarAsync(ct) is null
        ? Results.Problem(statusCode: 503, title: "Migration gerekli.")
        : Results.Ok(new { status = "ready" });
});

bool BootstrapValid(HttpContext context)
{
    var supplied = context.Request.Headers["X-Bootstrap-Key"].ToString();
    return supplied.Length is >= 32 and <= 512 && CryptographicOperations.FixedTimeEquals(
        SHA256.HashData(Encoding.UTF8.GetBytes(supplied)), expectedBootstrapHash);
}

// Initial setup closes after the first owner exists.
app.MapPost("/api/v1/bootstrap/projects", async (HttpContext context, CreateProjectRequest request,
    ITrackingStore store, PostgresIdentityStore identities, CancellationToken ct) =>
{
    if (!BootstrapValid(context))
        return Results.Problem(statusCode: 401, title: "Geçerli kurulum anahtarı gerekli.");
    if (await identities.HasUsers(ct)) return Results.Problem(statusCode: 409, title: "Kurulum tamamlandı. Panelden proje oluşturun.");
    var project = await store.CreateProjectAsync(request, ct);
    return Results.Json(project, statusCode: 201);
});

var projectApi = app.MapGroup("/api/v1").AddEndpointFilter(PilotEndpoints.ProjectFilter);
PilotEndpoints.Map(app, projectApi, BootstrapValid);
EnterpriseEndpoints.Map(app, projectApi, app.Configuration);
await app.Services.GetRequiredService<PostgresOrganizationStore>()
    .SyncSystemAdmins((app.Configuration["SystemAdmin:Emails"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries), CancellationToken.None);

projectApi.MapPost("/flows", async (HttpContext context, CreateFlowRequest request,
    ITrackingStore store, CancellationToken ct) =>
{
    var access = (ProjectAccess)context.Items[typeof(ProjectAccess)]!;
    return Results.Json(await store.CreateFlowAsync(access.ProjectId, request, ct), statusCode: 201);
});

projectApi.MapPost("/events", async (HttpContext context, SubmitEventRequest request,
    ITrackingStore store, TimeProvider timeProvider, PostgresConnectionStore connections, PostgresOrganizationStore organizations, CancellationToken ct) =>
{
    var access = (ProjectAccess)context.Items[typeof(ProjectAccess)]!;
    var actor = (ProjectIdentity)context.Items[typeof(ProjectIdentity)]!;
    // Plan enforcement: suspended or expired accounts and the daily event limit reject before any processing.
    await organizations.CountEvent(actor.OrganizationId, ct);
    EventReceipt receipt; ValidatedEvent validated;
    try
    {
        validated = InputValidation.Event(request, timeProvider.GetUtcNow());
        receipt = await store.AcceptAsync(access.ProjectId, validated, ct);
    }
    catch (Exception ex) when (ex is RequestValidationException or ResourceNotFoundException or ResourceConflictException)
    {
        // Rejections are part of the connection's history so integration mistakes are visible in the panel.
        await connections.RecordUsage(actor, context.Request.Headers.UserAgent, false, ex is RequestValidationException v ? string.Join("; ", v.Errors.Select(e => e.Key + ": " + e.Value[0])) : ex.Message, ct);
        await organizations.CountRejected(actor.OrganizationId, ct);
        throw;
    }
    await connections.RecordUsage(actor, context.Request.Headers.UserAgent, true, null, ct);
    IntegraOpsMetrics.EventsAccepted.Add(1, new KeyValuePair<string, object?>("duplicate", receipt.Duplicate));
    var location = $"/api/v1/flows/{validated.Flow}/transactions/{validated.TransactionId}";
    return receipt.Duplicate ? Results.Ok(receipt) : Results.Accepted(location, receipt);
});

projectApi.MapGet("/flows/{flow}/transactions/{transactionId}", async (HttpContext context,
    string flow, string transactionId, ITrackingStore store, CancellationToken ct) =>
{
    var access = (ProjectAccess)context.Items[typeof(ProjectAccess)]!;
    var result = await store.GetTransactionAsync(access.ProjectId, flow, transactionId, ct);
    return result is null ? Results.Problem(statusCode: 404, title: "İşlem bu projede bulunamadı.") : Results.Ok(result);
});

projectApi.MapGet("/transactions", async (
    HttpContext context,
    ITransactionQueries queries,
    string? flow,
    string? status,
    string? delay,
    int? page,
    int? pageSize,
    string? search,
    string? from,
    string? to,
    string? test,
    CancellationToken ct) =>
{
    var access = (ProjectAccess)context.Items[typeof(ProjectAccess)]!;
    var query = InputValidation.TransactionList(
        flow, status, delay, page ?? 1, pageSize ?? 20, search, from, to, test);
    return Results.Ok(await queries.ListAsync(access.ProjectId, query, ct));
});

projectApi.MapGet("/flows", async (
    HttpContext context, IPanelQueries queries, CancellationToken ct) =>
{
    var access = (ProjectAccess)context.Items[typeof(ProjectAccess)]!;
    return Results.Ok(await queries.ListFlowsAsync(access.ProjectId, ct));
});

projectApi.MapGet("/transactions/summary", async (
    HttpContext context, IPanelQueries queries, string? flow, string? test, CancellationToken ct) =>
{
    var access = (ProjectAccess)context.Items[typeof(ProjectAccess)]!;
    return Results.Ok(await queries.GetSummaryAsync(access.ProjectId, flow, test, ct));
});

projectApi.MapGet("/flows/{flow}", async (
    HttpContext context, PostgresFlowVersionStore versions, string flow, CancellationToken ct) =>
{
    var access = (ProjectAccess)context.Items[typeof(ProjectAccess)]!;
    var result = await versions.Overview(access.ProjectId, InputValidation.FlowCode(flow), ct);
    return result is null
        ? Results.Problem(statusCode: 404, title: "Akış bu projede bulunamadı.")
        : Results.Ok(result);
});

await app.RunAsync();
