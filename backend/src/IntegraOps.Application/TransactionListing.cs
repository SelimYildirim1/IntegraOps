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
