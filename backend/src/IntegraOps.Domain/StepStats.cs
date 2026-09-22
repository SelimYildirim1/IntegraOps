namespace IntegraOps.Domain;

/// <summary>Realised duration of a completed step, measured from the end of the previous stage.</summary>
public sealed record StepStat(string Code, DateTimeOffset StartedAt, DateTimeOffset CompletedAt,
    long ElapsedSeconds, int AllowanceSeconds, bool Breached, bool OutOfOrder = false);

public static class StepStats
{
    public static IReadOnlyList<StepStat> Compute(FlowDefinition flow, Projection projection)
    {
        var result = new List<StepStat>();
        foreach (var step in projection.Steps.OrderBy(s => s.Position))
        {
            if (step.Observation != "completed" || step.OccurredAt is not { } done || step.MaxDelaySeconds is not { } allowance) continue;
            if (projection.StageEnd(step.EffectiveStage) is not { } started) continue;
            // A step reported earlier than its predecessors is a source-time inconsistency, not a fast step.
            var outOfOrder = done < started;
            var elapsed = outOfOrder ? 0 : flow.ElapsedSeconds(started, done);
            result.Add(new StepStat(step.Code, started, done, elapsed, allowance, elapsed > allowance, outOfOrder));
        }
        return result;
    }
}
