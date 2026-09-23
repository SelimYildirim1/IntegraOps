namespace IntegraOps.Infrastructure;

/// <summary>Commercial plans and their default limits. Null means unlimited.</summary>
public sealed record PlanLimits(int? MaxProjects, int? MaxUsers, int? MaxEventsPerDay, int? MaxRetentionDays);

public static class Plans
{
    public const int TrialDays = 14;

    public static readonly IReadOnlyDictionary<string, PlanLimits> Defaults = new Dictionary<string, PlanLimits>
    {
        ["trial"] = new(3, 5, 5_000, 30),
        ["starter"] = new(5, 10, 50_000, 90),
        ["business"] = new(25, 50, 500_000, 365),
        ["enterprise"] = new(null, null, null, null),
        ["suspended"] = new(0, 0, 0, null),
    };

    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        ["trial"] = "Deneme", ["starter"] = "Başlangıç", ["business"] = "İşletme", ["enterprise"] = "Kurumsal", ["suspended"] = "Askıda",
    };

    /// <summary>AI assistant add-on by plan; trial includes it so customers can evaluate it.</summary>
    public static readonly IReadOnlyDictionary<string, bool> AiDefaults = new Dictionary<string, bool>
    {
        ["trial"] = true, ["starter"] = false, ["business"] = false, ["enterprise"] = true, ["suspended"] = false,
    };

    public static bool AiEnabled(string plan, bool? overrideValue) => overrideValue ?? AiDefaults.GetValueOrDefault(plan, false);

    public static PlanLimits Effective(string plan, int? maxProjects, int? maxUsers, int? maxEvents, int? maxRetention)
    {
        var d = Defaults.TryGetValue(plan, out var limits) ? limits : Defaults["trial"];
        return new(maxProjects ?? d.MaxProjects, maxUsers ?? d.MaxUsers, maxEvents ?? d.MaxEventsPerDay, maxRetention ?? d.MaxRetentionDays);
    }
}
