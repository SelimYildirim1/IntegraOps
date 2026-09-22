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
                if (i > 0 && (step.MaxDelaySeconds is null or < 1 or > 2592000))
                    errors[$"steps[{i}].maxDelaySeconds"] = ["Önceki adımdan sonraki süre 1–2592000 saniye (30 gün) olmalı."];
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
        else if (request.Status == "skipped") status = EventStatus.Skipped;     // explicitly not performed; counts as done
        else if (request.Status == "cancelled") status = EventStatus.Cancelled; // the whole transaction is cancelled
        else errors["status"] = ["completed, failed, skipped veya cancelled gönderin."];

        DateTimeOffset occurredAt = default;
        if (request.OccurredAt is null || request.OccurredAt.Length > 40 ||
            !TimestampPattern().IsMatch(request.OccurredAt) ||
            !DateTimeOffset.TryParse(request.OccurredAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out occurredAt))
            errors["occurredAt"] = ["Saat dilimi içeren ISO 8601 zamanı gerekli; örnek: 2026-09-07T10:00:00Z."];
        else if (occurredAt > now.AddMinutes(5))
            errors["occurredAt"] = ["Olay zamanı sunucu saatinin 5 dakikadan fazla ilerisinde olamaz."];
        if (request.Error?.Length > 2000) errors["error"] = ["Hata açıklaması en fazla 2000 karakter olabilir."];
        var group = string.IsNullOrWhiteSpace(request.Group) ? null : request.Group.Trim();
        if (group is not null) Code(errors, "group", group);
        if (request.Status == "completed" && !string.IsNullOrWhiteSpace(request.Error))
            errors["error"] = ["Tamamlanan olayda hata açıklaması gönderilmemeli."];
        ThrowIfInvalid(errors);
        return new ValidatedEvent(request.EventId, request.Flow!, request.TransactionId!, request.Step!,
            status, occurredAt.ToUniversalTime(), string.IsNullOrWhiteSpace(request.Error) ? null : request.Error, group);
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
