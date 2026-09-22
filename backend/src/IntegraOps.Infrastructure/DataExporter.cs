using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;
using static IntegraOps.Infrastructure.PilotDb;

namespace IntegraOps.Infrastructure;

/// <summary>
/// Produces a customer's data export: one JSON Lines file per table inside a zip, plus a manifest.
/// Secrets (password hashes, key hashes, protected channel targets) are never included.
/// </summary>
public sealed class DataExporter(NpgsqlDataSource source, IConfiguration configuration)
{
    public string Directory => configuration["Exports:Path"] ?? Path.Combine(AppContext.BaseDirectory, "exports");
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static readonly (string Name, string Sql)[] Tables =
    [
        ("organization", "SELECT id,name,plan,created_at FROM organizations WHERE id=@o"),
        ("members", "SELECT u.email,u.name,m.role,m.created_at FROM organization_members m JOIN app_users u ON u.id=m.user_id WHERE m.organization_id=@o"),
        ("projects", "SELECT id,name,created_at FROM projects WHERE organization_id=@o"),
        ("project_members", "SELECT m.project_id,u.email,m.role FROM project_members m JOIN app_users u ON u.id=m.user_id JOIN projects p ON p.id=m.project_id WHERE p.organization_id=@o"),
        ("flows", "SELECT f.id,f.project_id,f.code,f.name,f.created_at FROM flows f JOIN projects p ON p.id=f.project_id WHERE p.organization_id=@o"),
        ("flow_steps", "SELECT s.* FROM flow_steps s JOIN flows f ON f.id=s.flow_id JOIN projects p ON p.id=f.project_id WHERE p.organization_id=@o"),
        ("flow_versions", "SELECT v.* FROM flow_versions v JOIN flows f ON f.id=v.flow_id JOIN projects p ON p.id=f.project_id WHERE p.organization_id=@o"),
        ("transactions", "SELECT t.* FROM transactions t JOIN projects p ON p.id=t.project_id WHERE p.organization_id=@o"),
        ("transaction_events", "SELECT e.* FROM transaction_events e JOIN projects p ON p.id=e.project_id WHERE p.organization_id=@o"),
        ("alarms", "SELECT a.* FROM alarms a JOIN projects p ON p.id=a.project_id WHERE p.organization_id=@o"),
        ("alarm_notes", "SELECT n.* FROM alarm_notes n JOIN alarms a ON a.id=n.alarm_id JOIN projects p ON p.id=a.project_id WHERE p.organization_id=@o"),
        ("connections", "SELECT c.id,c.project_id,c.name,c.kind,c.template,c.created_at,c.last_event_at,c.events_total,c.rejected_total FROM connections c JOIN projects p ON p.id=c.project_id WHERE p.organization_id=@o"),
        ("api_keys", "SELECT k.id,k.project_id,k.name,k.scope,k.key_prefix,k.created_at,k.revoked_at,k.expires_at FROM api_keys k JOIN projects p ON p.id=k.project_id WHERE p.organization_id=@o"),
        ("notification_channels", "SELECT c.id,c.project_id,c.name,c.kind,c.enabled,c.created_at FROM notification_channels c JOIN projects p ON p.id=c.project_id WHERE p.organization_id=@o"),
        ("reconciliation_runs", "SELECT r.* FROM reconciliation_runs r JOIN projects p ON p.id=r.project_id WHERE p.organization_id=@o"),
        ("reconciliation_discrepancies", "SELECT d.* FROM reconciliation_discrepancies d JOIN projects p ON p.id=d.project_id WHERE p.organization_id=@o"),
        ("audit_log", "SELECT a.id,a.project_id,a.action,a.target,a.created_at,u.email actor FROM audit_log a LEFT JOIN app_users u ON u.id=a.actor_id JOIN projects p ON p.id=a.project_id WHERE p.organization_id=@o"),
        ("security_events", "SELECT id,kind,ip,user_agent,detail,created_at FROM security_events WHERE organization_id=@o"),
        ("usage_daily", "SELECT day,events,rejected FROM usage_daily WHERE organization_id=@o"),
    ];

    /// <summary>Runs at most one pending export; returns false when none was waiting.</summary>
    public async Task<bool> RunPending(CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var rows = await Rows(c, null, """
            UPDATE data_exports SET state='running' WHERE id=(SELECT id FROM data_exports WHERE state='pending' ORDER BY created_at LIMIT 1 FOR UPDATE SKIP LOCKED)
            RETURNING id,organization_id
            """, ct);
        if (rows.Count == 0) return false;
        var id = (Guid)rows[0]["id"]!; var org = (Guid)rows[0]["organization_id"]!;
        var path = Path.Combine(Directory, $"{org:N}-{id:N}.zip");
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var counts = new Dictionary<string, long>();
            await using (var file = File.Create(path))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                foreach (var (name, sql) in Tables)
                {
                    var entry = zip.CreateEntry(name + ".jsonl", CompressionLevel.Optimal);
                    await using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                    await using var cmd = Sql.Command(c, null, sql, ("o", org));
                    await using var reader = await cmd.ExecuteReaderAsync(ct);
                    long n = 0;
                    while (await reader.ReadAsync(ct))
                    {
                        var row = new Dictionary<string, object?>();
                        for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? null : Normalize(reader.GetValue(i));
                        await writer.WriteLineAsync(JsonSerializer.Serialize(row, Json));
                        n++;
                    }
                    counts[name] = n;
                }
                var manifest = zip.CreateEntry("manifest.json");
                await using var m = new StreamWriter(manifest.Open(), new UTF8Encoding(false));
                await m.WriteAsync(JsonSerializer.Serialize(new { export_id = id, organization_id = org, created_at = DateTimeOffset.UtcNow, format = "jsonl", tables = counts }, new JsonSerializerOptions { WriteIndented = true }));
            }
            var size = new FileInfo(path).Length;
            await Run(c, null, "UPDATE data_exports SET state='ready',path=@p,size_bytes=@s,completed_at=clock_timestamp(),expires_at=clock_timestamp()+interval '7 days' WHERE id=@id", ct,
                ("p", path), ("s", size), ("id", id));
            await PostgresOrganizationStore.Security(c, null, null, org, "data.export_ready", new RequestContext(null, null), $"{size} bayt", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            try { File.Delete(path); } catch { /* best effort */ }
            await Run(c, null, "UPDATE data_exports SET state='failed',error=@e,completed_at=clock_timestamp() WHERE id=@id", ct, ("e", ex.GetType().Name), ("id", id));
        }
        return true;
    }

    /// <summary>Removes export files past their expiry so customer data does not linger on disk.</summary>
    public async Task<int> RemoveExpired(CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var rows = await Rows(c, null, "SELECT id,path FROM data_exports WHERE state='ready' AND expires_at<=clock_timestamp() AND path IS NOT NULL", ct);
        foreach (var r in rows)
        {
            try { File.Delete((string)r["path"]!); } catch { /* already gone */ }
            await Run(c, null, "UPDATE data_exports SET path=NULL WHERE id=@id", ct, ("id", r["id"]));
        }
        return rows.Count;
    }

    private static object? Normalize(object value) => value switch
    {
        DateTime d => new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)),
        string s when s.Length > 0 && (s[0] == '{' || s[0] == '[') && TryJson(s, out var element) => element,
        _ => value,
    };

    private static bool TryJson(string s, out JsonElement element)
    {
        try { element = JsonDocument.Parse(s).RootElement.Clone(); return true; }
        catch (JsonException) { element = default; return false; }
    }
}
