using IntegraOps.Domain;

namespace IntegraOps.Application;

public sealed record FlowDetails(
    Guid Id, string Code, string Name, IReadOnlyList<FlowStep> Steps);

public interface IFlowQueries
{
    Task<FlowDetails?> GetAsync(Guid projectId, string flow, CancellationToken ct);
}

public static partial class InputValidation
{
    public static string FlowCode(string? flow)
    {
        var errors = new Dictionary<string, string[]>();
        Code(errors, "flow", flow);
        ThrowIfInvalid(errors);
        return flow!;
    }
}
