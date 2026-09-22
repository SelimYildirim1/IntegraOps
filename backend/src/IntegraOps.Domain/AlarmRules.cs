namespace IntegraOps.Domain;

public sealed record AlarmCondition(string Kind, string Step, string? Error, DateTimeOffset? DeadlineAt);

public static class AlarmRules
{
    public static IReadOnlyList<AlarmCondition> Current(Projection projection, DelayAssessment delay, DateTimeOffset? now = null)
    {
        var conditions = projection.Steps.Where(s => s.Observation == "failed")
            .Select(s => new AlarmCondition("failed", s.Code, s.Error, null)).ToList();
        // Every parallel branch past its deadline gets its own alarm; the most urgent one is always included.
        var at = now ?? DateTimeOffset.UtcNow;
        if (delay.State == "overdue" && delay.Step is not null)
        {
            if (delay.PendingSteps.Count == 0) // assessments built without branch detail (single expected step)
                conditions.Add(new AlarmCondition("overdue", delay.Step, null, delay.DeadlineAt));
            foreach (var p in delay.PendingSteps)
                if (p.Step == delay.Step || at >= p.DeadlineAt)
                    conditions.Add(new AlarmCondition("overdue", p.Step, null, p.DeadlineAt));
        }
        return conditions;
    }

    public static string Resolution(Projection projection, string step) =>
        projection.Status == TransactionStatus.Cancelled ? "cancelled"
        : projection.Steps.Any(s => s.Code == step && s.Observation is "completed" or "skipped") ? "step_completed"
        : "tracking_changed";
}
