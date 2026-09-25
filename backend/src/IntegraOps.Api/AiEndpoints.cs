using IntegraOps.Infrastructure;

namespace IntegraOps.Api;

public sealed record AiFeedbackInput(string? Rating, string? Comment);

/// <summary>
/// AI assistance: evidence-based analysis of an alarm or a transaction, plus user feedback. Off until Ai__Provider is set.
/// The client only names the subject; model, endpoint, prompt and evidence are chosen by the server.
/// </summary>
public static class AiEndpoints
{
    public static void Map(RouteGroupBuilder projectApi)
    {
        projectApi.MapGet("/ai/status", async (HttpContext c, PostgresAiStore s, CancellationToken ct)
            => Results.Ok(await s.Status(PilotEndpoints.Actor(c).OrganizationId, PilotEndpoints.Actor(c).ProjectId, ct))).WithMetadata(new ProjectRule());
        projectApi.MapGet("/alarms/{id:guid}/ai-analyses", async (HttpContext c, Guid id, PostgresAiStore s, CancellationToken ct)
            => Results.Ok(await s.AlarmAnalyses(PilotEndpoints.Actor(c), id, ct))).WithMetadata(new ProjectRule());
        projectApi.MapPost("/alarms/{id:guid}/ai-analyses", async (HttpContext c, Guid id, PostgresAiStore s, CancellationToken ct)
            => Results.Json(await s.AnalyzeAlarm(PilotEndpoints.Actor(c), id, ct), statusCode: 201)).WithMetadata(new ProjectRule("owner,operator"));
        projectApi.MapGet("/flows/{flow}/transactions/{transactionId}/ai-analyses", async (HttpContext c, string flow, string transactionId, PostgresAiStore s, CancellationToken ct)
            => Results.Ok(await s.TransactionAnalyses(PilotEndpoints.Actor(c), flow, transactionId, ct))).WithMetadata(new ProjectRule());
        projectApi.MapPost("/flows/{flow}/transactions/{transactionId}/ai-analyses", async (HttpContext c, string flow, string transactionId, PostgresAiStore s, CancellationToken ct)
            => Results.Json(await s.AnalyzeTransaction(PilotEndpoints.Actor(c), flow, transactionId, ct), statusCode: 201)).WithMetadata(new ProjectRule("owner,operator"));
        projectApi.MapPut("/ai/analyses/{id:guid}/feedback", async (HttpContext c, Guid id, AiFeedbackInput input, PostgresAiStore s, CancellationToken ct) =>
        { await s.Feedback(PilotEndpoints.Actor(c), id, input.Rating, input.Comment, ct); return Results.NoContent(); }).WithMetadata(new ProjectRule());
    }
}
