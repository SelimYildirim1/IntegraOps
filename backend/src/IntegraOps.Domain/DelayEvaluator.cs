namespace IntegraOps.Domain;

public sealed record PendingStep(string Step, DateTimeOffset StartedAt, DateTimeOffset DeadlineAt);

/// <summary>
/// State is none, waiting or overdue. Step/StartedAt/DeadlineAt describe the most urgent expected step;
/// Pending lists every expected step of the current stage that has an allowance (parallel branches).
/// </summary>
public sealed record DelayAssessment(
    string State, string? Step, DateTimeOffset? StartedAt, DateTimeOffset? DeadlineAt,
    IReadOnlyList<PendingStep>? Pending = null)
{
    public IReadOnlyList<PendingStep> PendingSteps => Pending ?? [];
}

public static class DelayEvaluator
{
    public static DelayAssessment Evaluate(Projection projection, DateTimeOffset now, FlowDefinition? flow = null)
    {
        var none = new DelayAssessment("none", null, null, null);
        // An explicit failure or cancellation already has its own status. A delay means an expected
        // completion has not been observed, not that the external system failed.
        if (projection.Status != TransactionStatus.InProgress) return none;

        // We cannot time the first stage: there is no preceding completion to anchor it.
        if (projection.CurrentStage() is not var (stage, startedAt)) return none;

        var pending = new List<PendingStep>();
        foreach (var step in projection.Steps.Where(s => s.EffectiveStage == stage && s.Expected && !s.Done).OrderBy(s => s.Position))
        {
            // A branch that failed keeps the transaction out of InProgress; here every branch is not_observed.
            if (step.Observation != "not_observed" || step.MaxDelaySeconds is not { } seconds) continue;
            // The expected step owns the allowance; business event time of the previous stage starts the clock.
            // With a calendar only working time counts, so the deadline may land on a later day.
            var deadlineAt = flow?.DeadlineFrom(startedAt, seconds) ?? startedAt.AddSeconds(seconds);
            pending.Add(new PendingStep(step.Code, startedAt, deadlineAt));
        }
        if (pending.Count == 0) return none;
        var urgent = pending.OrderBy(p => p.DeadlineAt).First();
        var overdue = pending.Any(p => now >= p.DeadlineAt);
        return new DelayAssessment(overdue ? "overdue" : "waiting", urgent.Step, urgent.StartedAt, urgent.DeadlineAt, pending);
    }
}
