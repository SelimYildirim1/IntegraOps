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
    string State,
    string? Step,
    DateTimeOffset? StartedAt,
    DateTimeOffset? DeadlineAt,
    DateTimeOffset? CheckedAt
);
public sealed record TransactionView(
    Guid Id,
    string Flow,
    string TransactionId,
    TransactionStatus Status,
    int AcceptedEventCount,
    int ProcessedEventCount,
    IReadOnlyList<TimelineStep> Steps,
    IReadOnlyList<ObservedEvent> Events,
    DelayView? Delay = null
);

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
