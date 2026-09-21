namespace IntegraOps.Domain;

public sealed record AlarmCondition(string Kind, string Step, string? Error, DateTimeOffset? DeadlineAt);

public static class AlarmRules
{
    public static IReadOnlyList<AlarmCondition> Current(Projection projection, DelayAssessment delay)
    {
        var conditions = projection.Steps.Where(s => s.Observation == "failed")
            .Select(s => new AlarmCondition("failed", s.Code, s.Error, null)).ToList();
        if (delay.State == "overdue" && delay.Step is not null)
            conditions.Add(new AlarmCondition("overdue", delay.Step, null, delay.DeadlineAt));
        return conditions;
    }

    public static string Resolution(Projection projection, string step) =>
        projection.Steps.Any(s => s.Code == step && s.Observation == "completed")
            ? "step_completed" : "tracking_changed";
}
