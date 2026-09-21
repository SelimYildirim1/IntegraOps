# IntegraOps V1 — Backend tam kodlar

Önce 00 numaralı kurulum rehberini oku. Bu bölümde yeni modüller ve mevcut backend’in referans dosyaları birlikte verilmiştir. Migration’ı çalıştırmadan önce bütün DEĞİŞTİR/YENİ dosyaları tamamla. Eski migration dosyaları referanstır; uygulanmış şemayı değiştirmek için düzenlenmez.

**18 yeni/değişen dosya; 31 referans dosya.**

Yollar proje köküne göredir. YENİ: dosyayı oluştur. DEĞİŞTİR: dosyanın tam içeriğini aşağıdakiyle değiştir. REFERANS: önceki sürümle aynı, sende varsa tekrar kopyalaman gerekmez.

## Dosya listesi

| İşlem | Dosya | Görevi |
|---|---|---|
| YENİ | `backend/src/IntegraOps.Api/PilotEndpoints.cs` | Yeni HTTP uçları; her istek için oturum, proje ve rol denetimi. |
| DEĞİŞTİR | `backend/src/IntegraOps.Api/Program.cs` | API başlangıcı: bağlantılar, servis kayıtları, hata yönetimi ve HTTP uçları. |
| YENİ | `backend/src/IntegraOps.Domain/AlarmRules.cs` | Hangi gözlemin alarm oluşturduğunu ve kapanışın ne anlama geldiğini belirler. |
| DEĞİŞTİR | `backend/src/IntegraOps.Infrastructure/DatabaseMigrator.cs` | 003 migration’ını önceki sürümlerden sonra yalnızca bir kez uygular. |
| DEĞİŞTİR | `backend/src/IntegraOps.Infrastructure/IntegraOps.Infrastructure.csproj` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| YENİ | `backend/src/IntegraOps.Infrastructure/Migrations/003_pilot.sql` | Kullanıcı, oturum, üyelik, alarm, bildirim ve yönetim geçmişi tablolarını ekler. |
| YENİ | `backend/src/IntegraOps.Infrastructure/NotificationTransport.cs` | SMTP gönderimi; izin verilen hedefe imzalı webhook, DNS denetimi ve zaman aşımı. |
| YENİ | `backend/src/IntegraOps.Infrastructure/PilotDb.cs` | Parametreli SQL okumalarını/yazmalarını ve yönetim kaydı eklemeyi ortaklaştırır. |
| YENİ | `backend/src/IntegraOps.Infrastructure/PilotSecurity.cs` | Girdi kontrolleri, kimlik modelleri ve ortak Data Protection kayıtları. |
| YENİ | `backend/src/IntegraOps.Infrastructure/PostgresAlarmStore.cs` | İşlem güncellemesiyle aynı veritabanı işleminde alarmı ve gönderim kayıtlarını oluşturur. |
| DEĞİŞTİR | `backend/src/IntegraOps.Infrastructure/PostgresDelayStore.cs` | Gecikmeyi güncellerken yeni alarm kurallarını da uygular; mevcut işlemleri tarar. |
| YENİ | `backend/src/IntegraOps.Infrastructure/PostgresIdentityStore.cs` | Giriş, parola, proje üyeliği, roller ve API anahtarı işlemlerini veritabanında yürütür. |
| YENİ | `backend/src/IntegraOps.Infrastructure/PostgresPilotQueries.cs` | Alarm listesi/detayı, gördüm işareti, kanallar, tekrar gönderim ve sistem görünümü. |
| YENİ | `backend/src/IntegraOps.Worker/NotificationDispatcher.cs` | Kalıcı gönderim kuyruğunu sahiplenir, gönderir, geçici hataları tekrar dener. |
| DEĞİŞTİR | `backend/src/IntegraOps.Worker/Program.cs` | Olay işleme, gecikme tarama, gönderim ve yaşam sinyali servislerini başlatır. |
| YENİ | `backend/src/IntegraOps.Worker/WorkerHeartbeat.cs` | Worker sürecinin son yaşam sinyalini veritabanına yazar. |
| YENİ | `backend/tests/IntegraOps.Tests/AlarmTests.cs` | Gecikme/hata ayrımını, geç tamamlanmayı ve doğru kapanış açıklamasını sınar. |
| DEĞİŞTİR | `IntegraOps.slnx` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/src/IntegraOps.Api/IntegraOps.Api.csproj` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/src/IntegraOps.Api/appsettings.json` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/src/IntegraOps.Application/Contracts.cs` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/src/IntegraOps.Application/FlowDetails.cs` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/src/IntegraOps.Application/InputValidation.cs` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/src/IntegraOps.Application/IntegraOps.Application.csproj` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/src/IntegraOps.Application/PanelQueries.cs` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/src/IntegraOps.Application/TransactionListing.cs` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/src/IntegraOps.Domain/DelayEvaluator.cs` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/src/IntegraOps.Domain/IntegraOps.Domain.csproj` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/src/IntegraOps.Domain/Tracking.cs` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/src/IntegraOps.Infrastructure/Messaging.cs` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/src/IntegraOps.Infrastructure/Migrations/001_initial.sql` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/src/IntegraOps.Infrastructure/Migrations/002_transaction_delays.sql` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/src/IntegraOps.Infrastructure/PostgresFlowQueries.cs` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/src/IntegraOps.Infrastructure/PostgresPanelQueries.cs` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/src/IntegraOps.Infrastructure/PostgresTrackingStore.cs` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/src/IntegraOps.Infrastructure/PostgresTransactionQueries.cs` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/src/IntegraOps.Infrastructure/Sql.cs` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/src/IntegraOps.Worker/DelayMonitor.cs` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/src/IntegraOps.Worker/EventConsumer.cs` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/src/IntegraOps.Worker/IntegraOps.Worker.csproj` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/src/IntegraOps.Worker/OutboxPublisher.cs` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/tests/IntegraOps.Tests/DelayTests.cs` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/tests/IntegraOps.Tests/IntegraOps.Tests.csproj` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/tests/IntegraOps.Tests/TimelineTests.cs` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/tests/IntegraOps.Tests/TransactionListTests.cs` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `backend/tests/IntegraOps.Tests/ValidationTests.cs` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `Directory.Build.props` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `Directory.Packages.props` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `global.json` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |

## 1. backend/src/IntegraOps.Api/PilotEndpoints.cs

**YENİ** — Yeni HTTP uçları; her istek için oturum, proje ve rol denetimi.

```csharp
using IntegraOps.Application;
using IntegraOps.Infrastructure;

namespace IntegraOps.Api;

public sealed record ProjectRule(string Roles = "owner,operator,viewer", bool SessionOnly = true);
public sealed record ProjectNameInput(string Name);

public static class PilotEndpoints
{
    public static string Bearer(HttpContext c)
    {
        var value = c.Request.Headers.Authorization.ToString();
        return value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? value[7..] : "";
    }
    public static ProjectIdentity Actor(HttpContext c) => (ProjectIdentity)c.Items[typeof(ProjectIdentity)]!;
    private static SessionUser User(HttpContext c) => (SessionUser)c.Items[typeof(SessionUser)]!;

    public static async ValueTask<object?> ProjectFilter(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var c = invocation.HttpContext; var ct = c.RequestAborted;
        var store = c.RequestServices.GetRequiredService<PostgresIdentityStore>();
        SessionUser? user = null; var project = Guid.Empty;
        if (c.Request.Headers.ContainsKey("Authorization"))
        {
            user = await store.Session(Bearer(c), ct) ?? throw new PilotHttpException(401, "Oturum sona erdi. Tekrar giriş yapın.");
            if (!Guid.TryParse(c.Request.Headers["X-Project-Id"], out project))
                throw new PilotHttpException(400, "X-Project-Id gerekli.");
        }
        var actor = await store.Access(user?.Id, project, c.Request.Headers["X-API-Key"].ToString(), ct)
            ?? throw new PilotHttpException(user is null ? 401 : 403, "Bu projeye erişim doğrulanamadı.");
        var rule = c.GetEndpoint()?.Metadata.GetMetadata<ProjectRule>();
        if (actor.UserId is null)
        {
            var write = HttpMethods.IsPost(c.Request.Method);
            var path = c.Request.Path.Value;
            var allowed = (HttpMethods.IsGet(c.Request.Method) && actor.Scope is "legacy" or "read")
                || (write && path == "/api/v1/events" && actor.Scope is "legacy" or "ingest")
                || (write && path == "/api/v1/flows" && actor.Scope == "legacy");
            if (rule?.SessionOnly == true || !allowed) throw new PilotHttpException(403, "Bu anahtarın bu işlem için yetkisi yok.");
        }
        else
        {
            var roles = rule?.Roles ?? (HttpMethods.IsGet(c.Request.Method) ? "owner,operator,viewer" : "owner,operator");
            if (!roles.Split(',').Contains(actor.Role)) throw new PilotHttpException(403, "Bu işlem için yetkiniz yok.");
        }
        c.Items[typeof(ProjectIdentity)] = actor;
        c.Items[typeof(ProjectAccess)] = new ProjectAccess(actor.OrganizationId, actor.ProjectId);
        return await next(invocation);
    }

    public static void Map(WebApplication app, RouteGroupBuilder projectApi, Func<HttpContext, bool> bootstrapValid)
    {
        app.MapPost("/api/v1/bootstrap/owner", async (HttpContext c, FirstOwnerInput input,
            ITrackingStore tracking, PostgresIdentityStore store, CancellationToken ct) =>
        {
            if (!bootstrapValid(c)) throw new PilotHttpException(401, "Kurulum anahtarı doğrulanamadı.");
            var access = await tracking.AuthenticateAsync(c.Request.Headers["X-API-Key"].ToString(), ct)
                ?? throw new PilotHttpException(401, "Mevcut proje anahtarı gerekli.");
            await store.FirstOwner(access.ProjectId, input, ct);
            return Results.Ok(new { created = true });
        }).RequireRateLimiting("login");
        app.MapPost("/api/v1/auth/login", async (LoginInput input, PostgresIdentityStore store, CancellationToken ct)
            => Results.Ok(await store.Login(input, ct))).RequireRateLimiting("login");

        var me = app.MapGroup("/api/v1/me").AddEndpointFilter(async (invocation, next) =>
        {
            var c = invocation.HttpContext;
            var user = await c.RequestServices.GetRequiredService<PostgresIdentityStore>().Session(Bearer(c), c.RequestAborted)
                ?? throw new PilotHttpException(401, "Oturum sona erdi. Tekrar giriş yapın.");
            c.Items[typeof(SessionUser)] = user;
            return await next(invocation);
        });
        me.MapGet("/user", (HttpContext c) => Results.Ok(User(c)));
        me.MapGet("/projects", async (HttpContext c, PostgresIdentityStore s, CancellationToken ct) => Results.Ok(await s.Projects(User(c).Id, ct)));
        me.MapPost("/projects", async (HttpContext c, ProjectNameInput input, PostgresIdentityStore s, CancellationToken ct)
            => Results.Json(await s.CreateProject(User(c).Id, input.Name, ct), statusCode: 201));
        me.MapPost("/logout", async (HttpContext c, PostgresIdentityStore s, CancellationToken ct) =>
        { await s.Logout(Bearer(c), ct); return Results.NoContent(); });
        me.MapPost("/password", async (HttpContext c, PasswordInput input, PostgresIdentityStore s, CancellationToken ct) =>
        { await s.ChangePassword(User(c).Id, input, ct); return Results.NoContent(); });

        projectApi.MapGet("/access", (HttpContext c) => Results.Ok(Actor(c))).WithMetadata(new ProjectRule());
        projectApi.MapGet("/alarms", async (HttpContext c, PostgresPilotQueries q, string? state, string? flow, int? page, CancellationToken ct)
            => Results.Ok(await q.Alarms(Actor(c).ProjectId, state, flow, page ?? 1, ct))).WithMetadata(new ProjectRule());
        projectApi.MapGet("/alarms/{id:guid}", async (HttpContext c, Guid id, PostgresPilotQueries q, CancellationToken ct)
            => Results.Ok(await q.Alarm(Actor(c).ProjectId, id, ct))).WithMetadata(new ProjectRule());
        projectApi.MapPost("/alarms/{id:guid}/acknowledge", async (HttpContext c, Guid id, PostgresPilotQueries q, CancellationToken ct) =>
        { await q.Acknowledge(Actor(c), id, ct); return Results.NoContent(); }).WithMetadata(new ProjectRule("owner,operator"));
        projectApi.MapPost("/deliveries/{id:guid}/retry", async (HttpContext c, Guid id, PostgresPilotQueries q, CancellationToken ct) =>
        { await q.Retry(Actor(c), id, ct); return Results.NoContent(); }).WithMetadata(new ProjectRule("owner,operator"));

        projectApi.MapGet("/members", async (HttpContext c, PostgresIdentityStore s, CancellationToken ct)
            => Results.Ok(await s.Members(Actor(c).ProjectId, ct))).WithMetadata(new ProjectRule("owner"));
        projectApi.MapPost("/members", async (HttpContext c, MemberInput input, PostgresIdentityStore s, CancellationToken ct) =>
        { await s.PutMember(Actor(c), input, ct); return Results.NoContent(); }).WithMetadata(new ProjectRule("owner"));
        projectApi.MapDelete("/members/{id:guid}", async (HttpContext c, Guid id, PostgresIdentityStore s, CancellationToken ct) =>
        { await s.RemoveMember(Actor(c), id, ct); return Results.NoContent(); }).WithMetadata(new ProjectRule("owner"));
        projectApi.MapGet("/keys", async (HttpContext c, PostgresIdentityStore s, CancellationToken ct)
            => Results.Ok(await s.Keys(Actor(c).ProjectId, ct))).WithMetadata(new ProjectRule("owner"));
        projectApi.MapPost("/keys", async (HttpContext c, KeyInput input, PostgresIdentityStore s, CancellationToken ct)
            => Results.Json(await s.CreateKey(Actor(c), input, ct), statusCode: 201)).WithMetadata(new ProjectRule("owner"));
        projectApi.MapDelete("/keys/{id:guid}", async (HttpContext c, Guid id, PostgresIdentityStore s, CancellationToken ct) =>
        { await s.RevokeKey(Actor(c), id, ct); return Results.NoContent(); }).WithMetadata(new ProjectRule("owner"));
        projectApi.MapGet("/channels", async (HttpContext c, PostgresPilotQueries q, CancellationToken ct)
            => Results.Ok(await q.Channels(Actor(c).ProjectId, ct))).WithMetadata(new ProjectRule("owner"));
        projectApi.MapPost("/channels", async (HttpContext c, ChannelInput input, PostgresPilotQueries q, CancellationToken ct)
            => Results.Json(await q.CreateChannel(Actor(c), input, ct), statusCode: 201)).WithMetadata(new ProjectRule("owner"));
        projectApi.MapPut("/channels/{id:guid}", async (HttpContext c, Guid id, EnabledInput input, PostgresPilotQueries q, CancellationToken ct) =>
        { await q.SetChannel(Actor(c), id, input.Enabled, ct); return Results.NoContent(); }).WithMetadata(new ProjectRule("owner"));
        projectApi.MapGet("/operations", async (HttpContext c, PostgresPilotQueries q, CancellationToken ct)
            => Results.Ok(await q.Operations(Actor(c).ProjectId, ct))).WithMetadata(new ProjectRule("owner"));
    }
}
```

## 2. backend/src/IntegraOps.Api/Program.cs

**DEĞİŞTİR** — API başlangıcı: bağlantılar, servis kayıtları, hata yönetimi ve HTTP uçları.

```csharp
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
builder.Services.AddSingleton<ITransactionQueries, PostgresTransactionQueries>();
builder.Services.AddSingleton<IPanelQueries, PostgresPanelQueries>();
builder.Services.AddSingleton<IFlowQueries, PostgresFlowQueries>();

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
    catch (NpgsqlException) when (!context.RequestAborted.IsCancellationRequested)
    {
        app.Logger.LogError("Veritabanı işlemi tamamlanamadı. TraceId: {TraceId}", context.TraceIdentifier);
        await Results.Problem(statusCode: 503, title: "Kayıt servisine şu anda ulaşılamıyor. Aynı eventId ile tekrar deneyin.")
            .ExecuteAsync(context);
    }
});
app.UseRateLimiter();

app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/ready", async (NpgsqlDataSource dataSource, CancellationToken ct) =>
{
    await using var command = dataSource.CreateCommand("SELECT 1 FROM schema_migrations WHERE version = 3");
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

projectApi.MapGet("/transactions", async (
    HttpContext context,
    ITransactionQueries queries,
    string? flow,
    string? status,
    string? delay,
    int? page,
    int? pageSize,
    CancellationToken ct) =>
{
    var access = (ProjectAccess)context.Items[typeof(ProjectAccess)]!;
    var query = InputValidation.TransactionList(
        flow, status, delay, page ?? 1, pageSize ?? 20);
    return Results.Ok(await queries.ListAsync(access.ProjectId, query, ct));
});

projectApi.MapGet("/flows", async (
    HttpContext context, IPanelQueries queries, CancellationToken ct) =>
{
    var access = (ProjectAccess)context.Items[typeof(ProjectAccess)]!;
    return Results.Ok(await queries.ListFlowsAsync(access.ProjectId, ct));
});

projectApi.MapGet("/transactions/summary", async (
    HttpContext context, IPanelQueries queries, string? flow, CancellationToken ct) =>
{
    var access = (ProjectAccess)context.Items[typeof(ProjectAccess)]!;
    return Results.Ok(await queries.GetSummaryAsync(access.ProjectId, flow, ct));
});

projectApi.MapGet("/flows/{flow}", async (
    HttpContext context, IFlowQueries queries, string flow, CancellationToken ct) =>
{
    var access = (ProjectAccess)context.Items[typeof(ProjectAccess)]!;
    var result = await queries.GetAsync(access.ProjectId, flow, ct);
    return result is null
        ? Results.Problem(statusCode: 404, title: "Akış bu projede bulunamadı.")
        : Results.Ok(result);
});

await app.RunAsync();
```

## 3. backend/src/IntegraOps.Domain/AlarmRules.cs

**YENİ** — Hangi gözlemin alarm oluşturduğunu ve kapanışın ne anlama geldiğini belirler.

```csharp
namespace IntegraOps.Domain;

public sealed record AlarmCondition(string Kind, string Step, string? Error, DateTimeOffset? DeadlineAt);

public static class AlarmRules
{
    public static IReadOnlyList<AlarmCondition> Current(Projection projection, DelayAssessment delay)
    {
        var conditions = projection.Steps.Where(s => s.Observation == "failed")
            .Select(s => new AlarmCondition("failed", s.Code, s.Error, null)).ToList();
        if (delay.State == "overdue" && delay.Step is not null)
            conditions.Add(new AlarmCondition("overdue", delay.Step, null, delay.DeadlineAt));
        return conditions;
    }

    public static string Resolution(Projection projection, string step) =>
        projection.Steps.Any(s => s.Code == step && s.Observation == "completed")
            ? "step_completed" : "tracking_changed";
}
```

## 4. backend/src/IntegraOps.Infrastructure/DatabaseMigrator.cs

**DEĞİŞTİR** — 003 migration’ını önceki sürümlerden sonra yalnızca bir kez uygular.

```csharp
using System.Reflection;
using Npgsql;

namespace IntegraOps.Infrastructure;

public static class DatabaseMigrator
{
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
            [(1, "001_initial.sql"), (2, "002_transaction_delays.sql"), (3, "003_pilot.sql")];
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
```

## 5. backend/src/IntegraOps.Infrastructure/IntegraOps.Infrastructure.csproj

**DEĞİŞTİR** — Mevcut yapının tamamlayıcı dosyası.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <ProjectReference Include="../IntegraOps.Application/IntegraOps.Application.csproj" />
    <PackageReference Include="Npgsql" />
    <PackageReference Include="RabbitMQ.Client" />
    <EmbeddedResource Include="Migrations/*.sql" />
  </ItemGroup>
</Project>
```

## 6. backend/src/IntegraOps.Infrastructure/Migrations/003_pilot.sql

**YENİ** — Kullanıcı, oturum, üyelik, alarm, bildirim ve yönetim geçmişi tablolarını ekler.

```sql
CREATE TABLE app_users (
    id uuid PRIMARY KEY,
    email varchar(254) NOT NULL UNIQUE,
    name varchar(120) NOT NULL,
    password_hash text NOT NULL,
    failed_logins integer NOT NULL DEFAULT 0,
    locked_until timestamptz,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE TABLE project_members (
    project_id uuid NOT NULL REFERENCES projects(id),
    user_id uuid NOT NULL REFERENCES app_users(id),
    role varchar(16) NOT NULL CHECK (role IN ('owner', 'operator', 'viewer')),
    PRIMARY KEY (project_id, user_id)
);
CREATE TABLE user_sessions (
    token_hash char(64) PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES app_users(id),
    expires_at timestamptz NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE INDEX user_sessions_user ON user_sessions(user_id);
ALTER TABLE api_keys ADD COLUMN name varchar(120) NOT NULL DEFAULT 'Önceki proje anahtarı';
ALTER TABLE api_keys ADD COLUMN scope varchar(16) NOT NULL DEFAULT 'legacy'
    CHECK (scope IN ('legacy', 'ingest', 'read'));
ALTER TABLE transactions ADD COLUMN alarm_checked_at timestamptz;
CREATE INDEX transactions_alarm_backfill ON transactions(id) WHERE alarm_checked_at IS NULL;
CREATE TABLE alarms (
    id uuid PRIMARY KEY,
    project_id uuid NOT NULL REFERENCES projects(id),
    transaction_id uuid NOT NULL REFERENCES transactions(id),
    kind varchar(16) NOT NULL CHECK (kind IN ('overdue', 'failed')),
    step_code varchar(80) NOT NULL,
    error text,
    deadline_at timestamptz,
    opened_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    last_seen_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    resolved_at timestamptz,
    resolution varchar(40),
    acknowledged_at timestamptz,
    acknowledged_by uuid REFERENCES app_users(id)
);
CREATE UNIQUE INDEX alarms_one_active ON alarms(project_id, transaction_id, kind, step_code)
    WHERE resolved_at IS NULL;
CREATE INDEX alarms_project ON alarms(project_id, opened_at DESC, id);
CREATE TABLE alarm_events (
    id uuid PRIMARY KEY,
    alarm_id uuid NOT NULL REFERENCES alarms(id),
    kind varchar(32) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    actor_id uuid REFERENCES app_users(id)
);
CREATE TABLE notification_channels (
    id uuid PRIMARY KEY,
    project_id uuid NOT NULL REFERENCES projects(id),
    name varchar(120) NOT NULL,
    kind varchar(16) NOT NULL CHECK (kind IN ('email', 'webhook')),
    target_protected text NOT NULL,
    secret_protected text,
    enabled boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (project_id, name)
);
CREATE TABLE notification_deliveries (
    id uuid PRIMARY KEY,
    event_id uuid NOT NULL REFERENCES alarm_events(id),
    channel_id uuid NOT NULL REFERENCES notification_channels(id),
    body text NOT NULL,
    state varchar(16) NOT NULL DEFAULT 'pending' CHECK (state IN ('pending','sending','sent','dead','cancelled')),
    attempts integer NOT NULL DEFAULT 0,
    available_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    lease_token uuid,
    lease_until timestamptz,
    last_error varchar(120),
    sent_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (event_id, channel_id)
);
CREATE INDEX notification_delivery_due ON notification_deliveries(available_at)
    WHERE state IN ('pending', 'sending');
CREATE TABLE worker_heartbeats (
    name varchar(100) PRIMARY KEY,
    seen_at timestamptz NOT NULL
);
CREATE TABLE audit_log (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    project_id uuid NOT NULL REFERENCES projects(id),
    actor_id uuid REFERENCES app_users(id),
    action varchar(100) NOT NULL,
    target varchar(200) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
```

## 7. backend/src/IntegraOps.Infrastructure/NotificationTransport.cs

**YENİ** — SMTP gönderimi; izin verilen hedefe imzalı webhook, DNS denetimi ve zaman aşımı.

```csharp
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace IntegraOps.Infrastructure;

public sealed class DeliveryException(string code, bool permanent = false) : Exception(code)
{
    public bool Permanent { get; } = permanent;
}

public sealed class NotificationTransport : IDisposable
{
    private readonly IConfiguration configuration;
    private readonly HashSet<string> origins;
    private readonly HashSet<string> privateOrigins;
    private readonly HttpClient client;

    public NotificationTransport(IConfiguration configuration)
    {
        this.configuration = configuration;
        origins = ParseOrigins(configuration["Notifications:WebhookOrigins"]);
        privateOrigins = ParseOrigins(configuration["Notifications:PrivateWebhookOrigins"]);
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false,
            MaxConnectionsPerServer = 4, PooledConnectionLifetime = TimeSpan.FromMinutes(1), ConnectCallback = Connect };
        client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
    }

    private static HashSet<string> ParseOrigins(string? values) => (values ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => new Uri(x).GetLeftPart(UriPartial.Authority)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public Uri ValidateTarget(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment) || uri.Scheme is not ("https" or "http"))
            throw new PilotHttpException(400, "Webhook adresi geçersiz.");
        var origin = uri.GetLeftPart(UriPartial.Authority);
        if (!origins.Contains(origin) || (uri.Scheme != "https" && !privateOrigins.Contains(origin)))
            throw new PilotHttpException(400, "Webhook hedefi sunucunun izin verilen adresleri arasında değil.");
        return uri;
    }

    public static bool PublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var b = address.GetAddressBytes();
        if (b.Length == 4)
            return b[0] != 0 && b[0] != 10 && b[0] != 127 && b[0] < 224
                && !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] is >= 16 and <= 31)
                && !(b[0] == 192 && b[1] == 168) && !(b[0] == 100 && b[1] is >= 64 and <= 127)
                && !(b[0] == 198 && b[1] is 18 or 19);
        // Only ordinary global IPv6 unicast; excludes local, multicast and IPv4 transition ranges.
        return (b[0] & 0xe0) == 0x20 && !(b[0] == 0x20 && b[1] == 0x02)
            && !(b[0] == 0x20 && b[1] == 0x01 && b[2] == 0 && b[3] == 0);
    }

    private async ValueTask<Stream> Connect(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var uri = ValidateTarget(context.InitialRequestMessage.RequestUri!.AbsoluteUri);
        var allowPrivate = privateOrigins.Contains(uri.GetLeftPart(UriPartial.Authority));
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
        if (addresses.Length == 0 || (!allowPrivate && addresses.Any(a => !PublicAddress(a))))
            throw new DeliveryException("webhook_address_rejected", true);
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch { socket.Dispose(); if (ct.IsCancellationRequested) throw; }
        }
        throw new DeliveryException("webhook_connection_failed");
    }

    public async Task Send(Guid delivery, string kind, string target, string? secret, string body, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        if (kind == "webhook")
        {
            var uri = ValidateTarget(target);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
            var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret!),
                Encoding.UTF8.GetBytes($"{timestamp}.{delivery}.{body}"))).ToLowerInvariant();
            using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            request.Headers.Add("X-IntegraOps-Delivery", delivery.ToString());
            request.Headers.Add("X-IntegraOps-Time", timestamp);
            request.Headers.Add("X-IntegraOps-Signature", "sha256=" + signature);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new DeliveryException("webhook_http_" + (int)response.StatusCode,
                    (int)response.StatusCode is >= 300 and < 500 && (int)response.StatusCode is not (408 or 425 or 429));
            return;
        }
        var host = configuration["Smtp:Host"] ?? throw new DeliveryException("smtp_not_configured", true);
        var from = configuration["Smtp:From"] ?? throw new DeliveryException("smtp_from_missing", true);
        using var smtp = new SmtpClient(host, configuration.GetValue("Smtp:Port", 587))
        {
            EnableSsl = configuration.GetValue("Smtp:StartTls", true),
            UseDefaultCredentials = false,
            Timeout = 15000
        };
        if (!string.IsNullOrEmpty(configuration["Smtp:User"]))
            smtp.Credentials = new NetworkCredential(configuration["Smtp:User"], configuration["Smtp:Password"]);
        using var payload = JsonDocument.Parse(body);
        var data = payload.RootElement;
        string Field(string name) => data.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : "—";
        var change = Field("event") == "opened" ? "Alarm açıldı" : "Alarm kapandı";
        var reason = Field("alarm_kind") == "overdue" ? "Beklenen bildirim süresinde gelmedi" : "Kaynak sistem hata bildirdi";
        var emailBody = $"{change}\n\n{reason}\nİşlem: {Field("transaction_id")}\nAkış: {Field("flow")}\nAdım: {Field("step_code")}\n"
            + $"Beklenen son zaman: {Field("deadline_at")}\nAçıklama: {Field("error")}\n\nAlarm: {Field("alarm_id")}\nBildirim: {delivery}";
        using var message = new MailMessage(from, target, $"IntegraOps · {change} · {Field("transaction_id")}", emailBody) { IsBodyHtml = false };
        message.Headers.Add("Message-ID", $"<{delivery}@integraops.local>");
        await smtp.SendMailAsync(message, timeout.Token);
    }

    public void Dispose() => client.Dispose();
}
```

## 8. backend/src/IntegraOps.Infrastructure/PilotDb.cs

**YENİ** — Parametreli SQL okumalarını/yazmalarını ve yönetim kaydı eklemeyi ortaklaştırır.

```csharp
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
```

## 9. backend/src/IntegraOps.Infrastructure/PilotSecurity.cs

**YENİ** — Girdi kontrolleri, kimlik modelleri ve ortak Data Protection kayıtları.

```csharp
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IntegraOps.Infrastructure;

public sealed class PilotHttpException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
public sealed record LoginInput(string Email, string Password);
public sealed record FirstOwnerInput(string Email, string Name, string Password);
public sealed record MemberInput(string Email, string Name, string Role, string? Password);
public sealed record KeyInput(string Name, string Scope);
public sealed record PasswordInput(string CurrentPassword, string NewPassword);
public sealed record ChannelInput(string Name, string Kind, string Target, string? Secret);
public sealed record EnabledInput(bool Enabled);
public sealed record SessionUser(Guid Id, string Email, string Name);
public sealed record ProjectIdentity(Guid ProjectId, Guid OrganizationId, Guid? UserId, string Role, string? Scope);

public static class PilotSecurity
{
    public static string Token() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static string Text(string? text, int max, string field)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > max)
            throw new PilotHttpException(400, $"{field}: 1–{max} karakter gerekli.");
        return text;
    }
    public static string Email(string? value)
    {
        var email = Text(value, 254, "E-posta").ToLowerInvariant();
        if (!MailAddress.TryCreate(email, out var address) || address.Address != email)
            throw new PilotHttpException(400, "E-posta adresi geçersiz.");
        return email;
    }
    public static string Password(string? value) => value is { Length: >= 12 and <= 128 }
        ? value : throw new PilotHttpException(400, "Parola 12–128 karakter olmalı.");
    public static string Role(string value) => value is "owner" or "operator" or "viewer"
        ? value : throw new PilotHttpException(400, "Rol geçersiz.");
    public static IServiceCollection AddPilot(this IServiceCollection services, IConfiguration configuration)
    {
        var directory = configuration["Protection:KeyPath"] ?? Path.Combine(AppContext.BaseDirectory, "keys");
        Directory.CreateDirectory(directory);
        services.AddDataProtection().SetApplicationName("IntegraOps.V1")
            .PersistKeysToFileSystem(new DirectoryInfo(directory));
        services.AddSingleton<PostgresIdentityStore>();
        services.AddSingleton<PostgresPilotQueries>();
        services.AddSingleton<NotificationTransport>();
        return services;
    }
}
```

## 10. backend/src/IntegraOps.Infrastructure/PostgresAlarmStore.cs

**YENİ** — İşlem güncellemesiyle aynı veritabanı işleminde alarmı ve gönderim kayıtlarını oluşturur.

```csharp
using System.Text.Json;
using IntegraOps.Domain;
using Npgsql;
using static IntegraOps.Infrastructure.PilotDb;

namespace IntegraOps.Infrastructure;

public static class PostgresAlarmStore
{
    // The caller already holds the transaction row lock. Events and delivery jobs
    // commit atomically with the updated projection; repeated scans cannot re-open an active alarm.
    internal static async Task Sync(NpgsqlConnection c, NpgsqlTransaction tx, FlowDefinition flow,
        Guid transaction, Projection projection, DelayAssessment delay, CancellationToken ct)
    {
        if (delay.State == "pending_processing") return;
        var desired = AlarmRules.Current(projection, delay);
        var active = await Rows(c, tx, "SELECT id,kind,step_code FROM alarms WHERE project_id=@p AND transaction_id=@t AND resolved_at IS NULL",
            ct, ("p", flow.ProjectId), ("t", transaction));
        foreach (var old in active)
        {
            var step = (string)old["step_code"]!; var kind = (string)old["kind"]!; var id = (Guid)old["id"]!;
            if (desired.Any(x => x.Step == step && x.Kind == kind)) continue;
            var reason = AlarmRules.Resolution(projection, step);
            await Run(c, tx, "UPDATE alarms SET resolved_at=clock_timestamp(),resolution=@r WHERE id=@id", ct, ("id", id), ("r", reason));
            await Record(c, tx, id, "resolved", null, ct);
        }
        foreach (var condition in desired)
        {
            var previous = active.FirstOrDefault(x => (string)x["kind"]! == condition.Kind && (string)x["step_code"]! == condition.Step);
            if (previous is not null)
            {
                await Run(c, tx, "UPDATE alarms SET last_seen_at=clock_timestamp(),error=@e,deadline_at=CAST(@d AS timestamptz) WHERE id=@id", ct,
                    ("e", condition.Error), ("d", condition.DeadlineAt), ("id", previous["id"]));
                continue;
            }
            var id = Guid.CreateVersion7();
            await Run(c, tx, """
                INSERT INTO alarms(id,project_id,transaction_id,kind,step_code,error,deadline_at)
                VALUES(@id,@p,@t,@k,@s,@e,CAST(@d AS timestamptz))
                """, ct, ("id", id), ("p", flow.ProjectId), ("t", transaction), ("k", condition.Kind),
                ("s", condition.Step), ("e", condition.Error), ("d", condition.DeadlineAt));
            await Record(c, tx, id, "opened", null, ct);
        }
        await Run(c, tx, "UPDATE transactions SET alarm_checked_at=clock_timestamp() WHERE id=@t", ct, ("t", transaction));
    }

    internal static async Task Record(NpgsqlConnection c, NpgsqlTransaction tx, Guid alarm, string kind, Guid? actor, CancellationToken ct)
    {
        var eventId = Guid.CreateVersion7();
        await Run(c, tx, "INSERT INTO alarm_events(id,alarm_id,kind,actor_id) VALUES(@e,@a,@k,CAST(@u AS uuid))", ct,
            ("e", eventId), ("a", alarm), ("k", kind), ("u", actor));
        if (kind == "acknowledged") return;
        var data = (await Rows(c, tx, """
            SELECT a.id alarm_id,a.project_id,f.code flow,t.business_id transaction_id,a.kind alarm_kind,
                a.step_code,a.error,a.deadline_at,a.opened_at,a.resolved_at,a.resolution
            FROM alarms a JOIN transactions t ON t.id=a.transaction_id JOIN flows f ON f.id=t.flow_id WHERE a.id=@a
            """, ct, ("a", alarm)))[0];
        data["event_id"] = eventId; data["event"] = kind; data["schema_version"] = 1;
        var body = JsonSerializer.Serialize(data);
        var channels = await Rows(c, tx, "SELECT id FROM notification_channels WHERE project_id=@p AND enabled", ct, ("p", data["project_id"]));
        foreach (var channel in channels)
            await Run(c, tx, "INSERT INTO notification_deliveries(id,event_id,channel_id,body) VALUES(@id,@e,@c,@b)", ct,
                ("id", Guid.CreateVersion7()), ("e", eventId), ("c", channel["id"]), ("b", body));
    }
}
```

## 11. backend/src/IntegraOps.Infrastructure/PostgresDelayStore.cs

**DEĞİŞTİR** — Gecikmeyi güncellerken yeni alarm kurallarını da uygular; mevcut işlemleri tarar.

```csharp
using IntegraOps.Application;
using IntegraOps.Domain;
using Npgsql;

namespace IntegraOps.Infrastructure;

public sealed class PostgresDelayStore(NpgsqlDataSource dataSource)
{
    public async Task<int> CheckDueAsync(int batchSize, CancellationToken ct)
    {
        if (batchSize is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(batchSize));
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var candidates = new List<(Guid Id, Guid ProjectId, Guid FlowId)>();
        // The consumer locks the same transaction row. SKIP LOCKED also allows
        // multiple workers without having them process the same candidate together.
        // Missing rows backfill in-progress transactions created before this module.
        await using (var find = Sql.Command(connection, transaction, """
            SELECT t.id, t.project_id, t.flow_id
            FROM transactions t LEFT JOIN transaction_delays d ON d.transaction_id = t.id
            WHERE (t.alarm_checked_at IS NULL OR (t.status = 'InProgress'
              AND (d.transaction_id IS NULL OR d.state = 'pending_processing' OR t.updated_at > d.checked_at
                   OR (d.state = 'waiting' AND d.deadline_at <= clock_timestamp()))))
              AND NOT EXISTS (
                  SELECT 1 FROM transaction_events e
                  WHERE e.transaction_id = t.id AND e.processed_at IS NULL)
            ORDER BY COALESCE(d.deadline_at, t.created_at), t.id
            LIMIT @batchSize FOR UPDATE OF t SKIP LOCKED
            """, ("batchSize", batchSize)))
        await using (var reader = await find.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                candidates.Add((reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2)));
        }

        foreach (var candidate in candidates)
        {
            var flow = await PostgresTrackingStore.LoadFlowAsync(connection, transaction,
                candidate.ProjectId, null, candidate.FlowId, ct)
                ?? throw new InvalidDataException("Gecikme kontrolü için akış bulunamadı.");
            var events = await PostgresTrackingStore.LoadEventsAsync(connection, transaction,
                candidate.ProjectId, candidate.Id, ct);
            await RefreshAsync(connection, transaction, flow, candidate.Id,
                TimelineProjector.Project(flow, events), ct);
        }
        await transaction.CommitAsync(ct);
        return candidates.Count;
    }

    // Called inside the consumer's transaction as well as by the periodic scan.
    internal static async Task RefreshAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        FlowDefinition flow, Guid transactionId, Projection projection, CancellationToken ct)
    {
        await using var pending = Sql.Command(connection, transaction, """
            SELECT EXISTS (SELECT 1 FROM transaction_events
                WHERE project_id = @projectId AND transaction_id = @id AND processed_at IS NULL)
            """, ("projectId", flow.ProjectId), ("id", transactionId));
        var hasPendingEvents = (bool)(await pending.ExecuteScalarAsync(ct))!;
        // Use the database clock consistently, even when API and worker run on different hosts.
        await using var clock = Sql.Command(connection, transaction, "SELECT clock_timestamp()");
        var now = new DateTimeOffset((DateTime)(await clock.ExecuteScalarAsync(ct))!);
        var assessment = hasPendingEvents
            ? new DelayAssessment("pending_processing", null, null, null)
            : DelayEvaluator.Evaluate(projection, now);

        await using var update = Sql.Command(connection, transaction, """
            INSERT INTO transaction_delays
                (transaction_id, project_id, flow_id, state, step_code, started_at, deadline_at, checked_at)
            VALUES (@id, @projectId, @flowId, @state, @step,
                CAST(@startedAt AS timestamptz), CAST(@deadlineAt AS timestamptz), @checkedAt)
            ON CONFLICT (transaction_id) DO UPDATE SET
                state = EXCLUDED.state, step_code = EXCLUDED.step_code,
                started_at = EXCLUDED.started_at, deadline_at = EXCLUDED.deadline_at,
                checked_at = EXCLUDED.checked_at
            """, ("id", transactionId), ("projectId", flow.ProjectId), ("flowId", flow.Id),
            ("state", assessment.State), ("step", assessment.Step), ("startedAt", assessment.StartedAt),
            ("deadlineAt", assessment.DeadlineAt), ("checkedAt", now));
        await update.ExecuteNonQueryAsync(ct);
        await PostgresAlarmStore.Sync(connection, transaction, flow, transactionId, projection, assessment, ct);
    }

    internal static async Task<DelayView> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid projectId, Guid transactionId, Projection projection, bool hasPendingEvents, CancellationToken ct)
    {
        // An accepted notification may already contain the missing completion.
        // Until the consumer catches up, do not present its backlog as a business delay.
        if (hasPendingEvents) return new DelayView("pending_processing", null, null, null, null);

        await using var find = Sql.Command(connection, transaction, """
            SELECT d.state, d.step_code, d.started_at, d.deadline_at, d.checked_at, t.updated_at
            FROM transaction_delays d JOIN transactions t ON t.id = d.transaction_id
            WHERE d.project_id = @projectId AND d.transaction_id = @id
            """, ("projectId", projectId), ("id", transactionId));
        await using var reader = await find.ExecuteReaderAsync(ct);
        // A briefly running older worker (during an upgrade or rollback) may have
        // changed the transaction without refreshing the new delay table.
        if (!await reader.ReadAsync(ct) || reader.GetDateTime(5) > reader.GetDateTime(4))
            return new DelayView(projection.Status == TransactionStatus.InProgress ? "pending_evaluation" : "none",
                null, null, null, null);
        return new DelayView(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : new DateTimeOffset(reader.GetDateTime(2)),
            reader.IsDBNull(3) ? null : new DateTimeOffset(reader.GetDateTime(3)),
            new DateTimeOffset(reader.GetDateTime(4)));
    }
}
```

## 12. backend/src/IntegraOps.Infrastructure/PostgresIdentityStore.cs

**YENİ** — Giriş, parola, proje üyeliği, roller ve API anahtarı işlemlerini veritabanında yürütür.

```csharp
using Microsoft.AspNetCore.Identity;
using Npgsql;
using static IntegraOps.Infrastructure.PilotDb;
using static IntegraOps.Infrastructure.PilotSecurity;

namespace IntegraOps.Infrastructure;

public sealed class PostgresIdentityStore(NpgsqlDataSource source)
{
    private readonly PasswordHasher<string> hasher = new();
    private readonly string dummyHash = new PasswordHasher<string>().HashPassword("dummy", Token());

    public async Task<bool> HasUsers(CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        return (bool)(await Value(c, null, "SELECT EXISTS(SELECT 1 FROM app_users)", ct))!;
    }

    public async Task FirstOwner(Guid project, FirstOwnerInput input, CancellationToken ct)
    {
        var email = Email(input.Email); var name = Text(input.Name, 120, "Ad"); var password = Password(input.Password);
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await Value(c, tx, "SELECT pg_advisory_xact_lock(71426458531002)", ct);
        if ((bool)(await Value(c, tx, "SELECT EXISTS(SELECT 1 FROM app_users)", ct))!)
            throw new PilotHttpException(409, "İlk kullanıcı zaten oluşturuldu. Giriş yapın.");
        var id = Guid.CreateVersion7();
        await Run(c, tx, "INSERT INTO app_users(id,email,name,password_hash) VALUES(@id,@e,@n,@h)", ct,
            ("id", id), ("e", email), ("n", name), ("h", hasher.HashPassword(email, password)));
        await Run(c, tx, "INSERT INTO project_members(project_id,user_id,role) VALUES(@p,@u,'owner')", ct, ("p", project), ("u", id));
        await Audit(c, tx, project, id, "owner.bootstrap", id.ToString(), ct);
        await tx.CommitAsync(ct);
    }

    public async Task<object> Login(LoginInput input, CancellationToken ct)
    {
        var email = Email(input.Email);
        if (input.Password is not { Length: > 0 and <= 128 }) throw new PilotHttpException(401, "E-posta veya parola doğrulanamadı.");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var rows = await Rows(c, tx, "SELECT * FROM app_users WHERE email=@e FOR UPDATE", ct, ("e", email));
        if (rows.Count == 0)
        {
            hasher.VerifyHashedPassword(email, dummyHash, input.Password);
            throw new PilotHttpException(401, "E-posta veya parola doğrulanamadı.");
        }
        var u = rows[0]; var id = (Guid)u["id"]!;
        var verified = hasher.VerifyHashedPassword(email, (string)u["password_hash"]!, input.Password);
        if (u["locked_until"] is DateTime locked && locked > DateTime.UtcNow)
            throw new PilotHttpException(401, "E-posta veya parola doğrulanamadı. Bir süre sonra tekrar deneyin.");
        if (verified == PasswordVerificationResult.Failed)
        {
            await Run(c, tx, """
                UPDATE app_users SET failed_logins=CASE WHEN failed_logins>=4 THEN 0 ELSE failed_logins+1 END,
                    locked_until=CASE WHEN failed_logins>=4 THEN clock_timestamp()+interval '15 minutes' ELSE NULL END
                WHERE id=@u
                """, ct, ("u", id));
            await tx.CommitAsync(ct);
            throw new PilotHttpException(401, "E-posta veya parola doğrulanamadı.");
        }
        await Run(c, tx, "UPDATE app_users SET failed_logins=0,locked_until=NULL,password_hash=@h WHERE id=@u", ct,
            ("u", id), ("h", verified == PasswordVerificationResult.SuccessRehashNeeded
                ? hasher.HashPassword(email, input.Password) : u["password_hash"]));
        var token = Token();
        await Run(c, tx, """
            DELETE FROM user_sessions WHERE expires_at<=clock_timestamp();
            INSERT INTO user_sessions(token_hash,user_id,expires_at) VALUES(@h,@u,clock_timestamp()+interval '8 hours')
            """, ct, ("h", Hash(token)), ("u", id));
        await tx.CommitAsync(ct);
        return new { token, expiresIn = 28800, user = new SessionUser(id, email, (string)u["name"]!) };
    }

    public async Task<SessionUser?> Session(string token, CancellationToken ct)
    {
        if (token.Length != 64) return null;
        await using var c = await source.OpenConnectionAsync(ct);
        var rows = await Rows(c, null, """
            SELECT u.id,u.email,u.name FROM user_sessions s JOIN app_users u ON u.id=s.user_id
            WHERE s.token_hash=@h AND s.expires_at>clock_timestamp()
            """, ct, ("h", Hash(token)));
        return rows.Count == 0 ? null : new((Guid)rows[0]["id"]!, (string)rows[0]["email"]!, (string)rows[0]["name"]!);
    }

    public async Task Logout(string token, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await Run(c, null, "DELETE FROM user_sessions WHERE token_hash=@h", ct, ("h", Hash(token)));
    }

    public async Task ChangePassword(Guid user, PasswordInput input, CancellationToken ct)
    {
        var password = Password(input.NewPassword);
        if (input.CurrentPassword is not { Length: > 0 and <= 128 }) throw new PilotHttpException(400, "Mevcut parola gerekli.");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var u = (await Rows(c, tx, "SELECT email,password_hash FROM app_users WHERE id=@u FOR UPDATE", ct, ("u", user)))[0];
        var email = (string)u["email"]!;
        if (hasher.VerifyHashedPassword(email, (string)u["password_hash"]!, input.CurrentPassword) == PasswordVerificationResult.Failed)
            throw new PilotHttpException(400, "Mevcut parola doğrulanamadı.");
        await Run(c, tx, "UPDATE app_users SET password_hash=@h WHERE id=@u; DELETE FROM user_sessions WHERE user_id=@u", ct,
            ("h", hasher.HashPassword(email, password)), ("u", user));
        await tx.CommitAsync(ct);
    }

    public async Task<ProjectIdentity?> Access(Guid? user, Guid project, string apiKey, CancellationToken ct)
    {
        if (user is null && apiKey.Length is < 32 or > 128) return null;
        await using var c = await source.OpenConnectionAsync(ct);
        var rows = user is not null
            ? await Rows(c, null, """
                SELECT p.id,p.organization_id,m.role,NULL::text scope FROM projects p
                JOIN project_members m ON m.project_id=p.id WHERE p.id=@p AND m.user_id=@u
                """, ct, ("p", project), ("u", user))
            : await Rows(c, null, """
                SELECT p.id,p.organization_id,'key'::text role,k.scope FROM api_keys k
                JOIN projects p ON p.id=k.project_id WHERE lower(k.key_hash)=@h AND k.revoked_at IS NULL
                """, ct, ("h", Hash(apiKey)));
        if (rows.Count == 0) return null;
        var r = rows[0];
        return new((Guid)r["id"]!, (Guid)r["organization_id"]!, user, (string)r["role"]!, r["scope"] as string);
    }

    public async Task<object> Projects(Guid user, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        return await Rows(c, null, """
            SELECT p.id,p.name,m.role FROM project_members m JOIN projects p ON p.id=m.project_id
            WHERE m.user_id=@u ORDER BY p.created_at,p.id
            """, ct, ("u", user));
    }

    public async Task<object> CreateProject(Guid user, string name, CancellationToken ct)
    {
        name = Text(name, 160, "Proje adı");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var id = Guid.CreateVersion7(); var org = Guid.CreateVersion7();
        await Run(c, tx, """
            INSERT INTO organizations(id,name) VALUES(@o,@n);
            INSERT INTO projects(id,organization_id,name) VALUES(@p,@o,@n);
            INSERT INTO project_members(project_id,user_id,role) VALUES(@p,@u,'owner')
            """, ct, ("o", org), ("p", id), ("n", name), ("u", user));
        await Audit(c, tx, id, user, "project.create", id.ToString(), ct);
        await tx.CommitAsync(ct);
        return new { id, name, role = "owner" };
    }

    public async Task<object> Members(Guid project, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        return await Rows(c, null, """
            SELECT u.id,u.email,u.name,m.role FROM project_members m JOIN app_users u ON u.id=m.user_id
            WHERE m.project_id=@p ORDER BY u.email
            """, ct, ("p", project));
    }

    public async Task PutMember(ProjectIdentity actor, MemberInput input, CancellationToken ct)
    {
        var email = Email(input.Email); var role = Role(input.Role); var name = Text(input.Name, 120, "Ad");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockProject(c, tx, actor, ct);
        // Serializes creation of one account by owners of different projects.
        await Value(c, tx, "SELECT pg_advisory_xact_lock(hashtextextended(@e,7142645))", ct, ("e", email));
        var existing = await Value(c, tx, "SELECT id FROM app_users WHERE email=@e", ct, ("e", email));
        var user = existing is Guid known ? known : Guid.CreateVersion7();
        if (existing is null)
            await Run(c, tx, "INSERT INTO app_users(id,email,name,password_hash) VALUES(@u,@e,@n,@h)", ct,
                ("u", user), ("e", email), ("n", name), ("h", hasher.HashPassword(email, Password(input.Password))));
        else if (!string.IsNullOrEmpty(input.Password))
            throw new PilotHttpException(409, "Hesap zaten var. Mevcut hesabı eklerken parola alanını boş bırakın.");
        if (role != "owner") await GuardLastOwner(c, tx, actor.ProjectId, user, ct);
        await Run(c, tx, """
            INSERT INTO project_members(project_id,user_id,role) VALUES(@p,@u,@r)
            ON CONFLICT(project_id,user_id) DO UPDATE SET role=EXCLUDED.role
            """, ct, ("p", actor.ProjectId), ("u", user), ("r", role));
        await Audit(c, tx, actor.ProjectId, actor.UserId, "member." + role, user.ToString(), ct);
        await tx.CommitAsync(ct);
    }

    public async Task RemoveMember(ProjectIdentity actor, Guid user, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockProject(c, tx, actor, ct);
        await GuardLastOwner(c, tx, actor.ProjectId, user, ct);
        await Run(c, tx, "DELETE FROM project_members WHERE project_id=@p AND user_id=@u", ct, ("p", actor.ProjectId), ("u", user));
        await Audit(c, tx, actor.ProjectId, actor.UserId, "member.remove", user.ToString(), ct);
        await tx.CommitAsync(ct);
    }

    // Re-check after acquiring the project lock: another owner may just have changed this actor's role.
    private static async Task LockProject(NpgsqlConnection c, NpgsqlTransaction tx, ProjectIdentity actor, CancellationToken ct)
    {
        await Value(c, tx, "SELECT id FROM projects WHERE id=@p FOR UPDATE", ct, ("p", actor.ProjectId));
        if (await Value(c, tx, "SELECT role FROM project_members WHERE project_id=@p AND user_id=@u", ct,
            ("p", actor.ProjectId), ("u", actor.UserId)) is not "owner") throw new PilotHttpException(403, "Proje sahibi yetkisi gerekli.");
    }
    private static async Task GuardLastOwner(NpgsqlConnection c, NpgsqlTransaction tx, Guid project, Guid user, CancellationToken ct)
    {
        if ((bool)(await Value(c, tx, """
            SELECT EXISTS(SELECT 1 FROM project_members WHERE project_id=@p AND user_id=@u AND role='owner')
                AND (SELECT count(*) FROM project_members WHERE project_id=@p AND role='owner')=1
            """, ct, ("p", project), ("u", user)))!)
            throw new PilotHttpException(409, "Projenin son sahibini kaldıramaz veya rolünü düşüremezsiniz.");
    }

    public async Task<object> Keys(Guid project, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        return await Rows(c, null, "SELECT id,name,scope,key_prefix,created_at,revoked_at FROM api_keys WHERE project_id=@p ORDER BY created_at DESC", ct, ("p", project));
    }
    public async Task<object> CreateKey(ProjectIdentity actor, KeyInput input, CancellationToken ct)
    {
        var name = Text(input.Name, 120, "Anahtar adı");
        if (input.Scope is not ("ingest" or "read")) throw new PilotHttpException(400, "Anahtar yetkisi ingest veya read olmalı.");
        var key = "io_v1_" + Token(); var id = Guid.CreateVersion7();
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockProject(c, tx, actor, ct);
        await Run(c, tx, "INSERT INTO api_keys(id,project_id,name,scope,key_hash,key_prefix) VALUES(@id,@p,@n,@s,@h,@prefix)", ct,
            ("id", id), ("p", actor.ProjectId), ("n", name), ("s", input.Scope), ("h", Hash(key)), ("prefix", key[..16]));
        await Audit(c, tx, actor.ProjectId, actor.UserId, "key.create", id.ToString(), ct);
        await tx.CommitAsync(ct);
        return new { id, key, name, scope = input.Scope };
    }
    public async Task RevokeKey(ProjectIdentity actor, Guid key, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockProject(c, tx, actor, ct);
        if (await Run(c, tx, "UPDATE api_keys SET revoked_at=COALESCE(revoked_at,clock_timestamp()) WHERE id=@id AND project_id=@p", ct,
            ("id", key), ("p", actor.ProjectId)) == 0) throw new PilotHttpException(404, "Anahtar bulunamadı.");
        await Audit(c, tx, actor.ProjectId, actor.UserId, "key.revoke", key.ToString(), ct);
        await tx.CommitAsync(ct);
    }
}
```

## 13. backend/src/IntegraOps.Infrastructure/PostgresPilotQueries.cs

**YENİ** — Alarm listesi/detayı, gördüm işareti, kanallar, tekrar gönderim ve sistem görünümü.

```csharp
using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using static IntegraOps.Infrastructure.PilotDb;
using static IntegraOps.Infrastructure.PilotSecurity;

namespace IntegraOps.Infrastructure;

public sealed class PostgresPilotQueries(NpgsqlDataSource source, IDataProtectionProvider protection, NotificationTransport transport)
{
    private readonly IDataProtector protector = protection.CreateProtector("notification-channels.v1");

    public async Task<object> Alarms(Guid project, string? state, string? flow, int page, CancellationToken ct)
    {
        state ??= "open";
        if (state is not ("open" or "resolved" or "all") || page is < 1 or > 100000)
            throw new PilotHttpException(400, "Alarm filtresi geçersiz.");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        const string where = """
            FROM alarms a JOIN transactions t ON t.id=a.transaction_id JOIN flows f ON f.id=t.flow_id
            WHERE a.project_id=@p AND (@s='all' OR (@s='open' AND a.resolved_at IS NULL) OR (@s='resolved' AND a.resolved_at IS NOT NULL))
                AND (@f='' OR f.code=@f)
            """;
        (string, object?)[] args = [("p", project), ("s", state), ("f", flow ?? ""), ("skip", (page - 1) * 25)];
        var total = await Value(c, tx, "SELECT count(*) " + where, ct, args);
        var items = await Rows(c, tx, "SELECT a.*,f.code flow,t.business_id " + where + " ORDER BY a.opened_at DESC,a.id DESC LIMIT 25 OFFSET @skip", ct, args);
        await tx.CommitAsync(ct);
        return new { items, total, page, pageSize = 25 };
    }

    public async Task<object> Alarm(Guid project, Guid id, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var alarms = await Rows(c, null, """
            SELECT a.*,f.code flow,t.business_id FROM alarms a JOIN transactions t ON t.id=a.transaction_id
            JOIN flows f ON f.id=t.flow_id WHERE a.project_id=@p AND a.id=@id
            """, ct, ("p", project), ("id", id));
        if (alarms.Count == 0) throw new PilotHttpException(404, "Alarm bu projede bulunamadı.");
        var events = await Rows(c, null, """
            SELECT e.id,e.kind,e.created_at,u.name actor FROM alarm_events e LEFT JOIN app_users u ON u.id=e.actor_id
            WHERE e.alarm_id=@id ORDER BY e.created_at,e.id
            """, ct, ("id", id));
        var deliveries = await Rows(c, null, """
            SELECT d.id,c.name channel,c.kind,e.kind event,d.state,d.attempts,d.available_at,d.last_error,d.sent_at
            FROM notification_deliveries d JOIN alarm_events e ON e.id=d.event_id
            JOIN notification_channels c ON c.id=d.channel_id WHERE e.alarm_id=@id ORDER BY d.created_at,d.id
            """, ct, ("id", id));
        return new { alarm = alarms[0], events, deliveries };
    }

    public async Task Acknowledge(ProjectIdentity actor, Guid id, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        if (await Value(c, tx, "SELECT id FROM alarms WHERE id=@id AND project_id=@p FOR UPDATE", ct,
            ("p", actor.ProjectId), ("id", id)) is null) throw new PilotHttpException(404, "Alarm bulunamadı.");
        if (await Run(c, tx, """
            UPDATE alarms SET acknowledged_at=clock_timestamp(),acknowledged_by=@u WHERE id=@id AND acknowledged_at IS NULL
            """, ct, ("id", id), ("u", actor.UserId)) > 0)
        {
            await PostgresAlarmStore.Record(c, tx, id, "acknowledged", actor.UserId, ct);
            await Audit(c, tx, actor.ProjectId, actor.UserId, "alarm.acknowledge", id.ToString(), ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task Retry(ProjectIdentity actor, Guid id, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        if (await Run(c, tx, """
            UPDATE notification_deliveries d SET state='pending',attempts=0,available_at=clock_timestamp(),last_error=NULL
            FROM notification_channels c WHERE d.channel_id=c.id AND c.project_id=@p AND c.enabled
                AND d.id=@id AND d.state='dead'
            """, ct, ("p", actor.ProjectId), ("id", id)) == 0)
            throw new PilotHttpException(409, "Bu projede yeniden denenebilir bir bildirim bulunamadı. Kanal da etkin olmalı.");
        await Audit(c, tx, actor.ProjectId, actor.UserId, "delivery.retry", id.ToString(), ct);
        await tx.CommitAsync(ct);
    }

    public async Task<object> Channels(Guid project, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var rows = await Rows(c, null, "SELECT id,name,kind,enabled,created_at,target_protected FROM notification_channels WHERE project_id=@p ORDER BY created_at", ct, ("p", project));
        foreach (var row in rows)
        {
            var target = protector.Unprotect((string)row["target_protected"]!);
            row.Remove("target_protected");
            row["target"] = row["kind"] is "webhook" ? new Uri(target).GetLeftPart(UriPartial.Authority) : target;
        }
        return rows;
    }

    public async Task<object> CreateChannel(ProjectIdentity actor, ChannelInput input, CancellationToken ct)
    {
        var name = Text(input.Name, 120, "Kanal adı"); var target = Text(input.Target, 2048, "Hedef");
        string? secret = null;
        if (input.Kind == "email") target = Email(target);
        else if (input.Kind == "webhook")
        {
            transport.ValidateTarget(target);
            secret = string.IsNullOrEmpty(input.Secret) ? Token() : input.Secret;
            if (secret.Length is < 32 or > 128) throw new PilotHttpException(400, "Webhook sırrı 32–128 karakter olmalı.");
        }
        else throw new PilotHttpException(400, "Kanal türü email veya webhook olmalı.");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var id = Guid.CreateVersion7();
        try
        {
            await Run(c, tx, """
                INSERT INTO notification_channels(id,project_id,name,kind,target_protected,secret_protected)
                VALUES(@id,@p,@n,@k,@t,@s)
                """, ct, ("id", id), ("p", actor.ProjectId), ("n", name), ("k", input.Kind),
                ("t", protector.Protect(target)), ("s", secret is null ? null : protector.Protect(secret)));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        { throw new PilotHttpException(409, "Bu kanal adı zaten kullanılıyor."); }
        await Audit(c, tx, actor.ProjectId, actor.UserId, "channel.create", id.ToString(), ct);
        await tx.CommitAsync(ct);
        return new { id, name, secret };
    }

    public async Task SetChannel(ProjectIdentity actor, Guid id, bool enabled, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        if (await Run(c, tx, "UPDATE notification_channels SET enabled=@e WHERE project_id=@p AND id=@id", ct,
            ("p", actor.ProjectId), ("id", id), ("e", enabled)) == 0) throw new PilotHttpException(404, "Kanal bulunamadı.");
        if (!enabled)
            await Run(c, tx, "UPDATE notification_deliveries SET state='cancelled' WHERE channel_id=@id AND state='pending'", ct, ("id", id));
        await Audit(c, tx, actor.ProjectId, actor.UserId, enabled ? "channel.enable" : "channel.disable", id.ToString(), ct);
        await tx.CommitAsync(ct);
    }

    public async Task<object> Operations(Guid project, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var counts = (await Rows(c, null, """
            SELECT
                (SELECT count(*) FROM outbox_messages WHERE project_id=@p AND published_at IS NULL) outbox_pending,
                (SELECT min(created_at) FROM outbox_messages WHERE project_id=@p AND published_at IS NULL) oldest_outbox_at,
                (SELECT count(*) FROM transaction_events WHERE project_id=@p AND processed_at IS NULL) events_pending,
                (SELECT count(*) FROM notification_deliveries d JOIN notification_channels n ON n.id=d.channel_id
                    WHERE n.project_id=@p AND d.state IN ('pending','sending')) notifications_pending,
                (SELECT count(*) FROM notification_deliveries d JOIN notification_channels n ON n.id=d.channel_id
                    WHERE n.project_id=@p AND d.state='dead') notifications_dead,
                (SELECT max(seen_at) FROM worker_heartbeats) worker_seen_at,
                clock_timestamp() checked_at
            """, ct, ("p", project)))[0];
        var audit = await Rows(c, null, """
            SELECT a.action,a.target,a.created_at,u.name actor FROM audit_log a LEFT JOIN app_users u ON u.id=a.actor_id
            WHERE a.project_id=@p ORDER BY a.id DESC LIMIT 100
            """, ct, ("p", project));
        return new { counts, audit };
    }
}
```

## 14. backend/src/IntegraOps.Worker/NotificationDispatcher.cs

**YENİ** — Kalıcı gönderim kuyruğunu sahiplenir, gönderir, geçici hataları tekrar dener.

```csharp
using IntegraOps.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using static IntegraOps.Infrastructure.PilotDb;

namespace IntegraOps.Worker;

public sealed class NotificationDispatcher(NpgsqlDataSource source, IDataProtectionProvider protection,
    NotificationTransport transport, IConfiguration configuration, ILogger<NotificationDispatcher> logger) : BackgroundService
{
    private readonly IDataProtector protector = protection.CreateProtector("notification-channels.v1");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await Next(stoppingToken)) await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("Bildirim kuyruğuna erişilemedi. ErrorType: {ErrorType}", ex.GetType().Name);
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task<bool> Next(CancellationToken ct)
    {
        var lease = Guid.NewGuid(); Dictionary<string, object?> job;
        await using (var c = await source.OpenConnectionAsync(ct))
        await using (var tx = await c.BeginTransactionAsync(ct))
        {
            await Run(c, tx, """
                UPDATE notification_deliveries SET state='dead',last_error='attempt_limit_after_restart'
                WHERE state='sending' AND lease_until<clock_timestamp() AND attempts>=8;
                UPDATE notification_deliveries d SET state='cancelled' FROM notification_channels c
                WHERE c.id=d.channel_id AND NOT c.enabled AND (d.state='pending' OR (d.state='sending' AND d.lease_until<clock_timestamp()))
                """, ct);
            var rows = await Rows(c, tx, """
                SELECT d.id,d.body,c.kind,c.target_protected,c.secret_protected,d.attempts
                FROM notification_deliveries d JOIN notification_channels c ON c.id=d.channel_id
                JOIN alarm_events e ON e.id=d.event_id
                WHERE c.enabled AND d.attempts<8 AND ((d.state='pending' AND d.available_at<=clock_timestamp())
                    OR (d.state='sending' AND d.lease_until<clock_timestamp()))
                    AND NOT EXISTS(SELECT 1 FROM notification_deliveries old JOIN alarm_events oe ON oe.id=old.event_id
                        WHERE old.channel_id=d.channel_id AND oe.alarm_id=e.alarm_id
                            AND old.state IN ('pending','sending') AND (old.created_at,old.id)<(d.created_at,d.id))
                ORDER BY d.available_at,d.id LIMIT 1 FOR UPDATE OF d SKIP LOCKED
                """, ct);
            if (rows.Count == 0) { await tx.CommitAsync(ct); return false; }
            job = rows[0];
            await Run(c, tx, """
                UPDATE notification_deliveries SET state='sending',attempts=attempts+1,lease_token=@l,
                    lease_until=clock_timestamp()+interval '60 seconds' WHERE id=@id
                """, ct, ("l", lease), ("id", job["id"]));
            await tx.CommitAsync(ct);
        }
        string? error = null; var permanent = false;
        try
        {
            await transport.Send((Guid)job["id"]!, (string)job["kind"]!, protector.Unprotect((string)job["target_protected"]!),
                job["secret_protected"] is string s ? protector.Unprotect(s) : null, (string)job["body"]!, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DeliveryException ex) { error = ex.Message; permanent = ex.Permanent; }
        catch (PilotHttpException) { error = "webhook_target_rejected"; permanent = true; }
        catch (Exception ex) { error = "transport_" + ex.GetType().Name; }
        var attempts = (int)job["attempts"]! + 1;
        var state = error is null ? "sent" : permanent || attempts >= 8 ? "dead" : "pending";
        var seconds = Math.Min(3600, Math.Max(1, configuration.GetValue("Notifications:RetryBaseSeconds", 30)) * Math.Pow(2, attempts - 1))
            + Random.Shared.Next(0, 4);
        await using var finish = await source.OpenConnectionAsync(ct);
        await Run(finish, null, """
            UPDATE notification_deliveries SET state=@s,last_error=@e,available_at=clock_timestamp()+@delay,
                lease_token=NULL,lease_until=NULL,sent_at=CASE WHEN @s='sent' THEN clock_timestamp() ELSE NULL END
            WHERE id=@id AND lease_token=@l AND state='sending'
            """, ct, ("s", state), ("e", error), ("delay", TimeSpan.FromSeconds(seconds)), ("id", job["id"]), ("l", lease));
        return true;
    }
}
```

## 15. backend/src/IntegraOps.Worker/Program.cs

**DEĞİŞTİR** — Olay işleme, gecikme tarama, gönderim ve yaşam sinyali servislerini başlatır.

```csharp
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
await builder.Build().RunAsync();
```

## 16. backend/src/IntegraOps.Worker/WorkerHeartbeat.cs

**YENİ** — Worker sürecinin son yaşam sinyalini veritabanına yazar.

```csharp
using IntegraOps.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace IntegraOps.Worker;

public sealed class WorkerHeartbeat(NpgsqlDataSource source, ILogger<WorkerHeartbeat> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var c = await source.OpenConnectionAsync(stoppingToken);
                await PilotDb.Run(c, null, """
                    INSERT INTO worker_heartbeats(name,seen_at) VALUES('worker',clock_timestamp())
                    ON CONFLICT(name) DO UPDATE SET seen_at=EXCLUDED.seen_at
                    """, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Yaşam sinyali yazılamadı. ErrorType: {Type}", ex.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
```

## 17. backend/tests/IntegraOps.Tests/AlarmTests.cs

**YENİ** — Gecikme/hata ayrımını, geç tamamlanmayı ve doğru kapanış açıklamasını sınar.

```csharp
using IntegraOps.Domain;
using Xunit;

namespace IntegraOps.Tests;

public sealed class AlarmTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);
    private static Projection Waiting() => new(TransactionStatus.InProgress,
        [new("erp-created", "ERP", 0, null, "completed", Now.AddSeconds(-31), null),
         new("invoice-created", "Fatura", 1, 30, "not_observed", null, null)]);

    [Fact]
    public void Overdue_does_not_claim_a_failed_invoice()
    {
        var projection = Waiting();
        var alarm = Assert.Single(AlarmRules.Current(projection, DelayEvaluator.Evaluate(projection, Now)));
        Assert.Equal("overdue", alarm.Kind); Assert.Equal("invoice-created", alarm.Step);
        Assert.Null(alarm.Error); Assert.Equal(TransactionStatus.InProgress, projection.Status);
    }
    [Fact]
    public void Late_completion_removes_condition_and_explains_resolution()
    {
        var projection = new Projection(TransactionStatus.Completed,
            [Waiting().Steps[0], Waiting().Steps[1] with { Observation = "completed", OccurredAt = Now }]);
        Assert.Empty(AlarmRules.Current(projection, DelayEvaluator.Evaluate(projection, Now)));
        Assert.Equal("step_completed", AlarmRules.Resolution(projection, "invoice-created"));
    }
    [Fact]
    public void Failure_elsewhere_does_not_claim_missing_invoice_completed()
    {
        var projection = new Projection(TransactionStatus.Failed,
            [Waiting().Steps[0] with { Observation = "failed", Error = "ERP failure" }, Waiting().Steps[1]]);
        var alarm = Assert.Single(AlarmRules.Current(projection, DelayEvaluator.Evaluate(projection, Now)));
        Assert.Equal("failed", alarm.Kind); Assert.Equal("erp-created", alarm.Step);
        Assert.Equal("tracking_changed", AlarmRules.Resolution(projection, "invoice-created"));
    }
    [Fact]
    public void Before_deadline_there_is_no_alarm()
    {
        var projection = Waiting();
        Assert.Empty(AlarmRules.Current(projection, DelayEvaluator.Evaluate(projection, Now.AddSeconds(-2))));
    }
}
```

## 18. IntegraOps.slnx

**DEĞİŞTİR** — Mevcut yapının tamamlayıcı dosyası.

```xml
<Solution>
  <Folder Name="/backend/src/">
    <Project Path="backend/src/IntegraOps.Domain/IntegraOps.Domain.csproj" />
    <Project Path="backend/src/IntegraOps.Application/IntegraOps.Application.csproj" />
    <Project Path="backend/src/IntegraOps.Infrastructure/IntegraOps.Infrastructure.csproj" />
    <Project Path="backend/src/IntegraOps.Api/IntegraOps.Api.csproj" />
    <Project Path="backend/src/IntegraOps.Worker/IntegraOps.Worker.csproj" />
  </Folder>
  <Folder Name="/backend/tests/">
    <Project Path="backend/tests/IntegraOps.Tests/IntegraOps.Tests.csproj" />
  </Folder>
  <Folder Name="/samples/">
    <Project Path="samples/OrderSource/OrderSource.csproj" />
  </Folder>
</Solution>
```

## 19. backend/src/IntegraOps.Api/IntegraOps.Api.csproj

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <ItemGroup>
    <ProjectReference Include="../IntegraOps.Infrastructure/IntegraOps.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

## 20. backend/src/IntegraOps.Api/appsettings.json

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

## 21. backend/src/IntegraOps.Application/Contracts.cs

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```csharp
using IntegraOps.Domain;

namespace IntegraOps.Application;

public sealed record CreateProjectRequest(string OrganizationName, string ProjectName);
public sealed record ProjectCredentials(Guid OrganizationId, Guid ProjectId, string ApiKey);
public sealed record ProjectAccess(Guid OrganizationId, Guid ProjectId);
public sealed record CreateFlowStep(string Code, string Name, int? MaxDelaySeconds);
public sealed record CreateFlowRequest(string Code, string Name, IReadOnlyList<CreateFlowStep> Steps);
public sealed record SubmitEventRequest(
    Guid EventId, string Flow, string TransactionId, string Step,
    string Status, string OccurredAt, string? Error = null);
public sealed record ValidatedEvent(
    Guid EventId, string Flow, string TransactionId, string Step,
    EventStatus Status, DateTimeOffset OccurredAt, string? Error);
public sealed record EventReceipt(
    Guid EventId, Guid TransactionRecordId, bool Duplicate, DateTimeOffset AcceptedAt);
public sealed record DelayView(
    string State, string? Step, DateTimeOffset? StartedAt,
    DateTimeOffset? DeadlineAt, DateTimeOffset? CheckedAt);
public sealed record TransactionView(
    Guid Id, string Flow, string TransactionId, TransactionStatus Status,
    int AcceptedEventCount, int ProcessedEventCount,
    IReadOnlyList<TimelineStep> Steps, IReadOnlyList<ObservedEvent> Events,
    DelayView? Delay = null);

public interface ITrackingStore
{
    Task<ProjectCredentials> CreateProjectAsync(CreateProjectRequest request, CancellationToken ct);
    Task<ProjectAccess?> AuthenticateAsync(string rawKey, CancellationToken ct);
    Task<FlowDefinition> CreateFlowAsync(Guid projectId, CreateFlowRequest request, CancellationToken ct);
    Task<EventReceipt> AcceptAsync(Guid projectId, ValidatedEvent request, CancellationToken ct);
    Task<TransactionView?> GetTransactionAsync(Guid projectId, string flow, string transactionId, CancellationToken ct);
}

public sealed class RequestValidationException(Dictionary<string, string[]> errors)
    : Exception("İstek alanlarını kontrol edin.")
{
    public Dictionary<string, string[]> Errors { get; } = errors;
}

public sealed class ResourceNotFoundException(string message) : Exception(message);
public sealed class ResourceConflictException(string message) : Exception(message);
```

## 22. backend/src/IntegraOps.Application/FlowDetails.cs

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```csharp
using IntegraOps.Domain;

namespace IntegraOps.Application;

public sealed record FlowDetails(
    Guid Id, string Code, string Name, IReadOnlyList<FlowStep> Steps);

public interface IFlowQueries
{
    Task<FlowDetails?> GetAsync(Guid projectId, string flow, CancellationToken ct);
}

public static partial class InputValidation
{
    public static string FlowCode(string? flow)
    {
        var errors = new Dictionary<string, string[]>();
        Code(errors, "flow", flow);
        ThrowIfInvalid(errors);
        return flow!;
    }
}
```

## 23. backend/src/IntegraOps.Application/InputValidation.cs

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using IntegraOps.Domain;

namespace IntegraOps.Application;

public static partial class InputValidation
{
    [GeneratedRegex("^[a-z][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex TransactionPattern();

    [GeneratedRegex("^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}(?:\\.\\d{1,7})?(?:Z|[+-]\\d{2}:\\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampPattern();

    public static void Project(CreateProjectRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        Name(errors, "organizationName", request.OrganizationName);
        Name(errors, "projectName", request.ProjectName);
        ThrowIfInvalid(errors);
    }

    public static void Flow(CreateFlowRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        Code(errors, "code", request.Code);
        Name(errors, "name", request.Name);
        if (request.Steps is null || request.Steps.Count is < 2 or > 30)
            errors["steps"] = ["Akış 2–30 sıralı adımdan oluşmalı."];
        else
        {
            var codes = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < request.Steps.Count; i++)
            {
                var step = request.Steps[i];
                if (step is null) { errors[$"steps[{i}]"] = ["Adım boş olamaz."]; continue; }
                Code(errors, $"steps[{i}].code", step.Code);
                Name(errors, $"steps[{i}].name", step.Name);
                if (step.Code is not null && !codes.Add(step.Code))
                    errors[$"steps[{i}].code"] = ["Adım kodları farklı olmalı."];
                if (i == 0 && step.MaxDelaySeconds is not null)
                    errors[$"steps[{i}].maxDelaySeconds"] = ["Başlangıç adımının önceki adıma göre süresi yoktur."];
                if (i > 0 && (step.MaxDelaySeconds is null or < 1 or > 86400))
                    errors[$"steps[{i}].maxDelaySeconds"] = ["Önceki adımdan sonraki süre 1–86400 saniye olmalı."];
            }
        }
        ThrowIfInvalid(errors);
    }

    public static ValidatedEvent Event(SubmitEventRequest request, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.EventId == Guid.Empty) errors["eventId"] = ["Benzersiz bir UUID gerekli."];
        Code(errors, "flow", request.Flow);
        Code(errors, "step", request.Step);
        if (request.TransactionId is null || request.TransactionId.Length > 128 || !TransactionPattern().IsMatch(request.TransactionId))
            errors["transactionId"] = ["1–128 karakter; harf, rakam, nokta, alt çizgi, iki nokta veya tire kullanın."];
        EventStatus status = default;
        if (request.Status == "completed") status = EventStatus.Completed;
        else if (request.Status == "failed") status = EventStatus.Failed;
        else errors["status"] = ["completed veya failed gönderin."];

        DateTimeOffset occurredAt = default;
        if (request.OccurredAt is null || request.OccurredAt.Length > 40 ||
            !TimestampPattern().IsMatch(request.OccurredAt) ||
            !DateTimeOffset.TryParse(request.OccurredAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out occurredAt))
            errors["occurredAt"] = ["Saat dilimi içeren ISO 8601 zamanı gerekli; örnek: 2026-09-07T10:00:00Z."];
        else if (occurredAt > now.AddMinutes(5))
            errors["occurredAt"] = ["Olay zamanı sunucu saatinin 5 dakikadan fazla ilerisinde olamaz."];
        if (request.Error?.Length > 2000) errors["error"] = ["Hata açıklaması en fazla 2000 karakter olabilir."];
        if (request.Status == "completed" && !string.IsNullOrWhiteSpace(request.Error))
            errors["error"] = ["Tamamlanan olayda hata açıklaması gönderilmemeli."];
        ThrowIfInvalid(errors);
        return new ValidatedEvent(request.EventId, request.Flow!, request.TransactionId!, request.Step!,
            status, occurredAt.ToUniversalTime(), string.IsNullOrWhiteSpace(request.Error) ? null : request.Error);
    }

    private static void Code(Dictionary<string, string[]> errors, string field, string? value)
    {
        if (value is null || value.Length > 64 || !CodePattern().IsMatch(value))
            errors[field] = ["Küçük harfle başlayan, en fazla 64 karakterlik harf/rakam/tire kodu gerekli."];
    }

    private static void Name(Dictionary<string, string[]> errors, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 160)
            errors[field] = ["1–160 karakterlik ad gerekli."];
    }

    private static void ThrowIfInvalid(Dictionary<string, string[]> errors)
    {
        if (errors.Count > 0) throw new RequestValidationException(errors);
    }
}
```

## 24. backend/src/IntegraOps.Application/IntegraOps.Application.csproj

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="../IntegraOps.Domain/IntegraOps.Domain.csproj" />
  </ItemGroup>
</Project>
```

## 25. backend/src/IntegraOps.Application/PanelQueries.cs

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```csharp
namespace IntegraOps.Application;

public sealed record FlowListItem(
    Guid Id, string Code, string Name, int StepCount);

public sealed record TransactionSummary(
    long Total, long Pending, long InProgress,
    long Completed, long Failed, long Overdue,
    DateTimeOffset QueriedAt);

public interface IPanelQueries
{
    Task<IReadOnlyList<FlowListItem>> ListFlowsAsync(
        Guid projectId, CancellationToken ct);

    Task<TransactionSummary> GetSummaryAsync(
        Guid projectId, string? flow, CancellationToken ct);
}
```

## 26. backend/src/IntegraOps.Application/TransactionListing.cs

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```csharp
using IntegraOps.Domain;

namespace IntegraOps.Application;

public sealed record TransactionListQuery(
    string? Flow, TransactionStatus? Status, string? Delay,
    int Page, int PageSize);

public sealed record TransactionListItem(
    Guid Id, string Flow, string TransactionId, TransactionStatus Status,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DelayView Delay);

public sealed record TransactionPage(
    int Page, int PageSize, long TotalCount,
    IReadOnlyList<TransactionListItem> Items)
{
    public long TotalPages => TotalCount / PageSize
        + (TotalCount % PageSize == 0 ? 0 : 1);
    public bool HasNextPage => (long)Page * PageSize < TotalCount;
}

public interface ITransactionQueries
{
    Task<TransactionPage> ListAsync(
        Guid projectId, TransactionListQuery query, CancellationToken ct);
}

public static partial class InputValidation
{
    public static TransactionListQuery TransactionList(
        string? flow, string? status, string? delay,
        int page = 1, int pageSize = 20)
    {
        var errors = new Dictionary<string, string[]>();
        flow = string.IsNullOrWhiteSpace(flow) ? null : flow.Trim();
        if (flow is not null) Code(errors, "flow", flow);

        TransactionStatus? parsedStatus = status?.Trim().ToLowerInvariant() switch
        {
            null or "" => null,
            "pending" => TransactionStatus.Pending,
            "inprogress" => TransactionStatus.InProgress,
            "failed" => TransactionStatus.Failed,
            "completed" => TransactionStatus.Completed,
            _ => null
        };
        if (!string.IsNullOrWhiteSpace(status) && parsedStatus is null)
            errors["status"] = ["pending, inProgress, failed veya completed kullanın."];

        delay = string.IsNullOrWhiteSpace(delay) ? null : delay.Trim().ToLowerInvariant();
        if (delay is not (null or "none" or "waiting" or "overdue"
            or "pending_processing" or "pending_evaluation"))
            errors["delay"] = ["none, waiting, overdue, pending_processing veya pending_evaluation kullanın."];
        if (page < 1) errors["page"] = ["Sayfa en az 1 olmalı."];
        if (pageSize is < 1 or > 100) errors["pageSize"] = ["Sayfa boyutu 1–100 arasında olmalı."];

        ThrowIfInvalid(errors);
        return new TransactionListQuery(flow, parsedStatus, delay, page, pageSize);
    }
}
```

## 27. backend/src/IntegraOps.Domain/DelayEvaluator.cs

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```csharp
namespace IntegraOps.Domain;

public sealed record DelayAssessment(
    string State, string? Step, DateTimeOffset? StartedAt, DateTimeOffset? DeadlineAt);

public static class DelayEvaluator
{
    public static DelayAssessment Evaluate(Projection projection, DateTimeOffset now)
    {
        var none = new DelayAssessment("none", null, null, null);
        // An explicit failure already has its own status. A delay means an expected
        // completion has not been observed, not that the external system failed.
        if (projection.Status != TransactionStatus.InProgress) return none;

        var steps = projection.Steps.OrderBy(step => step.Position).ToArray();
        var nextIndex = Array.FindIndex(steps, step => step.Observation != "completed");
        // We cannot time the first step: there is no preceding completion to anchor it.
        if (nextIndex <= 0) return none;

        var next = steps[nextIndex];
        var previous = steps[nextIndex - 1];
        if (next.Observation != "not_observed" || previous.Observation != "completed"
            || previous.OccurredAt is not { } startedAt || next.MaxDelaySeconds is not { } seconds)
            return none;

        // The expected step owns the allowance; business event time starts the clock.
        var deadlineAt = startedAt.AddSeconds(seconds);
        return new DelayAssessment(now >= deadlineAt ? "overdue" : "waiting",
            next.Code, startedAt, deadlineAt);
    }
}
```

## 28. backend/src/IntegraOps.Domain/IntegraOps.Domain.csproj

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```xml
<Project Sdk="Microsoft.NET.Sdk" />
```

## 29. backend/src/IntegraOps.Domain/Tracking.cs

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```csharp
namespace IntegraOps.Domain;

public enum EventStatus { Completed, Failed }
public enum TransactionStatus { Pending, InProgress, Failed, Completed }

// Flow definitions are immutable in the first milestone. Editing/versioning is a later feature.
public sealed record FlowStep(string Code, string Name, int Position, int? MaxDelaySeconds);
public sealed record FlowDefinition(
    Guid Id, Guid ProjectId, string Code, string Name, IReadOnlyList<FlowStep> Steps);

public sealed record ObservedEvent(
    Guid EventId, string Step, EventStatus Status, DateTimeOffset OccurredAt,
    DateTimeOffset ReceivedAt, string? Error);

public sealed record TimelineStep(
    string Code, string Name, int Position, int? MaxDelaySeconds,
    string Observation, DateTimeOffset? OccurredAt, string? Error);

public sealed record Projection(TransactionStatus Status, IReadOnlyList<TimelineStep> Steps);

public static class TimelineProjector
{
    public static Projection Project(FlowDefinition flow, IReadOnlyList<ObservedEvent> events)
    {
        // Delivery order is not business order. EventId breaks exact timestamp ties deterministically.
        var latestByStep = events
            .GroupBy(e => e.Step, StringComparer.Ordinal)
            .ToDictionary(g => g.Key,
                g => g.OrderBy(e => e.OccurredAt).ThenBy(e => e.EventId).Last(),
                StringComparer.Ordinal);

        var steps = flow.Steps.OrderBy(s => s.Position).Select(step =>
        {
            latestByStep.TryGetValue(step.Code, out var observed);
            return new TimelineStep(step.Code, step.Name, step.Position, step.MaxDelaySeconds,
                observed is null ? "not_observed" : observed.Status == EventStatus.Completed ? "completed" : "failed",
                observed?.OccurredAt, observed?.Error);
        }).ToArray();

        var status = events.Count == 0 ? TransactionStatus.Pending
            : steps.Any(s => s.Observation == "failed") ? TransactionStatus.Failed
            : steps.All(s => s.Observation == "completed") ? TransactionStatus.Completed
            : TransactionStatus.InProgress;

        return new Projection(status, steps);
    }
}
```

## 30. backend/src/IntegraOps.Infrastructure/Messaging.cs

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```csharp
using System.Text.Json;
using Npgsql;
using RabbitMQ.Client;

namespace IntegraOps.Infrastructure;

public sealed record EventPointer(Guid ProjectId, Guid EventId);

public static class Messaging
{
    public const string Queue = "integraops.events.v1";
    public const string DeadLetterQueue = "integraops.events.dead.v1";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task DeclareAsync(IChannel channel, CancellationToken ct)
    {
        await channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: null, cancellationToken: ct);
        await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = "",
                ["x-dead-letter-routing-key"] = DeadLetterQueue
            }, cancellationToken: ct);
    }
}

public sealed class OutboxDispatcher(NpgsqlDataSource dataSource)
{
    public async Task<bool> PublishNextAsync(IChannel channel, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        long id;
        EventPointer pointer;
        await using (var select = Sql.Command(connection, transaction, """
            SELECT id, project_id, event_id FROM outbox_messages
            WHERE published_at IS NULL ORDER BY id LIMIT 1 FOR UPDATE SKIP LOCKED
            """))
        await using (var reader = await select.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return false;
            id = reader.GetInt64(0);
            pointer = new EventPointer(reader.GetGuid(1), reader.GetGuid(2));
        }

        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = $"{pointer.ProjectId}:{pointer.EventId}"
        };
        // This channel must have publisher confirmations + confirmation tracking enabled.
        // The await fails if the broker rejects or returns the mandatory message.
        await channel.BasicPublishAsync(exchange: "", routingKey: Messaging.Queue, mandatory: true,
            basicProperties: properties, body: JsonSerializer.SerializeToUtf8Bytes(pointer, Messaging.Json),
            cancellationToken: ct);

        await using var mark = Sql.Command(connection, transaction,
            "UPDATE outbox_messages SET published_at = clock_timestamp() WHERE id = @id", ("id", id));
        await mark.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }
}
```

## 31. backend/src/IntegraOps.Infrastructure/Migrations/001_initial.sql

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```sql
CREATE TABLE organizations (
    id uuid PRIMARY KEY,
    name varchar(160) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE projects (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES organizations(id),
    name varchar(160) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE api_keys (
    id uuid PRIMARY KEY,
    project_id uuid NOT NULL REFERENCES projects(id),
    key_hash varchar(64) NOT NULL UNIQUE,
    key_prefix varchar(16) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    revoked_at timestamptz
);

CREATE TABLE flows (
    id uuid PRIMARY KEY,
    project_id uuid NOT NULL REFERENCES projects(id),
    code varchar(64) NOT NULL,
    name varchar(160) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (project_id, code),
    UNIQUE (project_id, id)
);

CREATE TABLE flow_steps (
    flow_id uuid NOT NULL REFERENCES flows(id),
    code varchar(64) NOT NULL,
    name varchar(160) NOT NULL,
    position smallint NOT NULL CHECK (position BETWEEN 0 AND 29),
    max_delay_seconds integer,
    PRIMARY KEY (flow_id, code),
    UNIQUE (flow_id, position),
    CHECK ((position = 0 AND max_delay_seconds IS NULL) OR
           (position > 0 AND max_delay_seconds IS NOT NULL AND max_delay_seconds BETWEEN 1 AND 86400))
);

CREATE TABLE transactions (
    id uuid PRIMARY KEY,
    project_id uuid NOT NULL,
    flow_id uuid NOT NULL,
    business_id varchar(128) NOT NULL,
    status varchar(20) NOT NULL DEFAULT 'Pending'
        CHECK (status IN ('Pending', 'InProgress', 'Failed', 'Completed')),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (project_id, flow_id, business_id),
    UNIQUE (project_id, flow_id, id),
    FOREIGN KEY (project_id, flow_id) REFERENCES flows(project_id, id)
);

CREATE TABLE transaction_events (
    project_id uuid NOT NULL,
    event_id uuid NOT NULL,
    transaction_id uuid NOT NULL,
    flow_id uuid NOT NULL,
    step_code varchar(64) NOT NULL,
    status varchar(16) NOT NULL CHECK (status IN ('Completed', 'Failed')),
    occurred_at timestamptz NOT NULL,
    received_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    processed_at timestamptz,
    error varchar(2000),
    fingerprint varchar(64) NOT NULL,
    PRIMARY KEY (project_id, event_id),
    FOREIGN KEY (project_id, flow_id, transaction_id) REFERENCES transactions(project_id, flow_id, id),
    FOREIGN KEY (flow_id, step_code) REFERENCES flow_steps(flow_id, code)
);

CREATE INDEX ix_events_timeline ON transaction_events (transaction_id, occurred_at, event_id);
CREATE INDEX ix_transactions_project_status ON transactions (project_id, status, updated_at DESC);

CREATE TABLE outbox_messages (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    project_id uuid NOT NULL,
    event_id uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    published_at timestamptz,
    UNIQUE (project_id, event_id),
    FOREIGN KEY (project_id, event_id) REFERENCES transaction_events(project_id, event_id)
);

CREATE INDEX ix_outbox_pending ON outbox_messages (id) WHERE published_at IS NULL;
```

## 32. backend/src/IntegraOps.Infrastructure/Migrations/002_transaction_delays.sql

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```sql
-- One current expectation per transaction. Existing business records are untouched.
CREATE TABLE transaction_delays (
    transaction_id uuid PRIMARY KEY,
    project_id uuid NOT NULL,
    flow_id uuid NOT NULL,
    state varchar(24) NOT NULL CHECK (state IN ('none', 'waiting', 'overdue', 'pending_processing')),
    step_code varchar(64),
    started_at timestamptz,
    deadline_at timestamptz,
    checked_at timestamptz NOT NULL,
    FOREIGN KEY (project_id, flow_id, transaction_id) REFERENCES transactions(project_id, flow_id, id),
    FOREIGN KEY (flow_id, step_code) REFERENCES flow_steps(flow_id, code),
    CHECK (
        (state IN ('waiting', 'overdue') AND step_code IS NOT NULL
            AND started_at IS NOT NULL AND deadline_at IS NOT NULL AND deadline_at > started_at)
        OR (state IN ('none', 'pending_processing') AND step_code IS NULL
            AND started_at IS NULL AND deadline_at IS NULL)
    )
);

CREATE INDEX ix_delays_waiting ON transaction_delays(deadline_at, transaction_id)
    WHERE state = 'waiting';
CREATE INDEX ix_events_unprocessed ON transaction_events(transaction_id)
    WHERE processed_at IS NULL;
```

## 33. backend/src/IntegraOps.Infrastructure/PostgresFlowQueries.cs

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```csharp
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
```

## 34. backend/src/IntegraOps.Infrastructure/PostgresPanelQueries.cs

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```csharp
using IntegraOps.Application;
using Npgsql;

namespace IntegraOps.Infrastructure;

public sealed class PostgresPanelQueries(NpgsqlDataSource dataSource) : IPanelQueries
{
    public async Task<IReadOnlyList<FlowListItem>> ListFlowsAsync(
        Guid projectId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = Sql.Command(connection, null, """
            SELECT f.id, f.code, f.name, count(s.code)::integer AS step_count
            FROM flows f
            LEFT JOIN flow_steps s ON s.flow_id = f.id
            WHERE f.project_id = @projectId
            GROUP BY f.id, f.code, f.name
            ORDER BY f.name, f.code
            """, ("projectId", projectId));

        var items = new List<FlowListItem>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            items.Add(new FlowListItem(
                reader.GetGuid(0), reader.GetString(1),
                reader.GetString(2), reader.GetInt32(3)));
        }
        return items;
    }

    public async Task<TransactionSummary> GetSummaryAsync(
        Guid projectId, string? flow, CancellationToken ct)
    {
        var query = InputValidation.TransactionList(flow, null, null);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        // Reuse the list's project scope and effective delay classification.
        // One aggregate query counts every matching transaction, with no pagination.
        await using var command = Sql.Command(connection, null,
            PostgresTransactionQueries.FilteredSql + """

            SELECT
                count(*) AS total,
                count(*) FILTER (WHERE status = 'Pending') AS pending,
                count(*) FILTER (WHERE status = 'InProgress') AS in_progress,
                count(*) FILTER (WHERE status = 'Completed') AS completed,
                count(*) FILTER (WHERE status = 'Failed') AS failed,
                count(*) FILTER (WHERE delay_state = 'overdue') AS overdue,
                statement_timestamp() AS queried_at
            FROM filtered
            """,
            ("projectId", projectId), ("flow", query.Flow),
            ("status", null), ("delay", null));

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException("İşlem özeti okunamadı.");

        return new TransactionSummary(
            reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2),
            reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5),
            new DateTimeOffset(reader.GetDateTime(6)));
    }
}
```

## 35. backend/src/IntegraOps.Infrastructure/PostgresTrackingStore.cs

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```csharp
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntegraOps.Application;
using IntegraOps.Domain;
using Npgsql;

namespace IntegraOps.Infrastructure;

public sealed class PostgresTrackingStore(NpgsqlDataSource dataSource) : ITrackingStore
{
    public async Task<ProjectCredentials> CreateProjectAsync(CreateProjectRequest request, CancellationToken ct)
    {
        InputValidation.Project(request);
        var organizationId = Guid.CreateVersion7();
        var projectId = Guid.CreateVersion7();
        var rawKey = "io_dev_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = Sql.Command(connection, transaction, """
            INSERT INTO organizations (id, name) VALUES (@organizationId, @organizationName);
            INSERT INTO projects (id, organization_id, name) VALUES (@projectId, @organizationId, @projectName);
            INSERT INTO api_keys (id, project_id, key_hash, key_prefix) VALUES (@keyId, @projectId, @hash, @prefix);
            """, ("organizationId", organizationId), ("organizationName", request.OrganizationName.Trim()),
            ("projectId", projectId), ("projectName", request.ProjectName.Trim()),
            ("keyId", Guid.CreateVersion7()), ("hash", Hash(rawKey)), ("prefix", rawKey[..16]));
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return new ProjectCredentials(organizationId, projectId, rawKey);
    }

    public async Task<ProjectAccess?> AuthenticateAsync(string rawKey, CancellationToken ct)
    {
        if (rawKey.Length is < 32 or > 128) return null;
        await using var command = dataSource.CreateCommand("""
            SELECT p.organization_id, p.id FROM api_keys k JOIN projects p ON p.id = k.project_id
            WHERE k.key_hash = @hash AND k.revoked_at IS NULL
            """);
        command.Parameters.AddWithValue("hash", Hash(rawKey));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new ProjectAccess(reader.GetGuid(0), reader.GetGuid(1)) : null;
    }

    public async Task<FlowDefinition> CreateFlowAsync(Guid projectId, CreateFlowRequest request, CancellationToken ct)
    {
        InputValidation.Flow(request);
        var flowId = Guid.CreateVersion7();
        var steps = request.Steps.Select((s, i) => new FlowStep(s.Code, s.Name.Trim(), i, s.MaxDelaySeconds)).ToArray();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        try
        {
            await using (var insert = Sql.Command(connection, transaction, """
                INSERT INTO flows (id, project_id, code, name) VALUES (@id, @projectId, @code, @name)
                """, ("id", flowId), ("projectId", projectId), ("code", request.Code), ("name", request.Name.Trim())))
                await insert.ExecuteNonQueryAsync(ct);
            foreach (var step in steps)
            {
                await using var insert = Sql.Command(connection, transaction, """
                    INSERT INTO flow_steps (flow_id, code, name, position, max_delay_seconds)
                    VALUES (@flowId, @code, @name, @position, CAST(@delay AS integer))
                    """, ("flowId", flowId), ("code", step.Code), ("name", step.Name),
                    ("position", step.Position), ("delay", step.MaxDelaySeconds));
                await insert.ExecuteNonQueryAsync(ct);
            }
            await transaction.CommitAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ResourceConflictException("Bu projede aynı kodla bir akış zaten var.");
        }
        return new FlowDefinition(flowId, projectId, request.Code, request.Name.Trim(), steps);
    }

    public async Task<EventReceipt> AcceptAsync(Guid projectId, ValidatedEvent request, CancellationToken ct)
    {
        var fingerprint = Hash(JsonSerializer.Serialize(request));
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var flow = await LoadFlowAsync(connection, transaction, projectId, request.Flow, null, ct)
            ?? throw new ResourceNotFoundException("Akış bu projede bulunamadı.");
        if (!flow.Steps.Any(s => s.Code == request.Step))
            throw new RequestValidationException(new() { ["step"] = ["Bu adım akışta tanımlı değil."] });

        // The uniqueness constraint serializes simultaneous creation of the same business transaction.
        await using (var insert = Sql.Command(connection, transaction, """
            INSERT INTO transactions (id, project_id, flow_id, business_id)
            VALUES (@id, @projectId, @flowId, @businessId)
            ON CONFLICT (project_id, flow_id, business_id) DO NOTHING
            """, ("id", Guid.CreateVersion7()), ("projectId", projectId),
            ("flowId", flow.Id), ("businessId", request.TransactionId)))
            await insert.ExecuteNonQueryAsync(ct);

        Guid transactionId;
        await using (var find = Sql.Command(connection, transaction, """
            SELECT id FROM transactions WHERE project_id = @projectId AND flow_id = @flowId AND business_id = @businessId
            """, ("projectId", projectId), ("flowId", flow.Id), ("businessId", request.TransactionId)))
            transactionId = (Guid)(await find.ExecuteScalarAsync(ct))!;

        object? acceptedAt;
        await using (var insert = Sql.Command(connection, transaction, """
            INSERT INTO transaction_events
                (project_id, event_id, transaction_id, flow_id, step_code, status, occurred_at, error, fingerprint)
            VALUES (@projectId, @eventId, @transactionId, @flowId, @step, @status, @occurredAt, @error, @fingerprint)
            ON CONFLICT (project_id, event_id) DO NOTHING RETURNING received_at
            """, ("projectId", projectId), ("eventId", request.EventId), ("transactionId", transactionId),
            ("flowId", flow.Id), ("step", request.Step), ("status", request.Status.ToString()),
            ("occurredAt", request.OccurredAt), ("error", request.Error), ("fingerprint", fingerprint)))
            acceptedAt = await insert.ExecuteScalarAsync(ct);

        if (acceptedAt is null)
        {
            await using var find = Sql.Command(connection, transaction, """
                SELECT fingerprint, transaction_id, received_at FROM transaction_events
                WHERE project_id = @projectId AND event_id = @eventId
                """, ("projectId", projectId), ("eventId", request.EventId));
            EventReceipt receipt;
            await using (var reader = await find.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Tekrarlanan olay kaydı bulunamadı.");
                if (reader.GetString(0) != fingerprint)
                    throw new ResourceConflictException("Aynı eventId daha önce farklı içerikle kullanılmış.");
                receipt = new EventReceipt(request.EventId, reader.GetGuid(1), true,
                    new DateTimeOffset(reader.GetDateTime(2)));
            }
            await transaction.CommitAsync(ct);
            return receipt;
        }

        // Event and outbox are committed together. No RabbitMQ connection is needed on this path.
        await using (var outbox = Sql.Command(connection, transaction,
            "INSERT INTO outbox_messages (project_id, event_id) VALUES (@projectId, @eventId)",
            ("projectId", projectId), ("eventId", request.EventId)))
            await outbox.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return new EventReceipt(request.EventId, transactionId, false, new DateTimeOffset((DateTime)acceptedAt));
    }

    public async Task<TransactionView?> GetTransactionAsync(Guid projectId, string flowCode, string businessId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        // Keep the accepted count, processed events and flow definition in one consistent snapshot.
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var flow = await LoadFlowAsync(connection, transaction, projectId, flowCode, null, ct);
        if (flow is null) return null;
        await using var find = Sql.Command(connection, transaction, """
            SELECT id FROM transactions WHERE project_id = @projectId AND flow_id = @flowId AND business_id = @businessId
            """, ("projectId", projectId), ("flowId", flow.Id), ("businessId", businessId));
        var foundId = await find.ExecuteScalarAsync(ct);
        if (foundId is not Guid id) return null;
        var events = await LoadEventsAsync(connection, transaction, projectId, id, ct);
        await using var count = Sql.Command(connection, transaction,
            "SELECT count(*) FROM transaction_events WHERE project_id = @projectId AND transaction_id = @id",
            ("projectId", projectId), ("id", id));
        var acceptedCount = checked((int)(long)(await count.ExecuteScalarAsync(ct))!);
        var projection = TimelineProjector.Project(flow, events);
        var delay = await PostgresDelayStore.ReadAsync(connection, transaction, projectId, id,
            projection, acceptedCount > events.Count, ct);
        await transaction.CommitAsync(ct);
        return new TransactionView(id, flow.Code, businessId, projection.Status,
            acceptedCount, events.Count, projection.Steps, events, delay);
    }

    public async Task ProcessAsync(EventPointer pointer, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        Guid transactionId;
        Guid flowId;
        await using (var find = Sql.Command(connection, transaction, """
            SELECT t.id, t.flow_id FROM transactions t
            JOIN transaction_events e ON e.transaction_id = t.id AND e.project_id = t.project_id
            WHERE e.project_id = @projectId AND e.event_id = @eventId FOR UPDATE OF t
            """, ("projectId", pointer.ProjectId), ("eventId", pointer.EventId)))
        await using (var reader = await find.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw new InvalidDataException("Kuyrukta karşılığı olmayan olay kimliği var.");
            transactionId = reader.GetGuid(0);
            flowId = reader.GetGuid(1);
        }

        await using (var apply = Sql.Command(connection, transaction, """
            UPDATE transaction_events SET processed_at = clock_timestamp()
            WHERE project_id = @projectId AND event_id = @eventId AND processed_at IS NULL
            """, ("projectId", pointer.ProjectId), ("eventId", pointer.EventId)))
        {
            if (await apply.ExecuteNonQueryAsync(ct) == 0)
            {
                await transaction.CommitAsync(ct);
                return; // Publisher/consumer redelivery is harmless.
            }
        }
        var flow = await LoadFlowAsync(connection, transaction, pointer.ProjectId, null, flowId, ct)
            ?? throw new InvalidDataException("Akış kaydı bulunamadı.");
        var events = await LoadEventsAsync(connection, transaction, pointer.ProjectId, transactionId, ct);
        var projection = TimelineProjector.Project(flow, events);
        await using (var update = Sql.Command(connection, transaction, """
            UPDATE transactions SET status = @status, updated_at = clock_timestamp()
            WHERE id = @id AND project_id = @projectId
            """, ("status", projection.Status.ToString()), ("id", transactionId), ("projectId", pointer.ProjectId)))
            await update.ExecuteNonQueryAsync(ct);
        await PostgresDelayStore.RefreshAsync(connection, transaction, flow, transactionId, projection, ct);
        await transaction.CommitAsync(ct);
    }

    internal static async Task<FlowDefinition?> LoadFlowAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid projectId, string? code, Guid? flowId, CancellationToken ct)
    {
        await using var find = Sql.Command(connection, transaction, """
            SELECT id, code, name FROM flows WHERE project_id = @projectId
            AND (code = @code OR id = CAST(@flowId AS uuid))
            """, ("projectId", projectId), ("code", code), ("flowId", flowId));
        Guid id;
        string name;
        string foundCode;
        await using (var reader = await find.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return null;
            id = reader.GetGuid(0); foundCode = reader.GetString(1); name = reader.GetString(2);
        }
        await using var stepsCommand = Sql.Command(connection, transaction, """
            SELECT code, name, position, max_delay_seconds FROM flow_steps WHERE flow_id = @id ORDER BY position
            """, ("id", id));
        var steps = new List<FlowStep>();
        await using (var reader = await stepsCommand.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                steps.Add(new FlowStep(reader.GetString(0), reader.GetString(1), reader.GetInt16(2),
                    reader.IsDBNull(3) ? null : reader.GetInt32(3)));
        }
        return new FlowDefinition(id, projectId, foundCode, name, steps);
    }

    internal static async Task<List<ObservedEvent>> LoadEventsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid projectId, Guid transactionId, CancellationToken ct)
    {
        await using var find = Sql.Command(connection, transaction, """
            SELECT event_id, step_code, status, occurred_at, received_at, error FROM transaction_events
            WHERE project_id = @projectId AND transaction_id = @id AND processed_at IS NOT NULL
            ORDER BY occurred_at, event_id
            """, ("projectId", projectId), ("id", transactionId));
        var events = new List<ObservedEvent>();
        await using var reader = await find.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            events.Add(new ObservedEvent(reader.GetGuid(0), reader.GetString(1), Enum.Parse<EventStatus>(reader.GetString(2)),
                new DateTimeOffset(reader.GetDateTime(3)), new DateTimeOffset(reader.GetDateTime(4)),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        return events;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
```

## 36. backend/src/IntegraOps.Infrastructure/PostgresTransactionQueries.cs

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```csharp
using System.Data;
using IntegraOps.Application;
using IntegraOps.Domain;
using Npgsql;

namespace IntegraOps.Infrastructure;

public sealed class PostgresTransactionQueries(NpgsqlDataSource dataSource)
    : ITransactionQueries
{
    internal const string FilteredSql = """
        WITH candidates AS (
            SELECT t.id, f.code AS flow, t.business_id, t.status,
                   t.created_at, t.updated_at,
                   d.step_code, d.started_at, d.deadline_at, d.checked_at,
                   CASE
                       WHEN EXISTS (
                           SELECT 1 FROM transaction_events e
                           WHERE e.project_id = t.project_id
                             AND e.transaction_id = t.id
                             AND e.processed_at IS NULL
                       ) THEN 'pending_processing'
                       WHEN d.transaction_id IS NULL OR t.updated_at > d.checked_at
                           THEN CASE WHEN t.status = 'InProgress'
                               THEN 'pending_evaluation' ELSE 'none' END
                       ELSE d.state
                   END AS delay_state
            FROM transactions t
            JOIN flows f ON f.id = t.flow_id AND f.project_id = t.project_id
            LEFT JOIN transaction_delays d
                ON d.transaction_id = t.id AND d.project_id = t.project_id
            WHERE t.project_id = @projectId
              AND (CAST(@flow AS text) IS NULL OR f.code = @flow)
              AND (CAST(@status AS text) IS NULL OR t.status = @status)
        ), filtered AS (
            SELECT * FROM candidates
            WHERE CAST(@delay AS text) IS NULL OR delay_state = @delay
        )
        """;

    public async Task<TransactionPage> ListAsync(
        Guid projectId, TransactionListQuery query, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        // Count and rows must see the same state even if the worker changes data.
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, ct);

        long totalCount;
        await using (var count = Command(connection, transaction, projectId, query,
            FilteredSql + " SELECT count(*) FROM filtered"))
        {
            totalCount = (long)(await count.ExecuteScalarAsync(ct))!;
        }

        var items = new List<TransactionListItem>();
        await using (var find = Command(connection, transaction, projectId, query,
            FilteredSql + """

            SELECT id, flow, business_id, status, created_at, updated_at,
                   delay_state, step_code, started_at, deadline_at, checked_at
            FROM filtered
            ORDER BY created_at DESC, id DESC
            LIMIT @limit OFFSET @offset
            """))
        {
            find.Parameters.AddWithValue("limit", query.PageSize);
            find.Parameters.AddWithValue("offset", ((long)query.Page - 1) * query.PageSize);
            await using var reader = await find.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var updatedAt = new DateTimeOffset(reader.GetDateTime(5));
                var state = reader.GetString(6);
                var checkedAt = ReadTime(reader, 10);
                var current = state is not ("pending_processing" or "pending_evaluation")
                    && checkedAt is not null && updatedAt <= checkedAt.Value;
                var delay = new DelayView(
                    state,
                    current && !reader.IsDBNull(7) ? reader.GetString(7) : null,
                    current ? ReadTime(reader, 8) : null,
                    current ? ReadTime(reader, 9) : null,
                    current ? checkedAt : null);
                items.Add(new TransactionListItem(
                    reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                    Enum.Parse<TransactionStatus>(reader.GetString(3)),
                    new DateTimeOffset(reader.GetDateTime(4)), updatedAt, delay));
            }
        }

        await transaction.CommitAsync(ct);
        return new TransactionPage(query.Page, query.PageSize, totalCount, items);
    }

    private static NpgsqlCommand Command(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid projectId, TransactionListQuery query, string sql) =>
        Sql.Command(connection, transaction, sql,
            ("projectId", projectId), ("flow", query.Flow),
            ("status", query.Status?.ToString()), ("delay", query.Delay));

    private static DateTimeOffset? ReadTime(NpgsqlDataReader reader, int column) =>
        reader.IsDBNull(column) ? null : new DateTimeOffset(reader.GetDateTime(column));
}
```

## 37. backend/src/IntegraOps.Infrastructure/Sql.cs

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```csharp
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
```

## 38. backend/src/IntegraOps.Worker/DelayMonitor.cs

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```csharp
using IntegraOps.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IntegraOps.Worker;

public sealed class DelayMonitor(PostgresDelayStore store, IConfiguration configuration,
    ILogger<DelayMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // This interval is how often we check, not how long any invoice may take.
        var interval = configuration.GetValue("DelayMonitor:PollIntervalSeconds", 5);
        var batchSize = configuration.GetValue("DelayMonitor:BatchSize", 100);
        if (interval is < 1 or > 60 || batchSize is < 1 or > 500)
            throw new InvalidOperationException("Gecikme tarama aralığı 1–60 saniye, parti boyutu 1–500 olmalı.");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(interval));
        try
        {
            do
            {
                try
                {
                    int count;
                    do
                    {
                        count = await store.CheckDueAsync(batchSize, stoppingToken);
                    } while (count == batchSize && !stoppingToken.IsCancellationRequested);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    // Do not stop event consumption because one delay scan failed.
                    logger.LogWarning("Gecikme kontrolü tamamlanamadı ({ErrorType}); yeniden denenecek.",
                        ex.GetType().Name);
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
```

## 39. backend/src/IntegraOps.Worker/EventConsumer.cs

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```csharp
using System.Text.Json;
using IntegraOps.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace IntegraOps.Worker;

public sealed class EventConsumer(ConnectionFactory factory, PostgresTrackingStore store,
    ILogger<EventConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await factory.CreateConnectionAsync(stoppingToken);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                await Messaging.DeclareAsync(channel, stoppingToken);
                await channel.BasicQosAsync(0, 16, false, stoppingToken);
                var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                channel.ChannelShutdownAsync += (_, _) => { disconnected.TrySetResult(); return Task.CompletedTask; };
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.UnregisteredAsync += (_, _) => { disconnected.TrySetResult(); return Task.CompletedTask; };
                consumer.ReceivedAsync += async (_, delivery) =>
                {
                    try
                    {
                        // Deserialize while the RabbitMQ-owned memory is still valid.
                        var pointer = JsonSerializer.Deserialize<EventPointer>(delivery.Body.Span, Messaging.Json);
                        if (pointer is null || pointer.ProjectId == Guid.Empty || pointer.EventId == Guid.Empty)
                            throw new InvalidDataException("Geçersiz olay işaretçisi.");
                        await store.ProcessAsync(pointer, stoppingToken);
                        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                    }
                    catch (Exception ex) when (ex is JsonException or InvalidDataException)
                    {
                        logger.LogError("Geçersiz kuyruk kaydı inceleme kuyruğuna ayrıldı ({ErrorType}).", ex.GetType().Name);
                        await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
                    catch (Exception ex)
                    {
                        logger.LogWarning("Olay işlenemedi ({ErrorType}); bağlantı yenilenip tekrar denenecek.", ex.GetType().Name);
                        // Leave the delivery unacknowledged; closing this channel safely requeues it.
                        disconnected.TrySetResult();
                    }
                };
                await channel.BasicConsumeAsync(Messaging.Queue, autoAck: false, consumer, stoppingToken);
                logger.LogInformation("Olay işleyicisi hazır.");
                await disconnected.Task.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("Olay işleyicisi bağlantı bekliyor ({ErrorType}).", ex.GetType().Name);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
```

## 40. backend/src/IntegraOps.Worker/IntegraOps.Worker.csproj

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <ProjectReference Include="../IntegraOps.Infrastructure/IntegraOps.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

## 41. backend/src/IntegraOps.Worker/OutboxPublisher.cs

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```csharp
using IntegraOps.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace IntegraOps.Worker;

public sealed class OutboxPublisher(ConnectionFactory factory, OutboxDispatcher dispatcher,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await factory.CreateConnectionAsync(stoppingToken);
                await using var channel = await connection.CreateChannelAsync(
                    new CreateChannelOptions(publisherConfirmationsEnabled: true,
                        publisherConfirmationTrackingEnabled: true), stoppingToken);
                await Messaging.DeclareAsync(channel, stoppingToken);
                logger.LogInformation("Olay aktarımı hazır.");
                while (!stoppingToken.IsCancellationRequested && channel.IsOpen)
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    deadline.CancelAfter(TimeSpan.FromSeconds(10));
                    if (!await dispatcher.PublishNextAsync(channel, deadline.Token))
                        await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // Do not log connection URIs, event bodies or database parameter values.
                logger.LogWarning("Olay aktarımı bekliyor ({ErrorType}); kalıcı kayıtlar tekrar denenecek.", ex.GetType().Name);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
```

## 42. backend/tests/IntegraOps.Tests/DelayTests.cs

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```csharp
using IntegraOps.Domain;
using Xunit;

namespace IntegraOps.Tests;

public sealed class DelayTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);
    private static FlowDefinition Flow(int invoiceSeconds = 600) => new(Guid.NewGuid(), Guid.NewGuid(),
        "invoice-flow", "Fatura akışı", [
            new("erp-completed", "ERP", 0, null),
            new("approval-completed", "Onay", 1, 3600),
            new("invoice-created", "Fatura", 2, invoiceSeconds),
            new("cargo-created", "Kargo", 3, 7200)]);

    private static ObservedEvent Event(string step, int seconds = 0, EventStatus status = EventStatus.Completed) =>
        new(Guid.NewGuid(), step, status, Start.AddSeconds(seconds), Start.AddSeconds(seconds + 2),
            status == EventStatus.Failed ? "Dış sistem hata bildirdi." : null);

    private static DelayAssessment Check(int nowSeconds, params ObservedEvent[] events) =>
        DelayEvaluator.Evaluate(TimelineProjector.Project(Flow(), events), Start.AddSeconds(nowSeconds));

    [Fact]
    public void Approval_uses_its_own_hour_allowance()
    {
        var result = Check(600, Event("erp-completed"));
        Assert.Equal("waiting", result.State);
        Assert.Equal("approval-completed", result.Step);
        Assert.Equal(Start.AddHours(1), result.DeadlineAt);
    }

    [Theory]
    [InlineData(899, "waiting")]
    [InlineData(900, "overdue")]
    [InlineData(901, "overdue")]
    public void Invoice_boundary_starts_at_approval_not_erp(int nowSeconds, string state)
    {
        var result = Check(nowSeconds, Event("erp-completed"), Event("approval-completed", 300));
        Assert.Equal(state, result.State);
        Assert.Equal("invoice-created", result.Step);
        Assert.Equal(Start.AddMinutes(5), result.StartedAt);
        Assert.Equal(Start.AddMinutes(15), result.DeadlineAt);
    }

    [Fact]
    public void Different_flows_can_allow_different_invoice_times()
    {
        ObservedEvent[] events = [Event("erp-completed"), Event("approval-completed", 300)];
        var now = Start.AddMinutes(20);
        var fast = DelayEvaluator.Evaluate(TimelineProjector.Project(Flow(600), events), now);
        var slow = DelayEvaluator.Evaluate(TimelineProjector.Project(Flow(7200), events), now);
        Assert.Equal("overdue", fast.State);
        Assert.Equal("waiting", slow.State);
    }

    [Fact]
    public void No_events_has_no_timer() => Assert.Equal("none", Check(9999).State);

    [Fact]
    public void Missing_first_step_has_no_invented_start_time() =>
        Assert.Equal("none", Check(9999, Event("invoice-created", 600)).State);

    [Fact]
    public void Later_step_does_not_hide_an_earlier_gap()
    {
        var result = Check(3600, Event("erp-completed"), Event("invoice-created", 600));
        Assert.Equal("overdue", result.State);
        Assert.Equal("approval-completed", result.Step);
    }

    [Fact]
    public void Explicit_failure_is_not_classified_as_delay()
    {
        var events = new[] { Event("erp-completed"), Event("approval-completed", 20, EventStatus.Failed) };
        var projection = TimelineProjector.Project(Flow(), events);
        Assert.Equal(TransactionStatus.Failed, projection.Status);
        Assert.Equal("none", DelayEvaluator.Evaluate(projection, Start.AddHours(2)).State);
    }

    [Fact]
    public void Late_invoice_moves_expectation_to_cargo()
    {
        var result = Check(1300, Event("erp-completed"), Event("approval-completed", 300),
            Event("invoice-created", 1200));
        Assert.Equal("waiting", result.State);
        Assert.Equal("cargo-created", result.Step);
        Assert.Equal(Start.AddSeconds(8400), result.DeadlineAt);
    }

    [Fact]
    public void Complete_flow_has_no_current_delay_even_if_completion_was_late()
    {
        var result = Check(10000, Event("cargo-created", 9500), Event("invoice-created", 1200),
            Event("approval-completed", 300), Event("erp-completed"));
        Assert.Equal(new DelayAssessment("none", null, null, null), result);
    }

    [Fact]
    public void Receipt_time_does_not_postpone_the_business_deadline()
    {
        var approval = Event("approval-completed", 300) with { ReceivedAt = Start.AddHours(3) };
        var result = Check(10800, Event("erp-completed"), approval);
        Assert.Equal("overdue", result.State);
        Assert.Equal(Start.AddSeconds(900), result.DeadlineAt);
    }

    [Fact]
    public void Older_failure_does_not_reset_a_later_successful_anchor()
    {
        var result = Check(900, Event("erp-completed"), Event("approval-completed", 300),
            Event("approval-completed", 100, EventStatus.Failed));
        Assert.Equal("overdue", result.State);
        Assert.Equal("invoice-created", result.Step);
    }

    [Fact]
    public void Delay_does_not_change_the_business_status()
    {
        var projection = TimelineProjector.Project(Flow(), [Event("erp-completed")]);
        Assert.Equal("overdue", DelayEvaluator.Evaluate(projection, Start.AddHours(2)).State);
        Assert.Equal(TransactionStatus.InProgress, projection.Status);
    }
}
```

## 43. backend/tests/IntegraOps.Tests/IntegraOps.Tests.csproj

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <ProjectReference Include="../../src/IntegraOps.Application/IntegraOps.Application.csproj" />
  </ItemGroup>
</Project>
```

## 44. backend/tests/IntegraOps.Tests/TimelineTests.cs

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```csharp
using IntegraOps.Domain;
using Xunit;

namespace IntegraOps.Tests;

public sealed class TimelineTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-09-07T10:00:00Z");
    private static readonly FlowDefinition Flow = new(Guid.NewGuid(), Guid.NewGuid(), "order-flow", "Sipariş",
        [new("order-created", "Sipariş", 0, null), new("erp-completed", "ERP", 1, 300)]);

    private static ObservedEvent Event(string step, EventStatus status, int minute) =>
        new(Guid.NewGuid(), step, status, Start.AddMinutes(minute), Start.AddMinutes(20), null);

    [Fact]
    public void Reverse_delivery_order_does_not_change_completed_transaction()
    {
        var first = Event("order-created", EventStatus.Completed, 0);
        var second = Event("erp-completed", EventStatus.Completed, 1);
        var result = TimelineProjector.Project(Flow, [second, first]);
        Assert.Equal(TransactionStatus.Completed, result.Status);
        Assert.Equal(Start, result.Steps[0].OccurredAt);
    }

    [Fact]
    public void Missing_event_is_not_evidence_of_failed_business_step()
    {
        var result = TimelineProjector.Project(Flow, [Event("order-created", EventStatus.Completed, 0)]);
        Assert.Equal(TransactionStatus.InProgress, result.Status);
        Assert.Equal("not_observed", result.Steps[1].Observation);
        Assert.Null(result.Steps[1].Error);
    }

    [Fact]
    public void Late_delivery_of_old_failure_does_not_override_later_success()
    {
        var result = TimelineProjector.Project(Flow,
            [Event("order-created", EventStatus.Completed, 0),
             Event("erp-completed", EventStatus.Completed, 3),
             Event("erp-completed", EventStatus.Failed, 1)]);
        Assert.Equal(TransactionStatus.Completed, result.Status);
        Assert.Equal(Start.AddMinutes(3), result.Steps[1].OccurredAt);
    }

    [Fact]
    public void A_newer_failure_overrides_an_older_success()
    {
        var result = TimelineProjector.Project(Flow,
            [Event("order-created", EventStatus.Completed, 0),
             Event("erp-completed", EventStatus.Failed, 3),
             Event("erp-completed", EventStatus.Completed, 1)]);
        Assert.Equal(TransactionStatus.Failed, result.Status);
    }

    [Fact]
    public void Accepted_but_unprocessed_transaction_remains_pending()
    {
        var result = TimelineProjector.Project(Flow, []);
        Assert.Equal(TransactionStatus.Pending, result.Status);
        Assert.All(result.Steps, s => Assert.Equal("not_observed", s.Observation));
    }

    [Fact]
    public void Identical_business_timestamps_have_a_stable_tie_breaker()
    {
        var failure = Event("erp-completed", EventStatus.Failed, 1) with
            { EventId = Guid.Parse("00000000-0000-0000-0000-000000000001") };
        var success = Event("erp-completed", EventStatus.Completed, 1) with
            { EventId = Guid.Parse("00000000-0000-0000-0000-000000000002") };
        Assert.Equal(TimelineProjector.Project(Flow, [failure, success]).Steps[1],
            TimelineProjector.Project(Flow, [success, failure]).Steps[1]);
    }
}
```

## 45. backend/tests/IntegraOps.Tests/TransactionListTests.cs

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```csharp
using IntegraOps.Application;
using IntegraOps.Domain;
using Xunit;

namespace IntegraOps.Tests;

public sealed class TransactionListTests
{
    [Fact]
    public void Defaults_do_not_filter_out_pending_transactions()
    {
        var query = InputValidation.TransactionList(null, null, null);
        Assert.Null(query.Flow);
        Assert.Null(query.Status);
        Assert.Null(query.Delay);
        Assert.Equal(1, query.Page);
        Assert.Equal(20, query.PageSize);
    }

    [Theory]
    [InlineData("inProgress", TransactionStatus.InProgress)]
    [InlineData(" COMPLETED ", TransactionStatus.Completed)]
    public void Public_status_is_mapped_to_the_database_enum(string input, TransactionStatus expected)
    {
        var query = InputValidation.TransactionList(" delay-learning ", input, " OVERDUE ", 2, 10);
        Assert.Equal(expected, query.Status);
        Assert.Equal("delay-learning", query.Flow);
        Assert.Equal("overdue", query.Delay);
    }

    [Theory]
    [InlineData(0, 20, "page")]
    [InlineData(-1, 20, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 101, "pageSize")]
    public void Invalid_pagination_is_rejected(int page, int size, string field)
    {
        var ex = Assert.Throws<RequestValidationException>(() =>
            InputValidation.TransactionList(null, null, null, page, size));
        Assert.Contains(field, ex.Errors.Keys);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3")]
    [InlineData("unknown")]
    public void Numeric_or_unknown_status_cannot_silently_select_an_enum(string status) =>
        Assert.Throws<RequestValidationException>(() =>
            InputValidation.TransactionList(null, status, null));

    [Fact]
    public void Invalid_flow_and_delay_are_both_reported()
    {
        var ex = Assert.Throws<RequestValidationException>(() =>
            InputValidation.TransactionList("bad'flow", null, "late"));
        Assert.Contains("flow", ex.Errors.Keys);
        Assert.Contains("delay", ex.Errors.Keys);
    }

    [Theory]
    [InlineData(0, 1, 0, false)]
    [InlineData(20, 1, 1, false)]
    [InlineData(21, 1, 2, true)]
    [InlineData(21, 2, 2, false)]
    [InlineData(21, 99, 2, false)]
    public void Page_metadata_handles_empty_and_out_of_range_pages(
        long total, int page, long pages, bool hasNext)
    {
        var result = new TransactionPage(page, 20, total, []);
        Assert.Equal(pages, result.TotalPages);
        Assert.Equal(hasNext, result.HasNextPage);
    }

    [Fact]
    public void Large_page_does_not_overflow_next_page_calculation()
    {
        var query = InputValidation.TransactionList(null, null, null, int.MaxValue, 100);
        var result = new TransactionPage(query.Page, query.PageSize, 500, []);
        Assert.False(result.HasNextPage);
    }
}
```

## 46. backend/tests/IntegraOps.Tests/ValidationTests.cs

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```csharp
using IntegraOps.Application;
using Xunit;

namespace IntegraOps.Tests;

public sealed class ValidationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-07T10:00:00Z");
    private static SubmitEventRequest Valid() => new(Guid.NewGuid(), "order-flow", "ORDER-123",
        "order-created", "completed", "2026-09-07T12:59:00+03:00");

    [Fact]
    public void Explicit_timezone_is_normalized_to_utc()
    {
        var result = InputValidation.Event(Valid(), Now);
        Assert.Equal(TimeSpan.Zero, result.OccurredAt.Offset);
        Assert.Equal(Now.AddMinutes(-1), result.OccurredAt);
    }

    [Theory]
    [InlineData("2026-09-07T10:00:00")]
    [InlineData("07/09/2026 10:00:00")]
    [InlineData("2026-09-07T10:06:00Z")]
    public void Ambiguous_or_excessively_future_time_is_rejected(string time)
    {
        var exception = Assert.Throws<RequestValidationException>(() =>
            InputValidation.Event(Valid() with { OccurredAt = time }, Now));
        Assert.Contains("occurredAt", exception.Errors.Keys);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("success")]
    [InlineData("stuck")]
    public void Producer_cannot_invent_statuses(string status)
    {
        var exception = Assert.Throws<RequestValidationException>(() =>
            InputValidation.Event(Valid() with { Status = status }, Now));
        Assert.Contains("status", exception.Errors.Keys);
    }

    [Fact]
    public void Missing_event_identity_is_rejected()
    {
        var exception = Assert.Throws<RequestValidationException>(() =>
            InputValidation.Event(Valid() with { EventId = Guid.Empty }, Now));
        Assert.Contains("eventId", exception.Errors.Keys);
    }

    [Fact]
    public void Repeating_step_codes_would_make_a_flow_ambiguous()
    {
        var request = new CreateFlowRequest("order-flow", "Sipariş",
            [new("created", "İlk", null), new("created", "İkinci", 300)]);
        Assert.Throws<RequestValidationException>(() => InputValidation.Flow(request));
    }

    [Fact]
    public void Subsequent_step_requires_a_positive_deadline()
    {
        var request = new CreateFlowRequest("order-flow", "Sipariş",
            [new("created", "Sipariş", null), new("paid", "Ödeme", null)]);
        var exception = Assert.Throws<RequestValidationException>(() => InputValidation.Flow(request));
        Assert.Contains("steps[1].maxDelaySeconds", exception.Errors.Keys);
    }
}
```

## 47. Directory.Build.props

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest</AnalysisLevel>
  </PropertyGroup>
</Project>
```

## 48. Directory.Packages.props

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Npgsql" Version="10.0.3" />
    <PackageVersion Include="RabbitMQ.Client" Version="7.2.2" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.8.1" />
    <PackageVersion Include="xunit" Version="2.9.3" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.5" />
  </ItemGroup>
</Project>
```

## 49. global.json

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
```
