namespace IntegraOps.Domain;

public sealed record StepChange(string Kind, string Code, string? Detail);

/// <summary>Human-readable differences between two flow versions, in target order.</summary>
public static class FlowDiff
{
    public static IReadOnlyList<StepChange> Compare(FlowDefinition from, FlowDefinition to)
    {
        var changes = new List<StepChange>();
        var before = from.Steps.ToDictionary(s => s.Code, StringComparer.Ordinal);
        var after = to.Steps.ToDictionary(s => s.Code, StringComparer.Ordinal);
        foreach (var step in to.Steps.OrderBy(s => s.Position))
        {
            if (!before.TryGetValue(step.Code, out var old))
            {
                changes.Add(new StepChange("added", step.Code, $"{step.Name}; {Allowance(step.MaxDelaySeconds)}"));
                continue;
            }
            if (old.Position != step.Position) changes.Add(new StepChange("moved", step.Code, $"{old.Position + 1}. → {step.Position + 1}."));
            if (old.Name != step.Name) changes.Add(new StepChange("renamed", step.Code, $"{old.Name} → {step.Name}"));
            if (old.MaxDelaySeconds != step.MaxDelaySeconds)
                changes.Add(new StepChange("allowance", step.Code, $"{Allowance(old.MaxDelaySeconds)} → {Allowance(step.MaxDelaySeconds)}"));
            var oldOverrides = old.Overrides ?? new Dictionary<string, int>();
            var newOverrides = step.Overrides ?? new Dictionary<string, int>();
            foreach (var group in oldOverrides.Keys.Union(newOverrides.Keys).OrderBy(g => g, StringComparer.Ordinal))
            {
                oldOverrides.TryGetValue(group, out var a); newOverrides.TryGetValue(group, out var b);
                if (!oldOverrides.ContainsKey(group)) changes.Add(new StepChange("override_added", step.Code, $"{group}: {Allowance(b)}"));
                else if (!newOverrides.ContainsKey(group)) changes.Add(new StepChange("override_removed", step.Code, $"{group}: {Allowance(a)}"));
                else if (a != b) changes.Add(new StepChange("override", step.Code, $"{group}: {Allowance(a)} → {Allowance(b)}"));
            }
        }
        foreach (var step in from.Steps.OrderBy(s => s.Position).Where(s => !after.ContainsKey(s.Code)))
            changes.Add(new StepChange("removed", step.Code, step.Name));
        var fromCalendar = from.Calendar?.Name ?? "7/24";
        var toCalendar = to.Calendar?.Name ?? "7/24";
        if (fromCalendar != toCalendar) changes.Add(new StepChange("calendar", "-", $"{fromCalendar} → {toCalendar}"));
        return changes;
    }

    private static string Allowance(int? seconds) => seconds is null ? "başlangıç" : seconds + " sn";
}
