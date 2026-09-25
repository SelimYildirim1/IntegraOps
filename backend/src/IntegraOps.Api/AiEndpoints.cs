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
        projectApi.MapGet("/flows/{flow}/transactions/{transactionId}/ai-signals", async (HttpContext c, string flow, string transactionId, PostgresPilotQueries q, CancellationToken ct)
            => Results.Ok(await q.TransactionSignals(PilotEndpoints.Actor(c).ProjectId, flow, transactionId, ct))).WithMetadata(new ProjectRule());
        projectApi.MapPost("/flows/{flow}/transactions/{transactionId}/ai-analyses", async (HttpContext c, string flow, string transactionId, PostgresAiStore s, CancellationToken ct)
            => Results.Json(await s.AnalyzeTransaction(PilotEndpoints.Actor(c), flow, transactionId, ct), statusCode: 201)).WithMetadata(new ProjectRule("owner,operator"));
        projectApi.MapGet("/ai/triage", async (HttpContext c, PostgresAiStore s, string? test, CancellationToken ct)
            => Results.Ok(await s.TriageAnalyses(PilotEndpoints.Actor(c), test ?? "exclude", ct))).WithMetadata(new ProjectRule());
        projectApi.MapPost("/ai/triage", async (HttpContext c, PostgresAiStore s, string? test, CancellationToken ct)
            => Results.Json(await s.AnalyzeTriage(PilotEndpoints.Actor(c), test ?? "exclude", ct), statusCode: 201)).WithMetadata(new ProjectRule("owner,operator"));
        projectApi.MapGet("/flows/{flow}/ai-threshold", async (HttpContext c, string flow, PostgresAiStore s, int? days, string? test, CancellationToken ct)
            => Results.Ok(await s.ThresholdAnalyses(PilotEndpoints.Actor(c), IntegraOps.Application.InputValidation.FlowCode(flow), days ?? 30, test ?? "exclude", ct))).WithMetadata(new ProjectRule());
        projectApi.MapPost("/flows/{flow}/ai-threshold", async (HttpContext c, string flow, PostgresAiStore s, int? days, string? test, CancellationToken ct)
            => Results.Json(await s.AnalyzeThreshold(PilotEndpoints.Actor(c), IntegraOps.Application.InputValidation.FlowCode(flow), days ?? 30, test ?? "exclude", ct), statusCode: 201)).WithMetadata(new ProjectRule("owner,operator"));
        projectApi.MapPut("/ai/analyses/{id:guid}/feedback", async (HttpContext c, Guid id, AiFeedbackInput input, PostgresAiStore s, CancellationToken ct) =>
        { await s.Feedback(PilotEndpoints.Actor(c), id, input.Rating, input.Comment, ct); return Results.NoContent(); }).WithMetadata(new ProjectRule());
    }
}
