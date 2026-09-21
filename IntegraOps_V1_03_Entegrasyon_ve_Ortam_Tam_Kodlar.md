# IntegraOps V1 — Entegrasyon ve ortam tam kodlar

Önce yerel `docker-compose.yml` düzenini kullan. `compose.live.yml` yalnızca ileride gerçek sunucunun ayarlarıyla kullanılacak şablondur. Demo OrderSource ve webhook alıcısı canlı şablonda yayınlanmaz. Referans `verify_api.py` ve `verify_delays.py` eski kurulum kontrolleridir; sahip hesabı kurulduktan sonraki toplu kontrol için `verify_v1.py` kullan.

**20 yeni/değişen dosya; 3 referans dosya.**

Yollar proje köküne göredir. YENİ: dosyayı oluştur. DEĞİŞTİR: dosyanın tam içeriğini aşağıdakiyle değiştir. REFERANS: önceki sürümle aynı, sende varsa tekrar kopyalaman gerekmez.

## Dosya listesi

| İşlem | Dosya | Görevi |
|---|---|---|
| DEĞİŞTİR | `docker-compose.yml` | Yerel API/worker/veritabanı/kuyruk, e-posta, webhook ve isteğe bağlı kaynak örneği. |
| DEĞİŞTİR | `deploy/docker/Dockerfile` | API, worker ve kaynak örneği imajlarını üretir; ortak anahtar dizini izinlerini hazırlar. |
| YENİ | `frontend/Dockerfile` | Paneli derleyip ayrı kullanıcıyla çalıştıran canlı konteyner tanımı. |
| YENİ | `frontend/.dockerignore` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| YENİ | `compose.live.yml` | Ayrı veri birimleri ve HTTPS giriş noktasıyla canlı ortam hazırlık şablonu. |
| YENİ | `deploy/Caddyfile` | Panel ve API alan adlarının HTTPS üzerinden ilgili servislere yönlenmesi. |
| YENİ | `.env.live.example` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| DEĞİŞTİR | `.dockerignore` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| DEĞİŞTİR | `.gitignore` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| DEĞİŞTİR | `.github/workflows/ci.yml` | Backend/panel derlemesi, birim testleri ve geçici Docker ortamında V1 kontrolleri. |
| YENİ | `samples/OrderSource/OrderSource.csproj` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| YENİ | `samples/OrderSource/Program.cs` | Örnek sipariş/fatura iş kaydını ve gönderim kaydını aynı transaction içinde oluşturur. |
| YENİ | `samples/OrderSource/SourceOutbox.cs` | Kaynak iş kaydıyla birlikte bildirim ekleme ve HTTP üzerinden güvenilir gönderici. |
| YENİ | `samples/webhook/receiver.py` | Yerel webhook imza/zaman denetimi, kalıcı tekrar önleme ve hata enjekte etme örneği. |
| YENİ | `scripts/prepare_v1.py` | Mevcut .env değerlerini koruyup yalnızca eksik V1 yerel ayarlarını ekler. |
| YENİ | `scripts/setup_v1.py` | İlk sahip, yerel kanallar, test akışı ve kaynak anahtarını hazırlar. |
| YENİ | `scripts/verify_v1.py` | Yeni test projesi açıp modülleri, yükü ve isteğe bağlı yerel kesintileri kontrol eder. |
| YENİ | `scripts/backup_local.sh` | Veritabanını ve Data Protection anahtarlarını yedekler. |
| YENİ | `scripts/check_restore.sh` | Yedeği yeni bir kontrol veritabanına açar; asıl veritabanını değiştirmez. |
| YENİ | `scripts/recover_pending_events.sql` | Yedekten dönüşte henüz işlenmemiş olayların tekrar yayımlanmasını sağlar. |
| REFERANS | `scripts/init_local.py` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `scripts/verify_api.py` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |
| REFERANS | `scripts/verify_delays.py` | Mevcut yapının tamamlayıcı dosyası; içerik tam olarak aşağıdadır. |

## 1. docker-compose.yml

**DEĞİŞTİR** — Yerel API/worker/veritabanı/kuyruk, e-posta, webhook ve isteğe bağlı kaynak örneği.

```yaml
name: integraops

services:
  postgres:
    image: postgres:17-alpine
    environment:
      POSTGRES_USER: integraops
      POSTGRES_DB: integraops
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?Run python3 scripts/init_local.py}
    ports:
      - "127.0.0.1:${POSTGRES_PORT:-54329}:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U integraops -d integraops"]
      interval: 3s
      timeout: 3s
      retries: 20

  rabbitmq:
    image: rabbitmq:4-management
    environment:
      RABBITMQ_DEFAULT_USER: integraops
      RABBITMQ_DEFAULT_PASS: ${RABBITMQ_PASSWORD:?Run python3 scripts/init_local.py}
    ports:
      - "127.0.0.1:${RABBITMQ_PORT:-56729}:5672"
      - "127.0.0.1:${RABBITMQ_MANAGEMENT_PORT:-15679}:15672"
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "-q", "ping"]
      interval: 5s
      timeout: 5s
      retries: 24

  migrate:
    build:
      context: .
      dockerfile: deploy/docker/Dockerfile
      target: api
    command: ["--migrate"]
    environment:
      ConnectionStrings__Postgres: "Host=postgres;Database=integraops;Username=integraops;Password=${POSTGRES_PASSWORD};Timeout=5;Command Timeout=15"
    depends_on:
      postgres:
        condition: service_healthy
    restart: "no"

  api:
    restart: unless-stopped
    build:
      context: .
      dockerfile: deploy/docker/Dockerfile
      target: api
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ConnectionStrings__Postgres: "Host=postgres;Database=integraops;Username=integraops;Password=${POSTGRES_PASSWORD};Timeout=5;Command Timeout=15"
      Bootstrap__Key: ${BOOTSTRAP_KEY:?Run python3 scripts/init_local.py}
      Notifications__WebhookOrigins: ${WEBHOOK_ORIGINS:-http://webhook:8080}
      Notifications__PrivateWebhookOrigins: ${PRIVATE_WEBHOOK_ORIGINS:-http://webhook:8080}
    volumes:
      - protection_keys:/var/lib/integraops/keys
    ports:
      - "127.0.0.1:${API_PORT:-8080}:8080"
    depends_on:
      migrate:
        condition: service_completed_successfully

  worker:
    restart: unless-stopped
    build:
      context: .
      dockerfile: deploy/docker/Dockerfile
      target: worker
    environment:
      ConnectionStrings__Postgres: "Host=postgres;Database=integraops;Username=integraops;Password=${POSTGRES_PASSWORD};Timeout=5;Command Timeout=15"
      RabbitMQ__Uri: "amqp://integraops:${RABBITMQ_PASSWORD}@rabbitmq:5672/"
      Notifications__WebhookOrigins: ${WEBHOOK_ORIGINS:-http://webhook:8080}
      Notifications__PrivateWebhookOrigins: ${PRIVATE_WEBHOOK_ORIGINS:-http://webhook:8080}
      Notifications__RetryBaseSeconds: 2
      Smtp__Host: mailpit
      Smtp__Port: 1025
      Smtp__StartTls: "false"
      Smtp__From: integraops@local.test
    volumes:
      - protection_keys:/var/lib/integraops/keys
    depends_on:
      migrate:
        condition: service_completed_successfully
      rabbitmq:
        condition: service_healthy

  mailpit:
    image: axllent/mailpit:latest
    ports:
      - "127.0.0.1:8025:8025"
    volumes:
      - mailpit_data:/data
    environment:
      MP_DATABASE: /data/mailpit.db
      MP_MAX_MESSAGES: 1000

  webhook:
    image: python:3.13-alpine
    command: ["python", "-u", "/app/receiver.py"]
    environment:
      WEBHOOK_SECRET: ${LOCAL_WEBHOOK_SECRET:?Run python3 scripts/prepare_v1.py}
    volumes:
      - ./samples/webhook/receiver.py:/app/receiver.py:ro
      - webhook_data:/data
    ports:
      - "127.0.0.1:8082:8080"

  source-postgres:
    image: postgres:17-alpine
    profiles: [demo]
    environment:
      POSTGRES_USER: source
      POSTGRES_DB: source
      POSTGRES_PASSWORD: ${SOURCE_POSTGRES_PASSWORD:?Run python3 scripts/prepare_v1.py}
    volumes:
      - source_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U source -d source"]
      interval: 3s
      timeout: 3s
      retries: 20

  source:
    profiles: [demo]
    build:
      context: .
      dockerfile: deploy/docker/Dockerfile
      target: source
    environment:
      ConnectionStrings__Postgres: "Host=source-postgres;Database=source;Username=source;Password=${SOURCE_POSTGRES_PASSWORD};Timeout=5;Command Timeout=15"
      IntegraOps__Url: http://api:8080
      IntegraOps__Key: ${SOURCE_API_KEY:-not-configured}
      IntegraOps__Flow: pilot-source
    ports:
      - "127.0.0.1:8081:8080"
    depends_on:
      source-postgres:
        condition: service_healthy
      api:
        condition: service_started

volumes:
  postgres_data:
  rabbitmq_data:
  protection_keys:
  mailpit_data:
  webhook_data:
  source_data:
```

## 2. deploy/docker/Dockerfile

**DEĞİŞTİR** — API, worker ve kaynak örneği imajlarını üretir; ortak anahtar dizini izinlerini hazırlar.

```text
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish backend/src/IntegraOps.Api/IntegraOps.Api.csproj -c Release -o /out/api
RUN dotnet publish backend/src/IntegraOps.Worker/IntegraOps.Worker.csproj -c Release -o /out/worker
RUN dotnet publish samples/OrderSource/OrderSource.csproj -c Release -o /out/source

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
COPY --from=build /out/api .
RUN mkdir -p /var/lib/integraops/keys && chown -R app:app /var/lib/integraops
ENV Protection__KeyPath=/var/lib/integraops/keys
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "IntegraOps.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS worker
WORKDIR /app
COPY --from=build /out/worker .
RUN mkdir -p /var/lib/integraops/keys && chown -R app:app /var/lib/integraops
ENV Protection__KeyPath=/var/lib/integraops/keys
USER app
ENTRYPOINT ["dotnet", "IntegraOps.Worker.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS source
WORKDIR /app
COPY --from=build /out/source .
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "OrderSource.dll"]
```

## 3. frontend/Dockerfile

**YENİ** — Paneli derleyip ayrı kullanıcıyla çalıştıran canlı konteyner tanımı.

```text
FROM node:24-alpine AS build
WORKDIR /app
ENV NEXT_TELEMETRY_DISABLED=1
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build
RUN npm prune --omit=dev

FROM node:24-alpine
WORKDIR /app
ENV NODE_ENV=production NEXT_TELEMETRY_DISABLED=1
COPY --from=build --chown=node:node /app .
USER node
EXPOSE 3000
CMD ["node", "node_modules/next/dist/bin/next", "start", "--hostname", "0.0.0.0"]
```

## 4. frontend/.dockerignore

**YENİ** — Mevcut yapının tamamlayıcı dosyası.

```text
node_modules
.next
.env*
.git
*.tsbuildinfo
```

## 5. compose.live.yml

**YENİ** — Ayrı veri birimleri ve HTTPS giriş noktasıyla canlı ortam hazırlık şablonu.

```yaml
name: integraops-live

services:
  postgres:
    image: postgres:17-alpine
    restart: unless-stopped
    environment:
      POSTGRES_USER: integraops
      POSTGRES_DB: integraops
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?Required}
    volumes: ["postgres_data:/var/lib/postgresql/data"]
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U integraops -d integraops"]
      interval: 5s
      timeout: 3s
      retries: 20
  rabbitmq:
    image: rabbitmq:4-management
    restart: unless-stopped
    environment:
      RABBITMQ_DEFAULT_USER: integraops
      RABBITMQ_DEFAULT_PASS: ${RABBITMQ_PASSWORD:?Required}
    volumes: ["rabbitmq_data:/var/lib/rabbitmq"]
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "-q", "ping"]
      interval: 5s
      timeout: 5s
      retries: 24
  migrate:
    build: { context: ., dockerfile: deploy/docker/Dockerfile, target: api }
    command: ["--migrate"]
    environment:
      ConnectionStrings__Postgres: "Host=postgres;Database=integraops;Username=integraops;Password=${POSTGRES_PASSWORD};Timeout=5;Command Timeout=15"
    depends_on: { postgres: { condition: service_healthy } }
  api:
    build: { context: ., dockerfile: deploy/docker/Dockerfile, target: api }
    restart: unless-stopped
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ConnectionStrings__Postgres: "Host=postgres;Database=integraops;Username=integraops;Password=${POSTGRES_PASSWORD};Timeout=5;Command Timeout=15"
      Bootstrap__Key: ${BOOTSTRAP_KEY:?Required}
      Notifications__WebhookOrigins: ${WEBHOOK_ORIGINS:-}
    volumes: ["protection_keys:/var/lib/integraops/keys"]
    depends_on: { migrate: { condition: service_completed_successfully } }
  worker:
    build: { context: ., dockerfile: deploy/docker/Dockerfile, target: worker }
    restart: unless-stopped
    environment:
      ConnectionStrings__Postgres: "Host=postgres;Database=integraops;Username=integraops;Password=${POSTGRES_PASSWORD};Timeout=5;Command Timeout=15"
      RabbitMQ__Uri: "amqp://integraops:${RABBITMQ_PASSWORD}@rabbitmq:5672/"
      Notifications__WebhookOrigins: ${WEBHOOK_ORIGINS:-}
      Notifications__RetryBaseSeconds: 30
      Smtp__Host: ${SMTP_HOST:?Required}
      Smtp__Port: ${SMTP_PORT:-587}
      Smtp__StartTls: "true"
      Smtp__From: ${SMTP_FROM:?Required}
      Smtp__User: ${SMTP_USER:-}
      Smtp__Password: ${SMTP_PASSWORD:-}
    volumes: ["protection_keys:/var/lib/integraops/keys"]
    depends_on:
      migrate: { condition: service_completed_successfully }
      rabbitmq: { condition: service_healthy }
  panel:
    build: { context: ./frontend }
    restart: unless-stopped
    environment:
      INTEGRAOPS_API_URL: http://api:8080
      PANEL_COOKIE_SECURE: "true"
    depends_on: [api]
  caddy:
    image: caddy:2-alpine
    restart: unless-stopped
    environment:
      PANEL_HOST: ${PANEL_HOST:?Required}
      API_HOST: ${API_HOST:?Required}
    ports: ["80:80", "443:443", "443:443/udp"]
    volumes:
      - ./deploy/Caddyfile:/etc/caddy/Caddyfile:ro
      - caddy_data:/data
      - caddy_config:/config
    depends_on: [panel, api]

volumes:
  postgres_data:
  rabbitmq_data:
  protection_keys:
  caddy_data:
  caddy_config:
```

## 6. deploy/Caddyfile

**YENİ** — Panel ve API alan adlarının HTTPS üzerinden ilgili servislere yönlenmesi.

```text
{$PANEL_HOST} {
    encode zstd gzip
    reverse_proxy panel:3000
}

{$API_HOST} {
    encode zstd gzip
    reverse_proxy api:8080
}
```

## 7. .env.live.example

**YENİ** — Mevcut yapının tamamlayıcı dosyası.

```text
# Copy to .env.live only when preparing the live host. Generate new hex secrets.
POSTGRES_PASSWORD=replace_with_48_hex_characters
RABBITMQ_PASSWORD=replace_with_48_hex_characters
BOOTSTRAP_KEY=replace_with_64_hex_characters
PANEL_HOST=panel.example.com
API_HOST=api.example.com
SMTP_HOST=smtp.example.com
SMTP_PORT=587
SMTP_FROM=notifications@example.com
SMTP_USER=replace_me
SMTP_PASSWORD=replace_me
# Comma-separated HTTPS origins, e.g. https://hooks.example.com
WEBHOOK_ORIGINS=
```

## 8. .dockerignore

**DEĞİŞTİR** — Mevcut yapının tamamlayıcı dosyası.

```text
**/bin
**/obj
**/TestResults
.git
.env
.local
.vs
.idea
frontend/
backups/
**/.env*
**/keys/
**/__pycache__/
.verification/
```

## 9. .gitignore

**DEĞİŞTİR** — Mevcut yapının tamamlayıcı dosyası.

```text
**/bin/
**/obj/
**/TestResults/
.env
.local/
.vs/
.idea/
*.user
*.suo
.DS_Store
**/.env.local
**/keys/
backups/
**/__pycache__/
.verification/
.env.live
```

## 10. .github/workflows/ci.yml

**DEĞİŞTİR** — Backend/panel derlemesi, birim testleri ve geçici Docker ortamında V1 kontrolleri.

```yaml
name: IntegraOps checks

on:
  push:
  pull_request:

jobs:
  backend:
    runs-on: ubuntu-latest
    timeout-minutes: 20
    env:
      V1_OWNER_EMAIL: ci-owner@local.test
      V1_OWNER_NAME: CI Owner
      # A disposable account in this job's isolated local database, never a deployment credential.
      V1_OWNER_PASSWORD: ci-only-test-password-123456
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - name: Build
        run: dotnet build IntegraOps.slnx -c Release
      - name: Unit tests
        run: dotnet test IntegraOps.slnx -c Release --no-build
      - name: Local configuration
        run: |
          python3 scripts/init_local.py
          python3 scripts/prepare_v1.py
      - name: Start actual services
        run: docker compose up --build -d
      - name: Verify the event pipeline
        run: python3 scripts/verify_api.py
      - name: Configure V1 and start the source sample
        run: |
          python3 scripts/setup_v1.py
          docker compose --profile demo up --build -d source
      - name: Verify V1 modules
        run: python3 scripts/verify_v1.py --load 50
      - name: Service logs on failure
        if: failure()
        run: docker compose logs --no-color --tail=150 api worker migrate
      - name: Stop test services
        if: always()
        run: docker compose --profile demo down

  panel:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: '24'
          cache: npm
          cache-dependency-path: frontend/package-lock.json
      - run: npm ci
        working-directory: frontend
      - run: npm run build
        working-directory: frontend
```

## 11. samples/OrderSource/OrderSource.csproj

**YENİ** — Mevcut yapının tamamlayıcı dosyası.

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <ItemGroup><PackageReference Include="Npgsql" /></ItemGroup>
</Project>
```

## 12. samples/OrderSource/Program.cs

**YENİ** — Örnek sipariş/fatura iş kaydını ve gönderim kaydını aynı transaction içinde oluşturur.

```csharp
using System.Text.RegularExpressions;
using Npgsql;
using OrderSource;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 8192);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Kaynak veritabanı ayarı gerekli.")));
builder.Services.AddHostedService<SourceSender>();
var app = builder.Build();
var source = app.Services.GetRequiredService<NpgsqlDataSource>();
await using (var c = await source.OpenConnectionAsync())
await using (var schema = SourceDb.Command(c, null, """
    CREATE TABLE IF NOT EXISTS orders (
        id varchar(128) PRIMARY KEY,
        invoice_state varchar(16) NOT NULL DEFAULT 'waiting',
        created_at timestamptz NOT NULL DEFAULT clock_timestamp()
    );
    CREATE TABLE IF NOT EXISTS source_outbox (
        id uuid PRIMARY KEY,body text NOT NULL,state varchar(16) NOT NULL DEFAULT 'pending',
        attempts integer NOT NULL DEFAULT 0,available_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        lease_token uuid,lease_until timestamptz,last_error text,
        created_at timestamptz NOT NULL DEFAULT clock_timestamp()
    );
    CREATE INDEX IF NOT EXISTS source_outbox_due ON source_outbox(available_at) WHERE state IN ('pending','sending');
    """)) await schema.ExecuteNonQueryAsync();

string Flow() => builder.Configuration["IntegraOps:Flow"] ?? "pilot-source";
SourceEvent Event(string id, string step, string status, string? error = null) =>
    new(Guid.NewGuid(), Flow(), id, step, status, DateTimeOffset.UtcNow.ToString("O"), error);

// These endpoints are a local demonstration of a source business system.
// They are not exposed by IntegraOps and must not be published as a public order API.
app.MapPost("/orders", async (OrderInput input, CancellationToken ct) =>
{
    if (input.TransactionId is null || !Regex.IsMatch(input.TransactionId, "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$"))
        return Results.BadRequest(new { error = "transactionId geçersiz." });
    await using var c = await source.OpenConnectionAsync(ct);
    await using var tx = await c.BeginTransactionAsync(ct);
    await using var insert = SourceDb.Command(c, tx,
        "INSERT INTO orders(id) VALUES(@id) ON CONFLICT DO NOTHING", ("id", input.TransactionId));
    var created = await insert.ExecuteNonQueryAsync(ct) > 0;
    if (created) await SourceDb.Enqueue(c, tx, Event(input.TransactionId, "erp-created", "completed"), ct);
    await tx.CommitAsync(ct);
    return Results.Ok(new { transactionId = input.TransactionId, created });
});

app.MapPost("/orders/{id}/{action}", async (string id, string action, CancellationToken ct) =>
{
    if (action is not ("invoice" or "invoice-failed")) return Results.NotFound();
    await using var c = await source.OpenConnectionAsync(ct);
    await using var tx = await c.BeginTransactionAsync(ct);
    await using var find = SourceDb.Command(c, tx, "SELECT invoice_state FROM orders WHERE id=@id FOR UPDATE", ("id", id));
    var previous = await find.ExecuteScalarAsync(ct) as string;
    if (previous is null) return Results.NotFound();
    var next = action == "invoice" ? "completed" : "failed";
    if (previous == "completed" && next == "failed") return Results.Conflict(new { error = "Tamamlanan demo faturası geri alınamaz." });
    if (previous != next)
    {
        await using var update = SourceDb.Command(c, tx, "UPDATE orders SET invoice_state=@s WHERE id=@id", ("s", next), ("id", id));
        await update.ExecuteNonQueryAsync(ct);
        await SourceDb.Enqueue(c, tx, Event(id, "invoice-created", next, next == "failed" ? "Yerel test: fatura servisi hata bildirdi." : null), ct);
    }
    await tx.CommitAsync(ct);
    return Results.Ok(new { transactionId = id, invoiceState = next });
});

app.MapGet("/outbox", async (CancellationToken ct) =>
{
    await using var c = await source.OpenConnectionAsync(ct);
    await using var cmd = SourceDb.Command(c, null, "SELECT id,state,attempts,last_error,created_at FROM source_outbox ORDER BY created_at DESC LIMIT 100");
    await using var r = await cmd.ExecuteReaderAsync(ct); var items = new List<object>();
    while (await r.ReadAsync(ct)) items.Add(new { id = r.GetGuid(0), state = r.GetString(1), attempts = r.GetInt32(2),
        lastError = r.IsDBNull(3) ? null : r.GetString(3), createdAt = r.GetDateTime(4) });
    return Results.Ok(items);
});
app.MapPost("/outbox/{id:guid}/retry", async (Guid id, CancellationToken ct) =>
{
    await using var c = await source.OpenConnectionAsync(ct);
    await using var cmd = SourceDb.Command(c, null, "UPDATE source_outbox SET state='pending',available_at=clock_timestamp() WHERE id=@id AND state='dead'", ("id", id));
    return await cmd.ExecuteNonQueryAsync(ct) > 0 ? Results.NoContent() : Results.NotFound();
});
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
await app.RunAsync();
public sealed record OrderInput(string TransactionId);
```

## 13. samples/OrderSource/SourceOutbox.cs

**YENİ** — Kaynak iş kaydıyla birlikte bildirim ekleme ve HTTP üzerinden güvenilir gönderici.

```csharp
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace OrderSource;

public sealed record SourceEvent(Guid EventId, string Flow, string TransactionId, string Step,
    string Status, string OccurredAt, string? Error = null);

public static class SourceDb
{
    public static NpgsqlCommand Command(NpgsqlConnection c, NpgsqlTransaction? tx, string sql, params (string, object?)[] args)
    {
        var command = new NpgsqlCommand(sql, c, tx);
        foreach (var (name, value) in args)
        {
            var p = new NpgsqlParameter { ParameterName = name, Value = value ?? DBNull.Value };
            if (value is null) p.NpgsqlDbType = NpgsqlDbType.Unknown;
            command.Parameters.Add(p);
        }
        return command;
    }
    public static async Task Enqueue(NpgsqlConnection c, NpgsqlTransaction tx, SourceEvent message, CancellationToken ct)
    {
        // Use the same connection and transaction as the business write.
        // Never call IntegraOps over HTTP inside that transaction.
        var body = JsonSerializer.Serialize(message, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await using var cmd = Command(c, tx, "INSERT INTO source_outbox(id,body) VALUES(@id,@body)", ("id", message.EventId), ("body", body));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

public sealed class SourceSender(NpgsqlDataSource source, IConfiguration config, ILogger<SourceSender> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await SendNext(client, stoppingToken)) await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("Kaynak gönderim kuyruğu bekliyor. ErrorType: {Type}", ex.GetType().Name);
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task<bool> SendNext(HttpClient client, CancellationToken ct)
    {
        Guid id; string body; int attempt; var lease = Guid.NewGuid();
        await using (var c = await source.OpenConnectionAsync(ct))
        await using (var tx = await c.BeginTransactionAsync(ct))
        {
            await using (var cmd = SourceDb.Command(c, tx, """
                SELECT id,body,attempts FROM source_outbox
                WHERE (state='pending' AND available_at<=clock_timestamp()) OR (state='sending' AND lease_until<clock_timestamp())
                ORDER BY created_at,id LIMIT 1 FOR UPDATE SKIP LOCKED
                """))
            await using (var r = await cmd.ExecuteReaderAsync(ct))
            {
                if (!await r.ReadAsync(ct)) return false;
                id = r.GetGuid(0); body = r.GetString(1); attempt = r.GetInt32(2) + 1;
            }
            await using var claim = SourceDb.Command(c, tx, """
                UPDATE source_outbox SET state='sending',attempts=attempts+1,lease_token=@l,lease_until=clock_timestamp()+interval '60 seconds'
                WHERE id=@id
                """, ("id", id), ("l", lease));
            await claim.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct);
        }
        var state = "pending"; string? error = null;
        try
        {
            var baseUrl = config["IntegraOps:Url"] ?? throw new InvalidOperationException("API adresi gerekli.");
            var key = config["IntegraOps:Key"] ?? throw new InvalidOperationException("Gönderim anahtarı gerekli.");
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl), "/api/v1/events"))
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            request.Headers.Add("X-API-Key", key);
            using var response = await client.SendAsync(request, ct);
            var code = (int)response.StatusCode;
            if (code is 200 or 202)
            {
                using var receipt = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (receipt.RootElement.GetProperty("eventId").GetGuid() != id) throw new InvalidDataException();
                state = "sent";
            }
            else
            {
                error = "http_" + code;
                if (code is >= 300 and < 500 && code is not (408 or 425 or 429)) state = "dead";
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { error = ex.GetType().Name; }
        await using var finishConnection = await source.OpenConnectionAsync(ct);
        await using var finish = SourceDb.Command(finishConnection, null, """
            UPDATE source_outbox SET state=@s,last_error=@e,lease_token=NULL,lease_until=NULL,
                available_at=clock_timestamp()+@delay WHERE id=@id AND lease_token=@l AND state='sending'
            """, ("s", state), ("e", error), ("delay", TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, Math.Min(attempt, 6))) + Random.Shared.Next(3))),
            ("id", id), ("l", lease));
        await finish.ExecuteNonQueryAsync(ct);
        return true;
    }
}
```

## 14. samples/webhook/receiver.py

**YENİ** — Yerel webhook imza/zaman denetimi, kalıcı tekrar önleme ve hata enjekte etme örneği.

```python
"""Local-only webhook receiver. Checks signature, freshness and durable deduplication."""
import hashlib
import hmac
import json
import os
import sqlite3
import time
import uuid
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

root = Path(os.environ.get("RECEIVER_DATA", "/data"))
root.mkdir(parents=True, exist_ok=True)
db = sqlite3.connect(root / "receipts.sqlite")
db.execute("CREATE TABLE IF NOT EXISTS receipts(id TEXT PRIMARY KEY, body TEXT NOT NULL, received_at REAL NOT NULL)")
db.commit()
secret = os.environ["WEBHOOK_SECRET"].encode()
if len(secret) < 32:
    raise RuntimeError("WEBHOOK_SECRET en az 32 karakter olmalı.")
failures = 0

class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_):
        pass

    def reply(self, code, body):
        data = json.dumps(body, ensure_ascii=False).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        if self.path != "/receipts":
            return self.reply(404, {})
        rows = db.execute("SELECT id,body,received_at FROM receipts ORDER BY received_at DESC LIMIT 100").fetchall()
        self.reply(200, [{"id": r[0], "body": json.loads(r[1]), "receivedAt": r[2]} for r in rows])

    def do_POST(self):
        global failures
        try:
            length = int(self.headers.get("Content-Length", "0"))
            if not 0 <= length <= 65536:
                return self.reply(413, {})
            body = self.rfile.read(length)
            # Local fault injection only. This receiver is never a public production service.
            if self.path == "/test/fail-next":
                failures = max(0, min(100, int(json.loads(body).get("count", 1))))
                return self.reply(200, {"failures": failures})
            if self.path != "/hook":
                return self.reply(404, {})
            delivery = str(uuid.UUID(self.headers.get("X-IntegraOps-Delivery", "")))
            timestamp = self.headers.get("X-IntegraOps-Time", "")
            if abs(time.time() - int(timestamp)) > 300:
                return self.reply(401, {"error": "expired"})
            signed = timestamp.encode() + b"." + delivery.encode() + b"." + body
            expected = "sha256=" + hmac.new(secret, signed, hashlib.sha256).hexdigest()
            if not hmac.compare_digest(expected, self.headers.get("X-IntegraOps-Signature", "")):
                return self.reply(401, {"error": "signature"})
            payload = json.loads(body)
            if failures:
                failures -= 1
                return self.reply(503, {"error": "local_test"})
            # A real receiver performs its business side effect in this same transaction.
            with db:
                created = db.execute("INSERT OR IGNORE INTO receipts VALUES(?,?,?)",
                    (delivery, json.dumps(payload, ensure_ascii=False), time.time())).rowcount == 1
            self.reply(200, {"accepted": True, "duplicate": not created})
        except (ValueError, json.JSONDecodeError):
            self.reply(400, {"error": "invalid_request"})

if __name__ == "__main__":
    HTTPServer((os.environ.get("RECEIVER_HOST", "0.0.0.0"), int(os.environ.get("RECEIVER_PORT", "8080"))), Handler).serve_forever()
```

## 15. scripts/prepare_v1.py

**YENİ** — Mevcut .env değerlerini koruyup yalnızca eksik V1 yerel ayarlarını ekler.

```python
"""Append missing local settings; never replace existing credentials."""
import os
import secrets
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def read_env(path):
    values = {}
    if path.exists():
        for line in path.read_text().splitlines():
            if line.strip() and not line.lstrip().startswith("#") and "=" in line:
                key, value = line.split("=", 1)
                values[key.strip()] = value.strip().strip('"').strip("'")
    return values

def update_env(path, changes):
    text = path.read_text() if path.exists() else ""
    lines = text.splitlines()
    for key, value in changes.items():
        replacement = f"{key}={value}"
        matches = [i for i, line in enumerate(lines) if line.startswith(key + "=")]
        if matches:
            lines[matches[0]] = replacement
        else:
            lines.append(replacement)
    temporary = path.with_name(path.name + ".tmp")
    descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(descriptor, "w") as stream:
        stream.write("\n".join(lines) + "\n")
    os.replace(temporary, path)
    os.chmod(path, 0o600)

def main():
    path = ROOT / ".env"
    current = read_env(path)
    if not all(current.get(k) for k in ("POSTGRES_PASSWORD", "RABBITMQ_PASSWORD", "BOOTSTRAP_KEY")):
        raise SystemExit("Mevcut .env bulunamadı veya eksik. İlk kurulumdaki scripts/init_local.py dosyasını kullanın.")
    defaults = {"LOCAL_WEBHOOK_SECRET": secrets.token_hex(32), "SOURCE_POSTGRES_PASSWORD": secrets.token_hex(24)}
    update_env(path, {k: v for k, v in defaults.items() if not current.get(k)})
    panel = ROOT / "frontend" / ".env.local"
    if panel.parent.exists():
        config = read_env(panel)
        update_env(panel, {k: v for k, v in {"INTEGRAOPS_API_URL": "http://127.0.0.1:" + current.get("API_PORT", "8080"),
            "PANEL_COOKIE_SECURE": "false"}.items() if k not in config})
    print("V1 yerel ayarları hazır; mevcut anahtarlar korundu.")

if __name__ == "__main__":
    main()
```

## 16. scripts/setup_v1.py

**YENİ** — İlk sahip, yerel kanallar, test akışı ve kaynak anahtarını hazırlar.

```python
"""Create the first owner, local notification channels and the source integration key."""
import getpass
import json
import os
import time
import urllib.error
import urllib.request
from prepare_v1 import ROOT, read_env, update_env

def request(base, path, data=None, headers=None, method=None):
    payload = None if data is None else json.dumps(data).encode()
    req = urllib.request.Request(base.rstrip("/") + path, data=payload,
        headers={"Content-Type": "application/json", **(headers or {})}, method=method or ("POST" if payload is not None else "GET"))
    try:
        with urllib.request.urlopen(req, timeout=20) as response:
            body = response.read()
            return response.status, json.loads(body) if body else None
    except urllib.error.HTTPError as error:
        body = error.read()
        return error.code, json.loads(body) if body else None

def require(result, codes=(200, 201, 204)):
    status, body = result
    if status not in codes:
        title = body.get("title", "İstek tamamlanamadı") if isinstance(body, dict) else "İstek tamamlanamadı"
        raise SystemExit(f"HTTP {status}: {title}")
    return body

def main():
    env = read_env(ROOT / ".env")
    panel = read_env(ROOT / "frontend" / ".env.local")
    base = panel.get("INTEGRAOPS_API_URL", "http://127.0.0.1:" + env.get("API_PORT", "8080"))
    for attempt in range(30):
        try:
            if request(base, "/health/ready")[0] == 200:
                break
        except (OSError, urllib.error.URLError):
            pass
        if attempt == 29:
            raise SystemExit("API hazır değil. docker compose logs --tail=50 api migrate ile kontrol edin.")
        time.sleep(1)
    email = (os.environ.get("V1_OWNER_EMAIL") or input("Proje sahibi e-posta: ")).strip()
    name = (os.environ.get("V1_OWNER_NAME") or input("Ad soyad: ")).strip()
    password = os.environ.get("V1_OWNER_PASSWORD") or getpass.getpass("Parola (ilk kurulumda en az 12 karakter): ")
    # Try login first so re-running setup never needs the revoked legacy key.
    login_status, login_body = request(base, "/api/v1/auth/login", {"email": email, "password": password})
    if login_status != 200:
        saved = ROOT / ".local" / "demo-project.json"
        saved_key = json.loads(saved.read_text()).get("apiKey") if saved.exists() else None
        key = panel.get("INTEGRAOPS_API_KEY") or saved_key or getpass.getpass("Mevcut proje API anahtarı: ")
        status, body = request(base, "/api/v1/bootstrap/owner", {"email": email, "name": name, "password": password},
            {"X-Bootstrap-Key": env["BOOTSTRAP_KEY"], "X-API-Key": key})
        require((status, body), (200, 409))
        login_body = require(request(base, "/api/v1/auth/login", {"email": email, "password": password}))
    headers = {"Authorization": "Bearer " + login_body["token"]}
    projects = require(request(base, "/api/v1/me/projects", headers=headers))
    owners = [p for p in projects if p["role"] == "owner"]
    if not owners:
        raise SystemExit("Bu hesap bir projenin sahibi olmalı.")
    selected = next((p for p in owners if p["id"] == env.get("PILOT_PROJECT_ID")), None)
    if selected is None:
        for index, project in enumerate(owners, 1):
            print(f"{index}. {project['name']} ({project['id']})")
        choice = 1 if len(owners) == 1 else int(input("Kurulum yapılacak proje numarası: "))
        if not 1 <= choice <= len(owners):
            raise SystemExit("Proje numarası geçersiz.")
        selected = owners[choice - 1]
    headers["X-Project-Id"] = selected["id"]
    flow = {"code": "pilot-source", "name": "Kaynak sistem denemesi", "steps": [
        {"code": "erp-created", "name": "ERP kaydı", "maxDelaySeconds": None},
        {"code": "invoice-created", "name": "Fatura", "maxDelaySeconds": 30}]}
    require(request(base, "/api/v1/flows", flow, headers), (201, 409))
    channels = require(request(base, "/api/v1/channels", headers=headers))
    for channel in [
        {"name": "Yerel e-posta", "kind": "email", "target": "operations@local.test"},
        {"name": "Yerel webhook", "kind": "webhook", "target": "http://webhook:8080/hook", "secret": env["LOCAL_WEBHOOK_SECRET"]}]:
        if not any(c["name"] == channel["name"] for c in channels):
            require(request(base, "/api/v1/channels", channel, headers))
    keys = require(request(base, "/api/v1/keys", headers=headers))
    old_key = env.get("SOURCE_API_KEY", "")
    reusable = env.get("PILOT_PROJECT_ID") == selected["id"] and any(k["scope"] == "ingest" and not k["revoked_at"]
        and k["key_prefix"] == old_key[:16] for k in keys)
    if not reusable:
        old_key = require(request(base, "/api/v1/keys", {"name": "Yerel kaynak sistemi", "scope": "ingest"}, headers))["key"]
    update_env(ROOT / ".env", {"SOURCE_API_KEY": old_key, "PILOT_PROJECT_ID": selected["id"]})
    require(request(base, "/api/v1/me/logout", headers=headers, method="POST"))
    print("Kurulum tamamlandı. Panelden giriş yapabilirsiniz.")
    print("Kaynak örneğini başlatmak için: docker compose --profile demo up --build -d source")
    print("Yerel e-postalar: http://localhost:8025 · Webhook kayıtları: http://localhost:8082/receipts")

if __name__ == "__main__":
    main()
```

## 17. scripts/verify_v1.py

**YENİ** — Yeni test projesi açıp modülleri, yükü ve isteğe bağlı yerel kesintileri kontrol eder.

```python
"""Collective local V1 test; creates an isolated test project. No third-party services are contacted."""
import argparse
import getpass
import json
import os
import secrets
import subprocess
import time
import uuid
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone, timedelta
from prepare_v1 import ROOT, read_env
from setup_v1 import request, require

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--load", type=int, default=25, help="Number of two-step transactions, 1–500")
    parser.add_argument("--faults", action="store_true", help="Temporarily stop local API and RabbitMQ, then start them again")
    args = parser.parse_args()
    if not 1 <= args.load <= 500:
        raise SystemExit("--load 1–500 arasında olmalı.")
    env = read_env(ROOT / ".env")
    base = "http://127.0.0.1:" + env.get("API_PORT", "8080")
    email = (os.environ.get("V1_OWNER_EMAIL") or input("Proje sahibi e-posta: ")).strip()
    password = os.environ.get("V1_OWNER_PASSWORD") or getpass.getpass("Parola: ")
    token = require(request(base, "/api/v1/auth/login", {"email": email, "password": password}))["token"]
    session = {"Authorization": "Bearer " + token}
    suffix = uuid.uuid4().hex[:10]
    project = require(request(base, "/api/v1/me/projects", {"name": "V1 toplu test " + suffix}, session))
    headers = session | {"X-Project-Id": project["id"]}
    passed = 0
    def check(name, value):
        nonlocal passed
        if not value:
            raise AssertionError(name)
        passed += 1
        print("GEÇTİ ·", name, flush=True)
    def api(path, data=None, auth=None, method=None):
        return request(base, "/api/v1" + path, data, headers if auth is None else auth, method)
    def wait(name, predicate, timeout=90):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            if predicate():
                check(name, True)
                return
            time.sleep(1)
        raise AssertionError(name + " zaman aşımı")
    print("Test projesi:", project["name"], project["id"])
    flow = {"code": "v1-check", "name": "Toplu kontrol", "steps": [
        {"code": "erp-created", "name": "ERP", "maxDelaySeconds": None},
        {"code": "invoice-created", "name": "Fatura", "maxDelaySeconds": 2}]}
    require(api("/flows", flow)); check("Aynı akış yeniden oluşturulamaz", api("/flows", flow)[0] == 409)
    for name, kind, target in [("Yerel e-posta", "email", "test@local.test"), ("Yerel webhook", "webhook", "http://webhook:8080/hook")]:
        require(api("/channels", {"name": name, "kind": kind, "target": target, "secret": env["LOCAL_WEBHOOK_SECRET"] if kind == "webhook" else None}))
    key = require(api("/keys", {"name": "Test gönderimi", "scope": "ingest"}))
    read = require(api("/keys", {"name": "Test okuma", "scope": "read"}))
    ingest = {"X-API-Key": key["key"]}; readonly = {"X-API-Key": read["key"]}
    check("Gönderim anahtarı akışları okuyamaz", api("/flows", auth=ingest)[0] == 403)
    check("Okuma anahtarı akışları okuyabilir", api("/flows", auth=readonly)[0] == 200)
    temporary_password = secrets.token_urlsafe(24)
    roles = {}
    for role in ("viewer", "operator"):
        address = f"{role}-{suffix}@local.test"
        require(api("/members", {"email": address, "name": "Test " + role, "role": role, "password": temporary_password}))
        user_token = require(request(base, "/api/v1/auth/login", {"email": address, "password": temporary_password}))["token"]
        roles[role] = {"Authorization": "Bearer " + user_token, "X-Project-Id": project["id"]}
    check("İzleyici akış oluşturamaz", api("/flows", flow, roles["viewer"])[0] == 403)
    check("Operatör kullanıcı yönetemez", api("/members", auth=roles["operator"])[0] == 403)
    me = require(api("/me/user"))
    check("Son proje sahibi kaldırılamaz", api("/members/" + me["id"], method="DELETE")[0] == 409)
    check("Özel ağdaki izinsiz webhook reddedilir", api("/channels", {"name": "Reddedilecek", "kind": "webhook", "target": "http://169.254.169.254/", "secret": env["LOCAL_WEBHOOK_SECRET"]})[0] == 400)
    require(request("http://127.0.0.1:8082", "/test/fail-next", {"count": 1}))
    def event(tx, step, status="completed", age=0):
        return {"eventId": str(uuid.uuid4()), "flow": "v1-check", "transactionId": tx, "step": step,
            "status": status, "occurredAt": (datetime.now(timezone.utc) - timedelta(seconds=age)).isoformat(),
            "error": "Yerel fatura hatası" if status == "failed" else None}
    first = event("V1-DELAY-" + suffix, "erp-created", age=5)
    check("Okuma anahtarı bildirim gönderemez", api("/events", first, readonly)[0] == 403)
    check("Bildirim kabul edildi", api("/events", first, ingest)[0] == 202)
    check("Aynı eventId ve içerik tekrar kaydedilmez", require(api("/events", first, ingest))["duplicate"] is True)
    check("Aynı eventId farklı içerikle reddedilir", api("/events", first | {"step": "invoice-created"}, ingest)[0] == 409)
    def detail(tx): return require(api("/flows/v1-check/transactions/" + tx))
    wait("Süre aşımı ayrı gecikme durumudur", lambda: detail(first["transactionId"])["delay"]["state"] == "overdue")
    check("Gecikme işlemi failed yapmaz", detail(first["transactionId"])["status"] == "inProgress")
    alarms = require(api("/alarms?flow=v1-check&state=open"))["items"]
    check("Bir açık gecikme alarmı oluştu", len(alarms) == 1)
    aid = alarms[0]["id"]
    require(api(f"/alarms/{aid}/acknowledge", {}, roles["operator"]))
    require(api(f"/alarms/{aid}/acknowledge", {}, roles["operator"]))
    alarm = require(api("/alarms/" + aid))
    check("Gördüm alarmı kapatmaz ve tekrarlanmaz", alarm["alarm"]["resolved_at"] is None and sum(e["kind"] == "acknowledged" for e in alarm["events"]) == 1)
    require(api("/events", event(first["transactionId"], "invoice-created"), ingest), (202,))
    wait("Geç tamamlanma alarmı kapatır", lambda: require(api("/alarms/" + aid))["alarm"]["resolution"] == "step_completed")
    wait("Açılış ve kapanış iki kanala gönderildi", lambda: len((d := require(api("/alarms/" + aid))["deliveries"])) == 4 and all(x["state"] == "sent" for x in d))
    check("Webhook başarısız gönderimden sonra tekrar denendi", any(d["attempts"] >= 2 for d in require(api("/alarms/" + aid))["deliveries"]))
    failure = event("V1-FAIL-" + suffix, "invoice-created", "failed")
    require(api("/events", failure, ingest), (202,))
    wait("Hata bildirimi failed alarmı açtı", lambda: any(a["kind"] == "failed" for a in require(api("/alarms?flow=v1-check&state=open"))["items"]))
    require(api("/events", event(failure["transactionId"], "invoice-created"), ingest), (202,))
    wait("Düzelen adımın hata alarmı kapandı", lambda: not any(a["kind"] == "failed" for a in require(api("/alarms?flow=v1-check&state=open"))["items"]))
    # Invoice arriving before ERP is a valid delivery order.
    reversed_tx = "V1-REVERSE-" + suffix
    require(api("/events", event(reversed_tx, "invoice-created"), ingest), (202,))
    require(api("/events", event(reversed_tx, "erp-created", age=1), ingest), (202,))
    wait("Ters sırada ulaşan bildirimler tamamlandı", lambda: detail(reversed_tx)["status"] == "completed")
    foreign = require(api("/me/projects", {"name": "V1 izolasyon " + suffix}))
    check("İzleyici diğer projeye erişemez", api("/flows", auth=roles["viewer"] | {"X-Project-Id": foreign["id"]})[0] == 403)
    check("Alarm kimliği diğer projede görünmez", api("/alarms/" + aid, auth=session | {"X-Project-Id": foreign["id"]})[0] == 404)
    bulk = flow | {"code": "v1-load", "name": "Yerel yük denemesi", "steps": [flow["steps"][0], flow["steps"][1] | {"maxDelaySeconds": 86400}]}
    require(api("/flows", bulk))
    def send_pair(i):
        for step in ("erp-created", "invoice-created"):
            require(api("/events", event(f"V1-LOAD-{suffix}-{i}", step) | {"flow": "v1-load"}, ingest), (202,))
    started = time.monotonic()
    with ThreadPoolExecutor(max_workers=4) as pool: list(pool.map(send_pair, range(args.load)))
    print(f"{args.load * 2} bildirim kabulü: {time.monotonic() - started:.2f} sn (bu bilgisayardaki ölçüm)")
    wait("Toplu işlemler tamamlandı", lambda: require(api("/transactions/summary?flow=v1-load"))["completed"] == args.load, timeout=180)
    check("Sayfalama sınırı çalışıyor", len(require(api("/transactions?flow=v1-load&page=1&pageSize=10"))["items"]) == min(10, args.load))
    require(api("/keys/" + key["id"], method="DELETE"))
    check("İptal edilen anahtar çalışmıyor", api("/events", first, ingest)[0] == 401)
    source_project = env.get("PILOT_PROJECT_ID")
    if args.faults and not source_project:
        raise SystemExit("Kesinti testi için önce setup_v1.py ve kaynak örneği kurulmalı.")
    if source_project:
        source_headers = session | {"X-Project-Id": source_project}
        tx = "DEMO-" + suffix
        require(request("http://127.0.0.1:8081", "/orders", {"transactionId": tx}))
        require(request("http://127.0.0.1:8081", "/orders/" + tx + "/invoice", {}))
        wait("Kaynak .NET uygulaması uçtan uca tamamlandı", lambda: api(f"/flows/pilot-source/transactions/{tx}", auth=source_headers)[1].get("status") == "completed")
    if args.faults:
        def compose(*parts): subprocess.run(["docker", "compose", *parts], cwd=ROOT, check=True, stdout=subprocess.DEVNULL)
        tx = "DEMO-OFFLINE-" + suffix
        try:
            compose("stop", "api")
            require(request("http://127.0.0.1:8081", "/orders", {"transactionId": tx}))
            check("IntegraOps kapalıyken kaynak sipariş kaydı sürüyor", True)
        finally: compose("start", "api")
        def ready():
            try: return request(base, "/health/ready")[0] == 200
            except OSError: return False
        wait("API yeniden hazır", ready)
        wait("Kaynak kuyruğu bağlantı gelince aktarır", lambda: api(f"/flows/pilot-source/transactions/{tx}", auth=source_headers)[0] == 200, timeout=150)
        try:
            compose("stop", "rabbitmq")
            event1 = event("V1-BROKER-" + suffix, "erp-created") | {"flow": "v1-load"}
            require(api("/events", event1), (202,))
            wait("RabbitMQ kapalıyken API bildirimi veritabanında tutar", lambda: require(api("/operations"))["counts"]["outbox_pending"] > 0)
        finally: compose("start", "rabbitmq")
        wait("RabbitMQ dönüşünde bekleyen bildirim işlenir", lambda: require(api("/flows/v1-load/transactions/" + event1["transactionId"]))["processedEventCount"] == 1, timeout=180)
    require(api("/me/logout", method="POST"))
    check("Çıkış oturumu geçersiz kılar", api("/flows")[0] == 401)
    print(f"\n{passed} kontrol geçti. Panelde '{project['name']}' projesini seçerek sonuçları inceleyin.")

if __name__ == "__main__":
    main()
```

## 18. scripts/backup_local.sh

**YENİ** — Veritabanını ve Data Protection anahtarlarını yedekler.

```bash
#!/usr/bin/env bash
set -euo pipefail
umask 077
cd "$(dirname "$0")/.."
backup_dir="backups/$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$backup_dir"
docker compose exec -T postgres pg_dump -U integraops -d integraops -Fc > "$backup_dir/integraops.dump"
docker compose exec -T api tar -C /var/lib/integraops/keys -czf - . > "$backup_dir/protection-keys.tar.gz"
if docker compose --profile demo ps --status running --services | grep -qx source-postgres; then
  docker compose exec -T source-postgres pg_dump -U source -d source -Fc > "$backup_dir/source.dump"
fi
test -s "$backup_dir/integraops.dump"
test -s "$backup_dir/protection-keys.tar.gz"
echo "Yedek hazır: $backup_dir"
```

## 19. scripts/check_restore.sh

**YENİ** — Yedeği yeni bir kontrol veritabanına açar; asıl veritabanını değiştirmez.

```bash
#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
backup_dir="${1:?Kullanım: bash scripts/check_restore.sh backups/ZAMAN}"
test -s "$backup_dir/integraops.dump"
test -s "$backup_dir/protection-keys.tar.gz"
restore_db="restore_check_$(date -u +%Y%m%d%H%M%S)_$$"
# Only a newly created, uniquely named database is touched; never the working database.
docker compose exec -T postgres createdb -U integraops "$restore_db"
docker compose exec -T postgres pg_restore -U integraops -d "$restore_db" --exit-on-error --no-owner < "$backup_dir/integraops.dump"
docker compose exec -T postgres psql -U integraops -d "$restore_db" -v ON_ERROR_STOP=1 -c \
  "SELECT 'transactions' AS kind,count(*) FROM transactions UNION ALL SELECT 'alarms',count(*) FROM alarms UNION ALL SELECT 'users',count(*) FROM app_users;"
tar -tzf "$backup_dir/protection-keys.tar.gz" > /dev/null
echo "Veritabanı geri yükleme ve anahtar arşivi okuma başarılı. Kontrol veritabanı: $restore_db"
echo "İnceledikten sonra yalnızca bu kontrol veritabanını kaldırabilirsiniz: docker compose exec postgres dropdb -U integraops $restore_db"
```

## 20. scripts/recover_pending_events.sql

**YENİ** — Yedekten dönüşte henüz işlenmemiş olayların tekrar yayımlanmasını sağlar.

```sql
-- Apply only during recovery, while the worker is stopped.
-- A restored DB can remember a RabbitMQ publish whose queue no longer exists.
-- Re-publish only events whose projection has not been processed.
BEGIN;
UPDATE outbox_messages o
SET published_at = NULL
FROM transaction_events e
WHERE e.project_id = o.project_id
  AND e.event_id = o.event_id
  AND e.processed_at IS NULL;
COMMIT;
SELECT count(*) AS pending_publications FROM outbox_messages WHERE published_at IS NULL;
```

## 21. scripts/init_local.py

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```python
"""Create local development secrets without overwriting existing configuration."""
from pathlib import Path
import secrets

root = Path(__file__).resolve().parents[1]
target = root / ".env"
if target.exists():
    raise SystemExit(".env zaten var; mevcut ayarlar korundu.")
values = {
    "POSTGRES_PASSWORD": secrets.token_hex(24),
    "RABBITMQ_PASSWORD": secrets.token_hex(24),
    "BOOTSTRAP_KEY": secrets.token_hex(32),
    "POSTGRES_PORT": "54329",
    "RABBITMQ_PORT": "56729",
    "RABBITMQ_MANAGEMENT_PORT": "15679",
    "API_PORT": "8080",
}
with target.open("x", encoding="utf-8") as output:
    output.write("\n".join(f"{key}={value}" for key, value in values.items()) + "\n")
target.chmod(0o600)
print("Yerel ayarlar hazır. Sonraki komut: docker compose up --build -d")
```

## 22. scripts/verify_api.py

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```python
"""Exercise the real API + PostgreSQL + RabbitMQ + worker. Python standard library only."""
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timedelta, timezone
from pathlib import Path
import json
import os
import time
import urllib.error
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parents[1]


def local_values():
    values = {}
    path = ROOT / ".env"
    if path.exists():
        for line in path.read_text().splitlines():
            if line.strip() and not line.lstrip().startswith("#") and "=" in line:
                key, value = line.split("=", 1)
                values[key.strip()] = value.strip()
    return values


def main():
    values = local_values()
    base = os.environ.get("INTEGRAOPS_API_URL", f"http://127.0.0.1:{values.get('API_PORT', '8080')}")
    bootstrap_key = os.environ.get("BOOTSTRAP_KEY", values.get("BOOTSTRAP_KEY"))
    if not bootstrap_key:
        raise SystemExit("Önce python3 scripts/init_local.py çalıştırın veya BOOTSTRAP_KEY tanımlayın.")

    def request(method, path, body=None, api_key=None, bootstrap=False, expected=200):
        headers = {"Content-Type": "application/json"}
        if api_key:
            headers["X-API-Key"] = api_key
        if bootstrap:
            headers["X-Bootstrap-Key"] = bootstrap_key
        payload = None if body is None else json.dumps(body).encode()
        req = urllib.request.Request(base + path, data=payload, headers=headers, method=method)
        try:
            with urllib.request.urlopen(req, timeout=20) as response:
                code, data = response.status, response.read()
        except urllib.error.HTTPError as error:
            code, data = error.code, error.read()
        if code != expected:
            raise RuntimeError(f"{method} {path}: beklenen HTTP {expected}, gelen HTTP {code}")
        return json.loads(data) if data else None

    ready_until = time.monotonic() + 90
    while True:
        try:
            request("GET", "/health/ready")
            break
        except (OSError, RuntimeError):
            if time.monotonic() >= ready_until:
                raise SystemExit("API hazır olmadı. docker compose logs api migrate komutuyla kontrol edin.")
            time.sleep(1)

    def provision(label):
        return request("POST", "/api/v1/bootstrap/projects", {
            "organizationName": f"Demo {label}", "projectName": "Commerce"
        }, bootstrap=True, expected=201)

    a, b = provision("A"), provision("B")
    steps = ["order-created", "payment-completed", "erp-completed", "invoice-created", "cargo-created"]
    names = ["Sipariş", "Ödeme", "ERP", "Fatura", "Kargo"]
    delays = [None, 300, 300, 600, 900]
    flow = {"code": "order-flow", "name": "Sipariş akışı", "steps": [
        {"code": code, "name": name, "maxDelaySeconds": delay}
        for code, name, delay in zip(steps, names, delays)
    ]}
    for project in [a, b]:
        request("POST", "/api/v1/flows", flow, api_key=project["apiKey"], expected=201)

    timeline_path = "/api/v1/flows/order-flow/transactions/ORDER-10092"
    request("GET", timeline_path, expected=401)
    start = datetime.now(timezone.utc) - timedelta(minutes=10)

    def event(step, minute, status="completed"):
        return {"eventId": str(uuid.uuid4()), "flow": "order-flow", "transactionId": "ORDER-10092",
                "step": step, "status": status,
                "occurredAt": (start + timedelta(minutes=minute)).isoformat()}

    first = event(steps[0], 0)
    receipt = request("POST", "/api/v1/events", first, api_key=a["apiKey"], expected=202)
    duplicate = request("POST", "/api/v1/events", first, api_key=a["apiKey"])
    assert duplicate["duplicate"] is True
    assert duplicate["transactionRecordId"] == receipt["transactionRecordId"]
    request("POST", "/api/v1/events", {**first, "status": "failed"}, api_key=a["apiKey"], expected=409)
    request("GET", timeline_path, api_key=b["apiKey"], expected=404)

    # Concurrent and reverse-order deliveries must merge into the same transaction.
    remaining = [event(step, i) for i, step in enumerate(steps[1:], 1)]
    with ThreadPoolExecutor(max_workers=4) as pool:
        results = list(pool.map(lambda item: request("POST", "/api/v1/events", item,
                            api_key=a["apiKey"], expected=202), reversed(remaining)))
    assert all(item["transactionRecordId"] == receipt["transactionRecordId"] for item in results)

    def wait_processed(key, count):
        deadline = time.monotonic() + 45
        while True:
            result = request("GET", timeline_path, api_key=key)
            if result["processedEventCount"] == count:
                return result
            if time.monotonic() >= deadline:
                raise RuntimeError("Kayıt kabul edildi fakat işleyici süresinde tamamlamadı. Worker/RabbitMQ loglarını kontrol edin.")
            time.sleep(0.3)

    result = wait_processed(a["apiKey"], 5)
    assert result["acceptedEventCount"] == 5
    assert result["status"] == "completed"
    assert all(step["observation"] == "completed" for step in result["steps"])

    # An older failure arriving late must not regress a later success.
    request("POST", "/api/v1/events", event("erp-completed", 1, "failed"), api_key=a["apiKey"], expected=202)
    assert wait_processed(a["apiKey"], 6)["status"] == "completed"

    # Event and business IDs can be identical across independent projects.
    second_tenant = request("POST", "/api/v1/events", first, api_key=b["apiKey"], expected=202)
    assert second_tenant["transactionRecordId"] != receipt["transactionRecordId"]
    assert wait_processed(b["apiKey"], 1)["status"] == "inProgress"

    request("POST", "/api/v1/events", {**event("unknown-step", 1)}, api_key=a["apiKey"], expected=400)
    request("POST", "/api/v1/events", {**event(steps[0], 0), "occurredAt": "2026-09-07T10:00:00"},
            api_key=a["apiKey"], expected=400)

    private_dir = ROOT / ".local"
    private_dir.mkdir(exist_ok=True)
    credentials_path = private_dir / "demo-project.json"
    credentials_path.write_text(json.dumps(a, indent=2), encoding="utf-8")
    credentials_path.chmod(0o600)
    print("Başarılı: kalıcı olay işleme, tekrar gönderim, içerik çakışması, eşzamanlılık, sıra dışı teslim ve proje ayrımı.")
    print("Demo erişimi .local/demo-project.json dosyasında; bu dosya paylaşılmamalı.")
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
```

## 23. scripts/verify_delays.py

**REFERANS** — Mevcut yapının tamamlayıcı dosyası.

```python
"""Local, real-service delay demo. Creates its own demo project; uses Python stdlib."""
from datetime import datetime, timezone
import json
import os
import time
import urllib.error
import urllib.request
import uuid

from verify_api import local_values


def main():
    values = local_values()
    base = os.environ.get("INTEGRAOPS_API_URL", f"http://127.0.0.1:{values.get('API_PORT', '8080')}")
    bootstrap_key = os.environ.get("BOOTSTRAP_KEY", values.get("BOOTSTRAP_KEY"))
    if not bootstrap_key:
        raise SystemExit("Mevcut .env dosyasında BOOTSTRAP_KEY bulunamadı. Anahtarları yeniden üretmeyin.")

    def request(method, path, body=None, key=None, bootstrap=False, expected=200):
        headers = {"Content-Type": "application/json"}
        if key:
            headers["X-API-Key"] = key
        if bootstrap:
            headers["X-Bootstrap-Key"] = bootstrap_key
        req = urllib.request.Request(base + path, method=method, headers=headers,
            data=None if body is None else json.dumps(body).encode())
        try:
            with urllib.request.urlopen(req, timeout=15) as response:
                status, data = response.status, response.read()
        except urllib.error.HTTPError as error:
            status, data = error.code, error.read()
        if status != expected:
            raise RuntimeError(f"{method} {path}: HTTP {expected} bekleniyordu, {status} geldi.")
        return json.loads(data) if data else None

    ready_until = time.monotonic() + 60
    while True:
        try:
            request("GET", "/health/ready")
            break
        except (OSError, RuntimeError):
            if time.monotonic() >= ready_until:
                raise SystemExit("API hazır değil. docker compose logs --tail=50 api migrate ile kontrol edin.")
            time.sleep(1)

    project = request("POST", "/api/v1/bootstrap/projects", {
        "organizationName": "Gecikme modülü denemesi", "projectName": "Ayrı demo projesi"
    }, bootstrap=True, expected=201)
    key = project["apiKey"]
    request("POST", "/api/v1/flows", {
        "code": "delay-demo", "name": "ERP → Onay → Fatura → Kargo",
        "steps": [
            {"code": "erp-completed", "name": "ERP", "maxDelaySeconds": None},
            {"code": "approval-completed", "name": "Onay", "maxDelaySeconds": 30},
            {"code": "invoice-created", "name": "Fatura", "maxDelaySeconds": 60},
            {"code": "cargo-created", "name": "Kargo", "maxDelaySeconds": 120}
        ]
    }, key=key, expected=201)
    path = "/api/v1/flows/delay-demo/transactions/DELAY-DEMO-1"

    def submit(step, status="completed"):
        body = {"eventId": str(uuid.uuid4()), "flow": "delay-demo", "transactionId": "DELAY-DEMO-1",
            "step": step, "status": status, "occurredAt": datetime.now(timezone.utc).isoformat()}
        if status == "failed":
            body["error"] = "Demo: dış sistem fatura oluşturamadı."
        request("POST", "/api/v1/events", body, key=key, expected=202)
        return body

    def wait_for(state, step, count, timeout=45):
        until = time.monotonic() + timeout
        while True:
            result = request("GET", path, key=key)
            delay = result.get("delay") or {}
            if (result["processedEventCount"] == count and delay.get("state") == state
                    and delay.get("step") == step):
                return result
            if time.monotonic() >= until:
                raise RuntimeError(f"Beklenen: {state}/{step}; gelen: {delay.get('state')}/{delay.get('step')}. "
                    "docker compose logs --tail=50 worker ile kontrol edin.")
            time.sleep(0.5)

    def show(label, result):
        print(label, flush=True)
        print(json.dumps({"status": result["status"], "delay": result["delay"]},
            ensure_ascii=False, indent=2), flush=True)

    print("Ayrı demo projesi açıldı. Süreler sadece bu demo akışına ait: 30 / 60 / 120 saniye.", flush=True)
    first = submit("erp-completed")
    waiting = wait_for("waiting", "approval-completed", 1, timeout=20)
    show("1. ERP tamamlandı; onay bekleniyor.", waiting)
    print("2. Yeni olay göndermeden yaklaşık 30–35 saniyede gecikme taramasını gözleyeceğiz.", flush=True)
    overdue = wait_for("overdue", "approval-completed", 1)
    assert overdue["status"] == "inProgress", "Gecikme, işlem durumunu hataya çevirmemeli."
    show("Süre doldu: onay gecikmiş, işlem hâlâ devam ediyor.", overdue)

    receipt = request("POST", "/api/v1/events", first, key=key)
    assert receipt["duplicate"] is True
    unchanged = wait_for("overdue", "approval-completed", 1)
    assert unchanged["delay"]["deadlineAt"] == waiting["delay"]["deadlineAt"]

    approval = submit("approval-completed")
    invoice = wait_for("waiting", "invoice-created", 2)
    deadline = datetime.fromisoformat(invoice["delay"]["deadlineAt"].replace("Z", "+00:00"))
    anchor = datetime.fromisoformat(approval["occurredAt"])
    assert abs((deadline - anchor).total_seconds() - 60) < 0.001
    show("3. Onay geç geldi. Onay gecikmesi kalktı; fatura için yeni 60 saniye başladı.", invoice)

    submit("invoice-created", "failed")
    failed = wait_for("none", None, 3)
    assert failed["status"] == "failed"
    show("4. Dış sistem açıkça hata bildirdi: failed, gecikme değil.", failed)

    submit("invoice-created")
    show("5. Yeni başarı bildirimi geldi; kargo için 120 saniye başladı.",
        wait_for("waiting", "cargo-created", 4))
    submit("cargo-created")
    completed = wait_for("none", None, 5)
    assert completed["status"] == "completed"
    show("6. Bütün adımlar tamamlandı; bekleyen adım kalmadı.", completed)
    print("Başarılı: periyodik tarama, adım bazlı süre, tekrar gönderim, geç bildirim ve hata ayrımı.")


if __name__ == "__main__":
    main()
```
