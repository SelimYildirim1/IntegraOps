using System.Globalization;
using IntegraOps.Domain;

namespace IntegraOps.Application;

/// <param name="Search">Case-insensitive match anywhere in the transaction id.</param>
/// <param name="Test">"exclude" (default), "include" or "only" for TEST- records.</param>
public sealed record TransactionListQuery(
    string? Flow, TransactionStatus? Status, string? Delay,
    int Page, int PageSize, string? Search = null,
    DateTimeOffset? From = null, DateTimeOffset? To = null, string Test = "exclude");

public sealed record TransactionListItem(
    Guid Id, string Flow, string TransactionId, TransactionStatus Status,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DelayView Delay, bool IsTest = false);

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
        int page = 1, int pageSize = 20, string? search = null,
        string? from = null, string? to = null, string? test = null)
    {
        var errors = new Dictionary<string, string[]>();
        flow = string.IsNullOrWhiteSpace(flow) ? null : flow.Trim();
        if (flow is not null) Code(errors, "flow", flow);
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (search is { Length: > 128 }) errors["search"] = ["Arama metni en fazla 128 karakter olabilir."];
        var fromAt = DateFilter(errors, "from", from);
        var toAt = DateFilter(errors, "to", to);
        if (fromAt is not null && toAt is not null && toAt < fromAt)
            errors["to"] = ["Bitiş zamanı başlangıçtan önce olamaz."];
        test = string.IsNullOrWhiteSpace(test) ? "exclude" : test.Trim().ToLowerInvariant();
        if (test is not ("exclude" or "include" or "only"))
            errors["test"] = ["exclude, include veya only kullanın."];

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
        return new TransactionListQuery(flow, parsedStatus, delay, page, pageSize, search, fromAt, toAt, test);
    }

    /// <summary>Accepts an ISO 8601 instant with offset or a plain date (interpreted as UTC midnight).</summary>
    public static DateTimeOffset? DateFilter(Dictionary<string, string[]> errors, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.Length <= 10 && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        if (value.Length <= 40 && TimestampPattern().IsMatch(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
            return at;
        errors[field] = ["Tarih için YYYY-AA-GG ya da saat dilimli ISO 8601 zamanı kullanın."];
        return null;
    }
}
