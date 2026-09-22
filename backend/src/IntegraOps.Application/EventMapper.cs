using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IntegraOps.Application;

/// <summary>
/// Maps raw source payloads to IntegraOps events with a small template language.
/// Each target field is either a constant ("invoice-created"), a path into the payload ("$.order.no"),
/// or a path with a fallback ("$.customer_class|standard"). eventId may be omitted: it is then derived
/// deterministically from transactionId, step and occurredAt so retries stay idempotent.
/// </summary>
public sealed record MappingTemplate(
    string Flow, string TransactionId, string Step, string OccurredAt,
    string? EventId = null, string? Status = null, string? Error = null, string? Group = null,
    string? Records = null)
{
    public static readonly string[] Fields = ["flow", "transactionId", "step", "occurredAt", "eventId", "status", "error", "group", "records"];
}

public sealed record MappedEvent(SubmitEventRequest Event, int Index, string? Problem);

public static class EventMapper
{
    private static readonly Guid Namespace = Guid.Parse("6f4a3c2e-8b71-4d5e-9a0f-2c1d3e4f5a6b");

    public static MappingTemplate Parse(JsonElement template)
    {
        if (template.ValueKind != JsonValueKind.Object) throw new RequestValidationException(new() { ["template"] = ["Şablon bir nesne olmalı."] });
        string? Get(string name) => template.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var errors = new Dictionary<string, string[]>();
        foreach (var required in new[] { "flow", "transactionId", "step", "occurredAt" })
            if (string.IsNullOrWhiteSpace(Get(required))) errors[required] = ["Bu alan için sabit değer ya da $.yol gerekli."];
        foreach (var p in template.EnumerateObject())
            if (!MappingTemplate.Fields.Contains(p.Name)) errors[p.Name] = ["Bilinmeyen şablon alanı."];
        if (errors.Count > 0) throw new RequestValidationException(errors);
        return new MappingTemplate(Get("flow")!, Get("transactionId")!, Get("step")!, Get("occurredAt")!,
            Get("eventId"), Get("status"), Get("error"), Get("group"), Get("records"));
    }

    /// <summary>Maps one payload (or the array under <c>records</c>) to events; problems are reported per record.</summary>
    public static List<MappedEvent> Map(MappingTemplate t, JsonElement payload, int maxRecords = 500)
    {
        IEnumerable<JsonElement> records = payload.ValueKind == JsonValueKind.Array ? payload.EnumerateArray() : [payload];
        if (t.Records is not null)
        {
            var list = Resolve(payload, t.Records);
            records = list is { ValueKind: JsonValueKind.Array } arr ? arr.EnumerateArray() : [];
        }
        var result = new List<MappedEvent>();
        var index = 0;
        foreach (var record in records)
        {
            if (index >= maxRecords) break;
            string? problem = null;
            string Text(string spec, string field)
            {
                var value = Evaluate(record, payload, spec);
                if (value is null && problem is null) problem = $"{field}: şablon '{spec}' bu kayıtta değer bulamadı.";
                return value ?? "";
            }
            var flow = Text(t.Flow, "flow"); var transaction = Text(t.TransactionId, "transactionId");
            var step = Text(t.Step, "step"); var occurred = NormalizeTime(Text(t.OccurredAt, "occurredAt"));
            var status = t.Status is null ? "completed" : Text(t.Status, "status").ToLowerInvariant();
            var error = t.Error is null ? null : Evaluate(record, payload, t.Error);
            var group = t.Group is null ? null : Evaluate(record, payload, t.Group);
            // A source id is used when present (any string is accepted and hashed if it is not a UUID);
            // records without one get a deterministic id from the business fact itself.
            var sourceId = t.EventId is null ? null : Evaluate(record, payload, t.EventId);
            Guid eventId;
            if (sourceId is { Length: > 0 } raw) { if (!Guid.TryParse(raw, out eventId)) eventId = Derive("id\n" + raw); }
            else eventId = Derive($"{flow}\n{transaction}\n{step}\n{status}\n{occurred}");
            result.Add(new MappedEvent(new SubmitEventRequest(eventId, flow, transaction, step, status, occurred, error, group), index, problem));
            index++;
        }
        return result;
    }

    /// <summary>Constant, "$.path", or "$.path|fallback". Paths use dots and [n] for arrays; "$$." refers to the whole payload.</summary>
    public static string? Evaluate(JsonElement record, JsonElement root, string spec)
    {
        if (!spec.StartsWith('$')) return spec;
        var fallback = (string?)null;
        var bar = spec.IndexOf('|');
        if (bar > 0) { fallback = spec[(bar + 1)..]; spec = spec[..bar]; }
        var value = spec.StartsWith("$$.") ? Resolve(root, "$." + spec[3..]) : Resolve(record, spec);
        if (value is { } v && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            return v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => v.GetRawText(),
                _ => v.GetRawText(),
            };
        return fallback;
    }

    private static JsonElement? Resolve(JsonElement root, string path)
    {
        if (path == "$") return root;
        if (!path.StartsWith("$.")) return null;
        var current = root;
        foreach (var part in path[2..].Split('.'))
        {
            var name = part; var indexes = new List<int>();
            var bracket = part.IndexOf('[');
            if (bracket >= 0)
            {
                name = part[..bracket];
                foreach (var piece in part[bracket..].Split('[', StringSplitOptions.RemoveEmptyEntries))
                    if (int.TryParse(piece.TrimEnd(']'), out var i)) indexes.Add(i);
            }
            if (name.Length > 0)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out var next)) return null;
                current = next;
            }
            foreach (var i in indexes)
            {
                if (current.ValueKind != JsonValueKind.Array || i < 0 || i >= current.GetArrayLength()) return null;
                current = current[i];
            }
        }
        return current;
    }

    /// <summary>Accepts ISO 8601 with offset, "yyyy-MM-dd HH:mm:ss" (UTC assumed) and unix seconds/milliseconds.</summary>
    public static string NormalizeTime(string raw)
    {
        raw = raw.Trim();
        if (raw.Length == 0) return raw;
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
            return (unix > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(unix) : DateTimeOffset.FromUnixTimeSeconds(unix)).ToString("O");
        if (DateTimeOffset.TryParseExact(raw, ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm"], CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var naive))
            return naive.ToString("O");
        return raw; // validated later by InputValidation.Event
    }

    /// <summary>UUID version 5 (SHA-1, RFC 4122) of the name in the IntegraOps namespace.</summary>
    public static Guid Derive(string name)
    {
        var ns = Namespace.ToByteArray();
        SwapGuidBytes(ns);
        var hash = SHA1.HashData([.. ns, .. Encoding.UTF8.GetBytes(name)]);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        SwapGuidBytes(bytes);
        return new Guid(bytes);
    }

    private static void SwapGuidBytes(byte[] b)
    {
        (b[0], b[3]) = (b[3], b[0]); (b[1], b[2]) = (b[2], b[1]);
        (b[4], b[5]) = (b[5], b[4]); (b[6], b[7]) = (b[7], b[6]);
    }

    /// <summary>Starting points for common source shapes; the customer adjusts the paths.</summary>
    public static readonly IReadOnlyList<(string Code, string Name, string Description, MappingTemplate Template, string Sample)> Catalog =
    [
        ("generic-order", "Genel sipariş kaydı", "Sipariş numarası, adım kodu ve zaman alanlarını taşıyan düz JSON kayıtları.",
            new("erp-order-invoice", "$.order_no", "$.step", "$.occurred_at", "$.event_id", "$.status|completed", "$.error", "$.customer_class"),
            """{"event_id":"c1f3a1a0-1b2c-4d3e-8f90-123456789abc","order_no":"SO-1001","step":"invoice-created","occurred_at":"2026-09-22T10:15:00+03:00","status":"completed","customer_class":"vip"}"""),
        ("erp-document", "ERP belge olayı", "Belge başlığı ve durum alanı olan ERP çıktıları; adım belge türünden, zaman belge tarihinden alınır.",
            new("erp-order-invoice", "$.header.order_no", "$.header.doc_type", "$.header.posted_at", null, "$.header.state|completed", "$.header.message", "$.header.channel"),
            """{"header":{"order_no":"SO-1002","doc_type":"invoice-created","posted_at":"2026-09-22 10:15:00","state":"completed","channel":"b2b"}}"""),
        ("webhook-batch", "Toplu webhook", "Bir zarfın içinde birden çok olay taşıyan e-ticaret webhook'ları.",
            new("erp-order-invoice", "$.id", "$.type", "$.created_at", "$.uid", null, null, "$$.shop", "$.events"),
            """{"shop":"tr-store","events":[{"uid":"8f1c0d3e-1a2b-4c3d-9e8f-0a1b2c3d4e5f","id":"SO-1003","type":"order-created","created_at":1790073600},{"uid":"9f1c0d3e-1a2b-4c3d-9e8f-0a1b2c3d4e5f","id":"SO-1003","type":"invoice-created","created_at":1790077200}]}"""),
    ];
}
