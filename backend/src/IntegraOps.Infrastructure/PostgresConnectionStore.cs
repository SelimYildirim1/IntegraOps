using System.Text.Json;
using IntegraOps.Application;
using Npgsql;
using static IntegraOps.Infrastructure.PilotDb;
using static IntegraOps.Infrastructure.PilotSecurity;

namespace IntegraOps.Infrastructure;

public sealed record ConnectionInput(string? Name, string? Kind, string? Description, JsonElement? Template, string? TemplateCode);
public sealed record MappingTestInput(JsonElement? Template, JsonElement? Sample);
public sealed record RotateInput(int? GraceHours);

/// <summary>V2–V3 reusable integrations: connections, mapping templates, key rotation, usage and version tracking.</summary>
public sealed class PostgresConnectionStore(NpgsqlDataSource source, ITrackingStore tracking, TimeProvider clock)
{
    public const int MinimumAgentMajor = 0; // raise when an agent/sdk contract change makes old clients incompatible

    public async Task<object> List(Guid project, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var rows = await Rows(c, null, """
            SELECT co.id,co.name,co.kind,co.description,co.template,co.template_code,co.contract_version,co.client_name,co.client_version,
                co.events_total,co.rejected_total,co.last_event_at,co.last_ping_at,co.last_error,co.last_error_at,co.created_at,
                (SELECT count(*) FROM api_keys k WHERE k.connection_id=co.id AND k.revoked_at IS NULL AND (k.expires_at IS NULL OR k.expires_at>clock_timestamp())) active_keys,
                (SELECT max(k.last_used_at) FROM api_keys k WHERE k.connection_id=co.id) key_last_used_at,
                (SELECT seen_at FROM agent_heartbeats a WHERE a.project_id=co.project_id AND a.name=co.name) agent_seen_at,
                (SELECT version FROM agent_heartbeats a WHERE a.project_id=co.project_id AND a.name=co.name) agent_version
            FROM connections co WHERE co.project_id=@p ORDER BY co.name
            """, ct, ("p", project));
        foreach (var r in rows)
        {
            r["template"] = r["template"] is string t ? JsonDocument.Parse(t).RootElement : null;
            r["compatible"] = Compatible(r["client_version"] as string ?? r["agent_version"] as string);
        }
        return rows;
    }

    /// <summary>Clients report "name/version"; a missing or pre-release version is accepted but flagged for review.</summary>
    public static bool? Compatible(string? version)
    {
        if (string.IsNullOrEmpty(version)) return null;
        var v = version.TrimStart('v');
        var major = v.Split('.')[0];
        return int.TryParse(major, out var m) ? m >= MinimumAgentMajor : null;
    }

    public object Catalog() => EventMapper.Catalog.Select(t => new { code = t.Code, name = t.Name, description = t.Description, template = t.Template, sample = JsonDocument.Parse(t.Sample).RootElement });

    public async Task<object> Save(ProjectIdentity actor, Guid? id, ConnectionInput input, CancellationToken ct)
    {
        var name = Text(input.Name, 120, "Bağlantı adı");
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9][A-Za-z0-9 ._-]{0,119}$")) throw new PilotHttpException(400, "Bağlantı adı harf/rakamla başlamalı; harf, rakam, boşluk, nokta, alt çizgi ve tire kullanılabilir.");
        if (input.Kind is not ("direct" or "agent" or "sdk" or "mapped")) throw new PilotHttpException(400, "Bağlantı türü direct, agent, sdk veya mapped olmalı.");
        var description = (input.Description ?? "").Trim(); if (description.Length > 500) throw new PilotHttpException(400, "Açıklama en fazla 500 karakter.");
        string? template = null;
        if (input.Kind == "mapped")
        {
            if (input.Template is not { ValueKind: JsonValueKind.Object } t) throw new PilotHttpException(400, "Eşlemeli bağlantı için şablon gerekli.");
            EventMapper.Parse(t);
            template = t.GetRawText();
            if (template.Length > 4000) throw new PilotHttpException(400, "Şablon çok büyük.");
        }
        var code = string.IsNullOrWhiteSpace(input.TemplateCode) ? null : input.TemplateCode.Trim();
        if (code is { Length: > 40 }) throw new PilotHttpException(400, "Şablon kodu geçersiz.");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var connectionId = id ?? Guid.CreateVersion7();
        try
        {
            if (id is null)
                await Run(c, tx, "INSERT INTO connections(id,project_id,name,kind,description,template,template_code,created_by) VALUES(@id,@p,@n,@k,@d,CAST(@t AS jsonb),@tc,@u)", ct,
                    ("id", connectionId), ("p", actor.ProjectId), ("n", name), ("k", input.Kind), ("d", description), ("t", template), ("tc", code), ("u", actor.UserId));
            else if (await Run(c, tx, "UPDATE connections SET name=@n,kind=@k,description=@d,template=CAST(@t AS jsonb),template_code=@tc WHERE id=@id AND project_id=@p", ct,
                ("id", connectionId), ("p", actor.ProjectId), ("n", name), ("k", input.Kind), ("d", description), ("t", template), ("tc", code)) == 0)
                throw new PilotHttpException(404, "Bağlantı bulunamadı.");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation) { throw new PilotHttpException(409, "Bu bağlantı adı zaten kullanılıyor."); }
        await Audit(c, tx, actor.ProjectId, actor.UserId, id is null ? "connection.create" : "connection.update", connectionId.ToString(), ct);
        await tx.CommitAsync(ct);
        return new { id = connectionId, name };
    }

    public async Task Delete(ProjectIdentity actor, Guid id, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        if (await Value(c, tx, "SELECT 1 FROM api_keys WHERE connection_id=@id AND revoked_at IS NULL", ct, ("id", id)) is not null)
            throw new PilotHttpException(409, "Bağlantının etkin anahtarı var; önce anahtarları iptal edin.");
        await Run(c, tx, "UPDATE api_keys SET connection_id=NULL WHERE connection_id=@id", ct, ("id", id));
        if (await Run(c, tx, "DELETE FROM connections WHERE id=@id AND project_id=@p", ct, ("id", id), ("p", actor.ProjectId)) == 0)
            throw new PilotHttpException(404, "Bağlantı bulunamadı.");
        await Audit(c, tx, actor.ProjectId, actor.UserId, "connection.delete", id.ToString(), ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>Dry run: maps a sample payload and validates each event without storing anything.</summary>
    public object TestMapping(MappingTestInput input)
    {
        if (input.Template is not { ValueKind: JsonValueKind.Object } t) throw new PilotHttpException(400, "Şablon gerekli.");
        if (input.Sample is not { } sample || sample.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) throw new PilotHttpException(400, "Örnek veri gerekli.");
        var template = EventMapper.Parse(t);
        var mapped = EventMapper.Map(template, sample, 50);
        var now = clock.GetUtcNow();
        return new
        {
            events = mapped.Select(m =>
            {
                var problems = new List<string>();
                if (m.Problem is not null) problems.Add(m.Problem);
                try { InputValidation.Event(m.Event, now); }
                catch (RequestValidationException ex) { problems.AddRange(ex.Errors.Select(e => $"{e.Key}: {e.Value[0]}")); }
                return new { index = m.Index, m.Event, problems };
            }),
        };
    }

    /// <summary>Maps and accepts a raw payload through a mapped connection. Each record is accepted independently.</summary>
    public async Task<object> Ingest(ProjectIdentity actor, Guid connectionId, JsonElement payload, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var rows = await Rows(c, null, "SELECT kind,template FROM connections WHERE id=@id AND project_id=@p", ct, ("id", connectionId), ("p", actor.ProjectId));
        if (rows.Count == 0) throw new PilotHttpException(404, "Bağlantı bulunamadı.");
        if ((string)rows[0]["kind"]! != "mapped" || rows[0]["template"] is not string templateJson) throw new PilotHttpException(400, "Bu bağlantı eşlemeli değil.");
        if (actor.ConnectionId is { } bound && bound != connectionId) throw new PilotHttpException(403, "Bu anahtar başka bir bağlantıya ait.");
        var template = EventMapper.Parse(JsonDocument.Parse(templateJson).RootElement);
        var mapped = EventMapper.Map(template, payload);
        if (mapped.Count == 0) throw new PilotHttpException(400, "Şablon bu veride kayıt bulamadı.");
        var results = new List<object>();
        int accepted = 0, duplicates = 0, rejected = 0; string? firstProblem = null;
        foreach (var m in mapped)
        {
            string? problem = m.Problem; bool duplicate = false;
            if (problem is null)
            {
                try
                {
                    var receipt = await tracking.AcceptAsync(actor.ProjectId, InputValidation.Event(m.Event, clock.GetUtcNow()), ct);
                    duplicate = receipt.Duplicate;
                }
                catch (RequestValidationException ex) { problem = string.Join("; ", ex.Errors.Select(e => $"{e.Key}: {e.Value[0]}")); }
                catch (ResourceNotFoundException ex) { problem = ex.Message; }
                catch (ResourceConflictException ex) { problem = ex.Message; }
            }
            if (problem is not null) { rejected++; firstProblem ??= problem; } else if (duplicate) duplicates++; else accepted++;
            results.Add(new { index = m.Index, eventId = m.Event.EventId, transactionId = m.Event.TransactionId, step = m.Event.Step, duplicate, problem });
        }
        await Run(c, null, """
            UPDATE connections SET events_total=events_total+@a, rejected_total=rejected_total+@r,
                last_event_at=CASE WHEN @a>0 THEN clock_timestamp() ELSE last_event_at END,
                last_error=CASE WHEN @r>0 THEN @e ELSE last_error END, last_error_at=CASE WHEN @r>0 THEN clock_timestamp() ELSE last_error_at END
            WHERE id=@id
            """, ct, ("a", accepted + duplicates), ("r", rejected), ("e", firstProblem is { Length: > 200 } ? firstProblem[..200] : firstProblem), ("id", connectionId));
        return new { accepted, duplicates, rejected, results };
    }

    /// <summary>Records who called: key usage, connection activity and the client version from the User-Agent.</summary>
    public async Task RecordUsage(ProjectIdentity actor, string? userAgent, bool accepted, string? error, CancellationToken ct)
    {
        if (actor.KeyId is null) return;
        var (clientName, clientVersion) = ParseUserAgent(userAgent);
        await using var c = await source.OpenConnectionAsync(ct);
        await Run(c, null, "UPDATE api_keys SET last_used_at=clock_timestamp() WHERE id=@k", ct, ("k", actor.KeyId));
        if (actor.ConnectionId is null) return;
        await Run(c, null, """
            UPDATE connections SET client_name=COALESCE(@cn,client_name), client_version=COALESCE(@cv,client_version),
                events_total=events_total+CASE WHEN @ok THEN 1 ELSE 0 END, rejected_total=rejected_total+CASE WHEN @ok THEN 0 ELSE 1 END,
                last_event_at=CASE WHEN @ok THEN clock_timestamp() ELSE last_event_at END,
                last_error=CASE WHEN @ok THEN last_error ELSE @e END, last_error_at=CASE WHEN @ok THEN last_error_at ELSE clock_timestamp() END
            WHERE id=@id
            """, ct, ("cn", clientName), ("cv", clientVersion), ("ok", accepted), ("e", error is { Length: > 200 } ? error[..200] : error), ("id", actor.ConnectionId));
    }

    public async Task Ping(ProjectIdentity actor, string? userAgent, CancellationToken ct)
    {
        if (actor.KeyId is null) throw new PilotHttpException(400, "Ping yalnızca API anahtarıyla yapılır.");
        var (clientName, clientVersion) = ParseUserAgent(userAgent);
        await using var c = await source.OpenConnectionAsync(ct);
        await Run(c, null, "UPDATE api_keys SET last_used_at=clock_timestamp() WHERE id=@k", ct, ("k", actor.KeyId));
        if (actor.ConnectionId is { } id)
            await Run(c, null, "UPDATE connections SET last_ping_at=clock_timestamp(), client_name=COALESCE(@cn,client_name), client_version=COALESCE(@cv,client_version) WHERE id=@id", ct,
                ("cn", clientName), ("cv", clientVersion), ("id", id));
    }

    public static (string? Name, string? Version) ParseUserAgent(string? ua)
    {
        if (string.IsNullOrWhiteSpace(ua)) return (null, null);
        var first = ua.Trim().Split(' ')[0];
        var slash = first.IndexOf('/');
        var name = slash > 0 ? first[..slash] : first;
        var version = slash > 0 ? first[(slash + 1)..] : null;
        if (name.Length > 120) name = name[..120];
        if (version is { Length: > 64 }) version = version[..64];
        return (name, version);
    }

    /// <summary>Issues a replacement key with the same name, scope and connection; the old key expires after the grace period.</summary>
    public async Task<object> Rotate(ProjectIdentity actor, Guid keyId, RotateInput input, CancellationToken ct)
    {
        var grace = input.GraceHours ?? 24;
        if (grace is < 0 or > 720) throw new PilotHttpException(400, "Geçiş süresi 0–720 saat olmalı.");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var rows = await Rows(c, tx, "SELECT id,name,scope,connection_id,revoked_at,rotated_to FROM api_keys WHERE id=@id AND project_id=@p FOR UPDATE", ct, ("id", keyId), ("p", actor.ProjectId));
        if (rows.Count == 0) throw new PilotHttpException(404, "Anahtar bulunamadı.");
        var old = rows[0];
        if (old["revoked_at"] is not null) throw new PilotHttpException(409, "İptal edilmiş anahtar yenilenemez.");
        if (old["rotated_to"] is not null) throw new PilotHttpException(409, "Bu anahtar zaten yenilendi.");
        var scope = (string)old["scope"]!;
        if (scope == "legacy") scope = "ingest";
        var key = "io_v1_" + Token(); var id = Guid.CreateVersion7();
        await Run(c, tx, "INSERT INTO api_keys(id,project_id,name,scope,key_hash,key_prefix,connection_id) VALUES(@id,@p,@n,@s,@h,@prefix,CAST(@c AS uuid))", ct,
            ("id", id), ("p", actor.ProjectId), ("n", old["name"]), ("s", scope), ("h", Hash(key)), ("prefix", key[..16]), ("c", old["connection_id"]));
        await Run(c, tx, "UPDATE api_keys SET rotated_to=@new, expires_at=clock_timestamp()+make_interval(hours => @g) WHERE id=@id", ct, ("new", id), ("g", grace), ("id", keyId));
        await Audit(c, tx, actor.ProjectId, actor.UserId, "key.rotate", $"{keyId} -> {id} grace={grace}h", ct);
        await tx.CommitAsync(ct);
        return new { id, key, name = old["name"], scope, expiresOldAt = clock.GetUtcNow().AddHours(grace) };
    }
}
