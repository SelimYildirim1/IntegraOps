using IntegraOps.Application;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using static IntegraOps.Infrastructure.PilotDb;
using static IntegraOps.Infrastructure.PilotSecurity;

namespace IntegraOps.Infrastructure;

public sealed record LookupInput(string? Flow, List<string>? TransactionIds);
public sealed record DiscrepancyInput(string? TransactionId, string? Kind, string? StepCode, string? Detail, string? SourceOccurredAt);
public sealed record ReconciliationRunInput(string? Name, string? Flow, string? WindowFrom, string? WindowTo, int? GraceSeconds,
    int? SourceCount, int? MatchedCount, string? Version, List<DiscrepancyInput>? Discrepancies);

/// <summary>V2: the service contract the Python reconciler talks to, plus panel queries.</summary>
public sealed class PostgresReconciliationStore(NpgsqlDataSource source, IDataProtectionProvider protection, IConfigurationReader panel)
{
    private readonly IDataProtector protector = protection.CreateProtector("notification-channels.v1");

    /// <summary>Bulk existence and progress check; the reconciler compares this with the source system.</summary>
    public async Task<object> Lookup(Guid project, LookupInput input, CancellationToken ct)
    {
        var flow = InputValidation.FlowCode(input.Flow);
        var ids = (input.TransactionIds ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count is 0 or > 500) throw new PilotHttpException(400, "transactionIds 1–500 kayıt içermeli.");
        if (ids.Any(id => id.Length > 128)) throw new PilotHttpException(400, "İşlem kimliği en fazla 128 karakter olabilir.");
        await using var c = await source.OpenConnectionAsync(ct);
        var rows = await Rows(c, null, """
            SELECT t.business_id, t.status, t.created_at, t.updated_at,
                COALESCE((SELECT array_agg(DISTINCT e.step_code) FROM transaction_events e WHERE e.transaction_id = t.id AND e.status = 'Completed'), '{}') completed_steps,
                COALESCE((SELECT array_agg(DISTINCT e.step_code) FROM transaction_events e WHERE e.transaction_id = t.id AND e.status = 'Failed'), '{}') failed_steps
            FROM transactions t JOIN flows f ON f.id = t.flow_id
            WHERE t.project_id = @p AND f.code = @f AND t.business_id = ANY(@ids)
            """, ct, ("p", project), ("f", flow), ("ids", ids.ToArray()));
        var found = rows.ToDictionary(r => (string)r["business_id"]!, StringComparer.Ordinal);
        return new
        {
            flow,
            items = ids.Select(id => found.TryGetValue(id, out var r)
                ? new { transactionId = id, exists = true, status = ((string)r["status"]!).ToLowerInvariant(), createdAt = r["created_at"], updatedAt = r["updated_at"],
                    completedSteps = (string[])r["completed_steps"]!, failedSteps = (string[])r["failed_steps"]! }
                : new { transactionId = id, exists = false, status = "", createdAt = (object?)null, updatedAt = (object?)null, completedSteps = Array.Empty<string>(), failedSteps = Array.Empty<string>() }),
        };
    }

    /// <summary>Records a run and reconciles its discrepancies with the known set: new, repeated and resolved.</summary>
    public async Task<object> RecordRun(ProjectIdentity actor, ReconciliationRunInput input, CancellationToken ct)
    {
        var name = Text(input.Name, 64, "Mutabakat adı");
        var flow = InputValidation.FlowCode(input.Flow);
        var errors = new Dictionary<string, string[]>();
        var from = InputValidation.DateFilter(errors, "windowFrom", input.WindowFrom);
        var to = InputValidation.DateFilter(errors, "windowTo", input.WindowTo);
        if (from is null || to is null || to <= from) errors["windowTo"] = ["Karşılaştırma aralığı geçersiz."];
        if (input.GraceSeconds is null or < 0 or > 604800) errors["graceSeconds"] = ["Bekleme payı 0–604800 saniye olmalı."];
        if (input.SourceCount is null or < 0 || input.MatchedCount is null or < 0) errors["sourceCount"] = ["Sayılar negatif olamaz."];
        var items = input.Discrepancies ?? [];
        if (items.Count > 5000) errors["discrepancies"] = ["Bir koşuda en fazla 5000 uyuşmazlık bildirilebilir."];
        if (errors.Count > 0) throw new RequestValidationException(errors);
        var version = (input.Version ?? "").Trim(); if (version.Length > 64) version = version[..64];

        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        if (await Value(c, tx, "SELECT 1 FROM flows WHERE project_id=@p AND code=@f", ct, ("p", actor.ProjectId), ("f", flow)) is null)
            throw new PilotHttpException(404, "Akış bu projede bulunamadı.");
        // One run per project/flow at a time keeps new/repeated/resolved counts consistent.
        await Value(c, tx, "SELECT pg_advisory_xact_lock(hashtextextended(@k, 91))", ct, ("k", actor.ProjectId + "/" + flow));
        var runId = Guid.CreateVersion7();
        await Run(c, tx, """
            INSERT INTO reconciliation_runs(id,project_id,name,flow,window_from,window_to,grace_seconds,source_count,matched_count,version)
            VALUES(@id,@p,@n,@f,@from,@to,@g,@s,@m,@v)
            """, ct, ("id", runId), ("p", actor.ProjectId), ("n", name), ("f", flow), ("from", from), ("to", to), ("g", input.GraceSeconds),
            ("s", input.SourceCount), ("m", input.MatchedCount), ("v", version));
        int created = 0, repeated = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var fresh = new List<(string Transaction, string Kind, string Step, string Detail)>();
        foreach (var d in items)
        {
            var transaction = Text(d.TransactionId, 128, "İşlem kimliği");
            if (d.Kind is not ("missing_transaction" or "missing_step" or "status_mismatch")) throw new PilotHttpException(400, "Uyuşmazlık türü geçersiz.");
            var step = (d.StepCode ?? "").Trim(); if (step.Length > 64) throw new PilotHttpException(400, "Adım kodu en fazla 64 karakter.");
            var detail = (d.Detail ?? "").Trim(); if (detail.Length > 500) detail = detail[..500];
            DateTimeOffset? occurred = null;
            if (!string.IsNullOrWhiteSpace(d.SourceOccurredAt))
            {
                var e = new Dictionary<string, string[]>();
                occurred = InputValidation.DateFilter(e, "sourceOccurredAt", d.SourceOccurredAt);
                if (e.Count > 0) throw new PilotHttpException(400, "sourceOccurredAt ISO 8601 olmalı.");
            }
            if (!seen.Add(transaction + "\n" + d.Kind + "\n" + step)) continue;
            var affected = await Run(c, tx, """
                UPDATE reconciliation_discrepancies SET last_run_id=@r,last_seen_at=clock_timestamp(),seen_count=seen_count+1,detail=@d,
                    resolved_at=NULL,resolved_run_id=NULL
                WHERE project_id=@p AND flow=@f AND transaction_id=@t AND kind=@k AND step_code=@s AND resolved_at IS NULL
                """, ct, ("r", runId), ("d", detail), ("p", actor.ProjectId), ("f", flow), ("t", transaction), ("k", d.Kind), ("s", step));
            if (affected > 0) { repeated++; continue; }
            // A previously resolved discrepancy that reappears counts as new again.
            await Run(c, tx, """
                INSERT INTO reconciliation_discrepancies(id,project_id,flow,transaction_id,kind,step_code,detail,source_occurred_at,first_run_id,last_run_id)
                VALUES(@id,@p,@f,@t,@k,@s,@d,CAST(@o AS timestamptz),@r,@r)
                ON CONFLICT(project_id,flow,transaction_id,kind,step_code) DO UPDATE SET first_run_id=EXCLUDED.first_run_id,last_run_id=EXCLUDED.last_run_id,
                    first_seen_at=clock_timestamp(),last_seen_at=clock_timestamp(),seen_count=1,detail=EXCLUDED.detail,source_occurred_at=EXCLUDED.source_occurred_at,
                    resolved_at=NULL,resolved_run_id=NULL
                """, ct, ("id", Guid.CreateVersion7()), ("p", actor.ProjectId), ("f", flow), ("t", transaction), ("k", d.Kind), ("s", step), ("d", detail),
                ("o", occurred), ("r", runId));
            created++;
            fresh.Add((transaction, d.Kind!, step, detail));
        }
        // Open discrepancies inside this window that the run no longer reports are resolved.
        var resolved = await Run(c, tx, """
            UPDATE reconciliation_discrepancies SET resolved_at=clock_timestamp(),resolved_run_id=@r
            WHERE project_id=@p AND flow=@f AND resolved_at IS NULL AND last_run_id<>@r
              AND (source_occurred_at IS NULL OR (source_occurred_at>=@from AND source_occurred_at<@to))
            """, ct, ("r", runId), ("p", actor.ProjectId), ("f", flow), ("from", from), ("to", to));
        await Run(c, tx, "UPDATE reconciliation_runs SET new_count=@n,repeated_count=@rp,resolved_count=@rs WHERE id=@id", ct,
            ("n", created), ("rp", repeated), ("rs", resolved), ("id", runId));
        if (created > 0) await Notify(c, tx, actor.ProjectId, name, flow, from.GetValueOrDefault(), to.GetValueOrDefault(), created, fresh, ct);
        await Audit(c, tx, actor.ProjectId, actor.UserId, "reconciliation.run", $"{flow} new={created} repeated={repeated} resolved={resolved}", ct);
        await tx.CommitAsync(ct);
        return new { runId, newCount = created, repeatedCount = repeated, resolvedCount = resolved };
    }

    // Only newly found discrepancies are announced, once, to the project's e-mail channels.
    private async Task Notify(NpgsqlConnection c, NpgsqlTransaction tx, Guid project, string name, string flow, DateTimeOffset from, DateTimeOffset to,
        int created, List<(string Transaction, string Kind, string Step, string Detail)> fresh, CancellationToken ct)
    {
        var channels = await Rows(c, tx, "SELECT target_protected FROM notification_channels WHERE project_id=@p AND enabled AND kind='email'", ct, ("p", project));
        if (channels.Count == 0) return;
        var projectName = (string)(await Value(c, tx, "SELECT name FROM projects WHERE id=@p", ct, ("p", project)))!;
        var lines = fresh.Take(20).Select(d => $"- {d.Transaction}: {KindName(d.Kind)}{(d.Step.Length > 0 ? " (" + d.Step + ")" : "")}{(d.Detail.Length > 0 ? " · " + d.Detail : "")}");
        var body = $"Mutabakat: {name}\nProje: {projectName}\nAkış: {flow}\nAralık: {from:yyyy-MM-dd HH:mm}Z – {to:yyyy-MM-dd HH:mm}Z\n\n"
            + $"{created} yeni uyuşmazlık bulundu:\n{string.Join("\n", lines)}{(created > 20 ? $"\n… ve {created - 20} kayıt daha" : "")}\n\n"
            + $"Ayrıntı: {panel.PanelUrl}/reconciliation\n";
        foreach (var ch in channels)
            await PostgresOperationsQueries.QueueEmail(c, tx, protector.Unprotect((string)ch["target_protected"]!),
                $"IntegraOps · Mutabakat · {flow} · {created} yeni uyuşmazlık", body, ct);
    }

    public static string KindName(string kind) => kind switch
    {
        "missing_transaction" => "IntegraOps'a hiç bildirilmemiş",
        "missing_step" => "adım bildirilmemiş",
        _ => "durum uyuşmazlığı",
    };

    public async Task<object> Runs(Guid project, string? flow, int page, CancellationToken ct)
    {
        if (page is < 1 or > 100000) throw new PilotHttpException(400, "Sayfa geçersiz.");
        await using var c = await source.OpenConnectionAsync(ct);
        var items = await Rows(c, null, """
            SELECT id,name,flow,window_from,window_to,grace_seconds,source_count,matched_count,new_count,repeated_count,resolved_count,version,created_at
            FROM reconciliation_runs WHERE project_id=@p AND (@f='' OR flow=@f) ORDER BY created_at DESC,id DESC LIMIT 25 OFFSET @skip
            """, ct, ("p", project), ("f", flow ?? ""), ("skip", (page - 1) * 25));
        var last = await Rows(c, null, """
            SELECT DISTINCT ON (name, flow) name, flow, window_to, created_at FROM reconciliation_runs WHERE project_id=@p ORDER BY name, flow, created_at DESC
            """, ct, ("p", project));
        // The reconciler extends its window back to the oldest open discrepancy so it keeps re-checking until resolved.
        var openSince = await Rows(c, null, """
            SELECT flow, min(source_occurred_at) oldest, count(*) open FROM reconciliation_discrepancies
            WHERE project_id=@p AND resolved_at IS NULL GROUP BY flow
            """, ct, ("p", project));
        return new { items, page, pageSize = 25, lastRuns = last, openSince };
    }

    public async Task<object> Discrepancies(Guid project, string? state, string? flow, string? kind, int page, CancellationToken ct)
    {
        state ??= "open";
        if (state is not ("open" or "resolved" or "all") || page is < 1 or > 100000) throw new PilotHttpException(400, "Filtre geçersiz.");
        if (kind is not (null or "" or "missing_transaction" or "missing_step" or "status_mismatch")) throw new PilotHttpException(400, "Tür geçersiz.");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        const string where = """
            FROM reconciliation_discrepancies d
            WHERE d.project_id=@p AND (@s='all' OR (@s='open' AND d.resolved_at IS NULL) OR (@s='resolved' AND d.resolved_at IS NOT NULL))
              AND (@f='' OR d.flow=@f) AND (@k='' OR d.kind=@k)
            """;
        (string, object?)[] args = [("p", project), ("s", state), ("f", flow ?? ""), ("k", kind ?? ""), ("skip", (page - 1) * 25)];
        var total = await Value(c, tx, "SELECT count(*) " + where, ct, args);
        var items = await Rows(c, tx, """
            SELECT d.id,d.flow,d.transaction_id,d.kind,d.step_code,d.detail,d.source_occurred_at,d.first_seen_at,d.last_seen_at,d.resolved_at,d.seen_count,
                EXISTS (SELECT 1 FROM transactions t JOIN flows f ON f.id=t.flow_id WHERE t.project_id=d.project_id AND f.code=d.flow AND t.business_id=d.transaction_id) known
            """ + "\n" + where + " ORDER BY d.resolved_at NULLS FIRST, d.first_seen_at DESC LIMIT 25 OFFSET @skip", ct, args);
        var counts = (await Rows(c, tx, """
            SELECT count(*) FILTER (WHERE resolved_at IS NULL) open, count(*) FILTER (WHERE resolved_at IS NULL AND kind='missing_transaction') missing_transactions
            FROM reconciliation_discrepancies WHERE project_id=@p
            """, ct, ("p", project)))[0];
        await tx.CommitAsync(ct);
        return new { items, total, page, pageSize = 25, counts };
    }
}

/// <summary>Small indirection so infrastructure classes can build panel links without depending on ASP.NET configuration.</summary>
public interface IConfigurationReader
{
    string PanelUrl { get; }
}

public sealed class ConfigurationReader(Microsoft.Extensions.Configuration.IConfiguration configuration) : IConfigurationReader
{
    public string PanelUrl => (configuration["Panel:BaseUrl"] ?? "http://localhost:3000").TrimEnd('/');
}
