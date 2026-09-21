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
