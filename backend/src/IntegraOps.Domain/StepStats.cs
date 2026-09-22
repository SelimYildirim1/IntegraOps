namespace IntegraOps.Domain;

/// <summary>Realised duration of a completed step, measured from the previous completed step.</summary>
public sealed record StepStat(string Code, DateTimeOffset StartedAt, DateTimeOffset CompletedAt,
    long ElapsedSeconds, int AllowanceSeconds, bool Breached, bool OutOfOrder = false);

public static class StepStats
{
    public static IReadOnlyList<StepStat> Compute(FlowDefinition flow, Projection projection)
    {
        var steps = projection.Steps.OrderBy(s => s.Position).ToArray();
        var result = new List<StepStat>();
        for (var i = 1; i < steps.Length; i++)
        {
            var step = steps[i]; var previous = steps[i - 1];
            if (step.Observation != "completed" || previous.Observation != "completed"
                || step.OccurredAt is not { } done || previous.OccurredAt is not { } started || step.MaxDelaySeconds is not { } allowance)
                continue;
            // A step reported earlier than its predecessor is a source-time inconsistency, not a fast step.
            var outOfOrder = done < started;
            var elapsed = outOfOrder ? 0 : flow.ElapsedSeconds(started, done);
            result.Add(new StepStat(step.Code, started, done, elapsed, allowance, elapsed > allowance, outOfOrder));
        }
        return result;
    }
}
