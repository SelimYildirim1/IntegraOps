namespace IntegraOps.Application;

public sealed record FlowListItem(
    Guid Id, string Code, string Name, int StepCount);

public sealed record TransactionSummary(
    long Total, long Pending, long InProgress,
    long Completed, long Failed, long Overdue,
    DateTimeOffset QueriedAt, long Cancelled = 0);

public interface IPanelQueries
{
    Task<IReadOnlyList<FlowListItem>> ListFlowsAsync(
        Guid projectId, CancellationToken ct);

    Task<TransactionSummary> GetSummaryAsync(
        Guid projectId, string? flow, string? test, CancellationToken ct);
}
