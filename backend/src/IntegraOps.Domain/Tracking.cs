namespace IntegraOps.Domain;

public enum EventStatus { Completed, Failed }
public enum TransactionStatus { Pending, InProgress, Failed, Completed }

// Flow definitions are immutable in the first milestone. Editing/versioning is a later feature.
public sealed record FlowStep(string Code, string Name, int Position, int? MaxDelaySeconds);
public sealed record FlowDefinition(
    Guid Id, Guid ProjectId, string Code, string Name, IReadOnlyList<FlowStep> Steps);

public sealed record ObservedEvent(
    Guid EventId, string Step, EventStatus Status, DateTimeOffset OccurredAt,
    DateTimeOffset ReceivedAt, string? Error);

public sealed record TimelineStep(
    string Code, string Name, int Position, int? MaxDelaySeconds,
    string Observation, DateTimeOffset? OccurredAt, string? Error);

public sealed record Projection(TransactionStatus Status, IReadOnlyList<TimelineStep> Steps);

public static class TimelineProjector
{
    public static Projection Project(FlowDefinition flow, IReadOnlyList<ObservedEvent> events)
    {
        // Delivery order is not business order. EventId breaks exact timestamp ties deterministically.
        var latestByStep = events
            .GroupBy(e => e.Step, StringComparer.Ordinal)
            .ToDictionary(g => g.Key,
                g => g.OrderBy(e => e.OccurredAt).ThenBy(e => e.EventId).Last(),
                StringComparer.Ordinal);

        var steps = flow.Steps.OrderBy(s => s.Position).Select(step =>
        {
            latestByStep.TryGetValue(step.Code, out var observed);
            return new TimelineStep(step.Code, step.Name, step.Position, step.MaxDelaySeconds,
                observed is null ? "not_observed" : observed.Status == EventStatus.Completed ? "completed" : "failed",
                observed?.OccurredAt, observed?.Error);
        }).ToArray();

        var status = events.Count == 0 ? TransactionStatus.Pending
            : steps.Any(s => s.Observation == "failed") ? TransactionStatus.Failed
            : steps.All(s => s.Observation == "completed") ? TransactionStatus.Completed
            : TransactionStatus.InProgress;

        return new Projection(status, steps);
    }
}
