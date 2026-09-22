namespace IntegraOps.Domain;

public enum EventStatus { Completed, Failed, Skipped, Cancelled }
public enum TransactionStatus { Pending, InProgress, Failed, Completed, Cancelled }

/// <summary>How a step participates in the flow.</summary>
public static class StepKind
{
    public const string Required = "required";       // must complete (or be skipped explicitly)
    public const string Optional = "optional";       // may happen; never expected, never timed
    public const string Conditional = "conditional"; // required only for transactions in the listed groups
}

// A published flow version is immutable; a transaction keeps the version it started with.
// Steps in the same stage run in parallel; the next stage waits for all applicable required steps of the previous one.
// Overrides give a different allowance to transactions of a named group (for example "vip").
public sealed record FlowStep(string Code, string Name, int Position, int? MaxDelaySeconds,
    IReadOnlyDictionary<string, int>? Overrides = null, int? Stage = null, string Kind = StepKind.Required,
    IReadOnlyList<string>? Groups = null)
{
    public int EffectiveStage => Stage ?? Position;
    public int? AllowanceFor(string? group) =>
        group is not null && Overrides is not null && Overrides.TryGetValue(group, out var seconds) ? seconds : MaxDelaySeconds;
    /// <summary>Whether a transaction of this group is expected to perform the step.</summary>
    public bool AppliesTo(string? group) => Kind switch
    {
        StepKind.Optional => false,
        StepKind.Conditional => group is not null && Groups is { Count: > 0 } && Groups.Contains(group, StringComparer.Ordinal),
        _ => true,
    };
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

/// <summary>Observation: not_observed, completed, failed, skipped, cancelled or not_applicable (step not expected for this group).</summary>
public sealed record TimelineStep(
    string Code, string Name, int Position, int? MaxDelaySeconds,
    string Observation, DateTimeOffset? OccurredAt, string? Error,
    int? Stage = null, string Kind = StepKind.Required)
{
    /// <summary>Linear flows built without stage numbers use the position as the stage.</summary>
    public int EffectiveStage => Stage ?? Position;
    /// <summary>Done for progression: completed or explicitly skipped.</summary>
    public bool Done => Observation is "completed" or "skipped";
    public bool Expected => Kind != StepKind.Optional && Observation != "not_applicable";
}

public sealed record Projection(TransactionStatus Status, IReadOnlyList<TimelineStep> Steps, string? Group = null)
{
    /// <summary>The stage whose applicable required steps are not all done yet, with the time it started; null when finished.</summary>
    public (int Stage, DateTimeOffset StartedAt)? CurrentStage()
    {
        foreach (var stage in Steps.Select(s => s.EffectiveStage).Distinct().OrderBy(s => s))
        {
            var members = Steps.Where(s => s.EffectiveStage == stage && s.Expected).ToArray();
            if (members.Length == 0 || members.All(s => s.Done)) continue; // nothing expected here, or already done
            var previous = StageEnd(stage);
            return previous is { } start ? (stage, start) : null;
        }
        return null;
    }

    /// <summary>When the stage before <paramref name="stage"/> was fully done: the latest occurrence among its done steps.</summary>
    public DateTimeOffset? StageEnd(int stage)
    {
        // The anchor is the last earlier stage that expects something; stages made only of optional steps do not delay the clock.
        foreach (var earlier in Steps.Where(s => s.EffectiveStage < stage).Select(s => s.EffectiveStage).Distinct().OrderByDescending(s => s))
        {
            var members = Steps.Where(s => s.EffectiveStage == earlier && s.Expected).ToArray();
            if (members.Length == 0) continue;
            return members.All(s => s.Done) ? members.Max(s => s.OccurredAt) : null;
        }
        return null;
    }
}

public static class TimelineProjector
{
    /// <param name="group">Transaction group; steps show the allowance that applies to it and conditional steps resolve against it.</param>
    public static Projection Project(FlowDefinition flow, IReadOnlyList<ObservedEvent> events, string? group = null)
    {
        // Delivery order is not business order. EventId breaks exact timestamp ties deterministically.
        var latestByStep = events
            .GroupBy(e => e.Step, StringComparer.Ordinal)
            .ToDictionary(g => g.Key,
                g => g.OrderBy(e => e.OccurredAt).ThenBy(e => e.EventId).Last(),
                StringComparer.Ordinal);
        var cancelled = events.Any(e => e.Status == EventStatus.Cancelled);

        var steps = flow.Steps.OrderBy(s => s.Position).Select(step =>
        {
            latestByStep.TryGetValue(step.Code, out var observed);
            var observation = observed is null
                ? (step.Kind == StepKind.Conditional && !step.AppliesTo(group) ? "not_applicable" : "not_observed")
                : observed.Status switch
                {
                    EventStatus.Completed => "completed",
                    EventStatus.Failed => "failed",
                    EventStatus.Skipped => "skipped",
                    _ => "cancelled",
                };
            return new TimelineStep(step.Code, step.Name, step.Position, step.AllowanceFor(group),
                observation, observed?.OccurredAt, observed?.Error, step.EffectiveStage, step.Kind);
        }).ToArray();

        var status = events.Count == 0 ? TransactionStatus.Pending
            : cancelled ? TransactionStatus.Cancelled
            : steps.Any(s => s.Observation == "failed") ? TransactionStatus.Failed
            : steps.Where(s => s.Expected).All(s => s.Done) ? TransactionStatus.Completed
            : TransactionStatus.InProgress;

        return new Projection(status, steps, group);
    }
}
