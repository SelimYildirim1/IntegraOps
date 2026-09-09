using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IntegraOps.Application;
using IntegraOps.Infrastructure;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 64 * 1024);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)));
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings__Postgres gerekli.");
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<ITrackingStore, PostgresTrackingStore>();

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
    catch (NpgsqlException) when (!context.RequestAborted.IsCancellationRequested)
    {
        app.Logger.LogError("Veritabanı işlemi tamamlanamadı. TraceId: {TraceId}", context.TraceIdentifier);
        await Results.Problem(statusCode: 503, title: "Kayıt servisine şu anda ulaşılamıyor. Aynı eventId ile tekrar deneyin.")
            .ExecuteAsync(context);
    }
});

app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/ready", async (NpgsqlDataSource dataSource, CancellationToken ct) =>
{
    await using var command = dataSource.CreateCommand("SELECT 1 FROM schema_migrations WHERE version = 2");
    return await command.ExecuteScalarAsync(ct) is null
        ? Results.Problem(statusCode: 503, title: "Migration gerekli.")
        : Results.Ok(new { status = "ready" });
});

// Development provisioning only. A production tenant/user administration flow is a later milestone.
app.MapPost("/api/v1/bootstrap/projects", async (HttpContext context, CreateProjectRequest request,
    ITrackingStore store, CancellationToken ct) =>
{
    var supplied = context.Request.Headers["X-Bootstrap-Key"].ToString();
    if (supplied.Length is < 32 or > 512 ||
        !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(supplied)), expectedBootstrapHash))
        return Results.Problem(statusCode: 401, title: "Geçerli kurulum anahtarı gerekli.");
    var project = await store.CreateProjectAsync(request, ct);
    return Results.Json(project, statusCode: 201);
});

var projectApi = app.MapGroup("/api/v1").AddEndpointFilter(async (invocation, next) =>
{
    var context = invocation.HttpContext;
    var key = context.Request.Headers["X-API-Key"].ToString();
    var store = context.RequestServices.GetRequiredService<ITrackingStore>();
    var access = await store.AuthenticateAsync(key, context.RequestAborted);
    if (access is null) return Results.Problem(statusCode: 401, title: "Geçerli proje API anahtarı gerekli.");
    context.Items[typeof(ProjectAccess)] = access;
    return await next(invocation);
});

projectApi.MapPost("/flows", async (HttpContext context, CreateFlowRequest request,
    ITrackingStore store, CancellationToken ct) =>
{
    var access = (ProjectAccess)context.Items[typeof(ProjectAccess)]!;
    return Results.Json(await store.CreateFlowAsync(access.ProjectId, request, ct), statusCode: 201);
});

projectApi.MapPost("/events", async (HttpContext context, SubmitEventRequest request,
    ITrackingStore store, TimeProvider timeProvider, CancellationToken ct) =>
{
    var access = (ProjectAccess)context.Items[typeof(ProjectAccess)]!;
    var validated = InputValidation.Event(request, timeProvider.GetUtcNow());
    var receipt = await store.AcceptAsync(access.ProjectId, validated, ct);
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

await app.RunAsync();
