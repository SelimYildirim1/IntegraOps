namespace IntegraOps.Domain;

public sealed record DelayAssessment(
    string State, string? Step, DateTimeOffset? StartedAt, DateTimeOffset? DeadlineAt);

public static class DelayEvaluator
{
    public static DelayAssessment Evaluate(Projection projection, DateTimeOffset now, FlowDefinition? flow = null)
    {
        var none = new DelayAssessment("none", null, null, null);
        // An explicit failure already has its own status. A delay means an expected
        // completion has not been observed, not that the external system failed.
        if (projection.Status != TransactionStatus.InProgress) return none;

        var steps = projection.Steps.OrderBy(step => step.Position).ToArray();
        var nextIndex = Array.FindIndex(steps, step => step.Observation != "completed");
        // We cannot time the first step: there is no preceding completion to anchor it.
        if (nextIndex <= 0) return none;

        var next = steps[nextIndex];
        var previous = steps[nextIndex - 1];
        if (next.Observation != "not_observed" || previous.Observation != "completed"
            || previous.OccurredAt is not { } startedAt || next.MaxDelaySeconds is not { } seconds)
            return none;

        // The expected step owns the allowance; business event time starts the clock.
        // With a calendar only working time counts, so the deadline may land on a later day.
        var deadlineAt = flow?.DeadlineFrom(startedAt, seconds) ?? startedAt.AddSeconds(seconds);
        return new DelayAssessment(now >= deadlineAt ? "overdue" : "waiting",
            next.Code, startedAt, deadlineAt);
    }
}
