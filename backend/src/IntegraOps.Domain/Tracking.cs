namespace IntegraOps.Domain;

public enum EventStatus { Completed, Failed }
public enum TransactionStatus { Pending, InProgress, Failed, Completed }

// A published flow version is immutable; a transaction keeps the version it started with.
// Overrides give a different allowance to transactions of a named group (for example "vip").
public sealed record FlowStep(string Code, string Name, int Position, int? MaxDelaySeconds,
    IReadOnlyDictionary<string, int>? Overrides = null)
{
    public int? AllowanceFor(string? group) =>
        group is not null && Overrides is not null && Overrides.TryGetValue(group, out var seconds) ? seconds : MaxDelaySeconds;
}
public sealed record FlowDefinition(
    Guid Id, Guid ProjectId, string Code, string Name, IReadOnlyList<FlowStep> Steps,
    Guid? VersionId = null, int Version = 1, BusinessCalendar? Calendar = null)
{
    /// <summary>Deadline for an allowance that starts at <paramref name="start"/>: working time when a calendar applies.</summary>
    public DateTimeOffset DeadlineFrom(DateTimeOffset start, int seconds) =>
        Calendar is null ? start.AddSeconds(seconds) : Calendar.AddWorkingSeconds(start, seconds);
    public long ElapsedSeconds(DateTimeOffset from, DateTimeOffset to) =>
        Calendar is null ? (long)Math.Round((to - from).TotalSeconds) : Calendar.WorkingSecondsBetween(from, to);
}

public sealed record ObservedEvent(
    Guid EventId, string Step, EventStatus Status, DateTimeOffset OccurredAt,
    DateTimeOffset ReceivedAt, string? Error);

public sealed record TimelineStep(
    string Code, string Name, int Position, int? MaxDelaySeconds,
    string Observation, DateTimeOffset? OccurredAt, string? Error);

public sealed record Projection(TransactionStatus Status, IReadOnlyList<TimelineStep> Steps, string? Group = null);

public static class TimelineProjector
{
    /// <param name="group">Transaction group; steps show the allowance that applies to it.</param>
    public static Projection Project(FlowDefinition flow, IReadOnlyList<ObservedEvent> events, string? group = null)
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
            return new TimelineStep(step.Code, step.Name, step.Position, step.AllowanceFor(group),
                observed is null ? "not_observed" : observed.Status == EventStatus.Completed ? "completed" : "failed",
                observed?.OccurredAt, observed?.Error);
        }).ToArray();

        var status = events.Count == 0 ? TransactionStatus.Pending
            : steps.Any(s => s.Observation == "failed") ? TransactionStatus.Failed
            : steps.All(s => s.Observation == "completed") ? TransactionStatus.Completed
            : TransactionStatus.InProgress;

        return new Projection(status, steps, group);
    }
}
