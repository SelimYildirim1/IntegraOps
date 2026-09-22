using System.Globalization;
using System.Text.RegularExpressions;
using IntegraOps.Application;
using IntegraOps.Domain;
using Npgsql;
using static IntegraOps.Infrastructure.PilotDb;
using static IntegraOps.Infrastructure.PilotSecurity;

namespace IntegraOps.Infrastructure;

public sealed record VersionStepInput(string? Code, string? Name, int? MaxDelaySeconds, Dictionary<string, int>? Overrides);
public sealed record VersionInput(string? Name, List<VersionStepInput>? Steps, Guid? CalendarId, string? Note);
public sealed record PublishInput(string? EffectiveFrom);
public sealed record FlowNameInput(string Name);
public sealed record HolidayInput(string Day, string? Name);
public sealed record CalendarInput(string? Name, string? TimeZone, int[]? WorkDays, string? StartTime, string? EndTime, List<HolidayInput>? Holidays);

/// <summary>V1.2: flow drafts and versions, business calendars, breach reporting.</summary>
public sealed partial class PostgresFlowVersionStore(NpgsqlDataSource source)
{
    [GeneratedRegex("^[a-z][a-z0-9-]{0,63}$")] private static partial Regex CodePattern();

    // ---------- Reading ----------

    public async Task<object?> Overview(Guid project, string code, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var flows = await Rows(c, null, "SELECT id,code,name FROM flows WHERE project_id=@p AND code=@c", ct, ("p", project), ("c", code));
        if (flows.Count == 0) return null;
        var flowId = (Guid)flows[0]["id"]!;
        var versions = await Rows(c, null, """
            SELECT v.id,v.version,v.state,v.effective_from,v.published_at,v.note,v.created_at,cal.name calendar,u.name created_by,
                (SELECT count(*) FROM flow_version_steps s WHERE s.version_id=v.id) step_count,
                (SELECT count(*) FROM transactions t WHERE t.flow_version_id=v.id) transaction_count,
                v.state='published' AND v.effective_from<=clock_timestamp() AND NOT EXISTS (
                    SELECT 1 FROM flow_versions n WHERE n.flow_id=v.flow_id AND n.state='published' AND n.effective_from<=clock_timestamp() AND n.version>v.version) effective
            FROM flow_versions v LEFT JOIN business_calendars cal ON cal.id=v.calendar_id LEFT JOIN app_users u ON u.id=v.created_by
            WHERE v.flow_id=@f ORDER BY v.version DESC
            """, ct, ("f", flowId));
        var effective = await FlowVersions.LoadAsync(c, null, project, code, null, null, ct);
        return new
        {
            id = flowId, code, name = flows[0]["name"], version = effective?.Version, versionId = effective?.VersionId,
            calendar = effective?.Calendar?.Name,
            steps = effective?.Steps.Select(Step) ?? [],
            versions,
            hasDraft = versions.Any(v => (string)v["state"]! == "draft"),
        };
    }

    public async Task<object> Version(Guid project, string code, int number, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var (flow, version) = await Find(c, null, project, code, number, ct);
        var steps = await FlowVersions.LoadStepsAsync(c, null, (Guid)version["id"]!, ct);
        return new
        {
            flow = flow["code"], name = flow["name"], version = number, state = version["state"], effectiveFrom = version["effective_from"],
            publishedAt = version["published_at"], calendarId = version["calendar_id"], note = version["note"],
            steps = steps.Select(Step),
        };
    }

    public async Task<object> Diff(Guid project, string code, int from, int to, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var (_, a) = await Find(c, null, project, code, from, ct);
        var (_, b) = await Find(c, null, project, code, to, ct);
        var left = await FlowVersions.LoadAsync(c, null, project, null, null, (Guid)a["id"]!, ct);
        var right = await FlowVersions.LoadAsync(c, null, project, null, null, (Guid)b["id"]!, ct);
        return new { from, to, changes = FlowDiff.Compare(left!, right!) };
    }

    private static object Step(FlowStep s) => new { code = s.Code, name = s.Name, position = s.Position, maxDelaySeconds = s.MaxDelaySeconds,
        overrides = s.Overrides ?? new Dictionary<string, int>() };

    private static async Task<(Dictionary<string, object?> Flow, Dictionary<string, object?> Version)> Find(NpgsqlConnection c, NpgsqlTransaction? tx,
        Guid project, string code, int number, CancellationToken ct, bool forUpdate = false)
    {
        var flows = await Rows(c, tx, "SELECT id,code,name FROM flows WHERE project_id=@p AND code=@c" + (forUpdate ? " FOR UPDATE" : ""), ct, ("p", project), ("c", code));
        if (flows.Count == 0) throw new PilotHttpException(404, "Akış bu projede bulunamadı.");
        var versions = await Rows(c, tx, "SELECT * FROM flow_versions WHERE flow_id=@f AND version=@v" + (forUpdate ? " FOR UPDATE" : ""), ct,
            ("f", flows[0]["id"]), ("v", number));
        if (versions.Count == 0) throw new PilotHttpException(404, "Sürüm bulunamadı.");
        return (flows[0], versions[0]);
    }

    // ---------- Drafts ----------

    public async Task<object> CreateDraft(ProjectIdentity actor, string code, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var flows = await Rows(c, tx, "SELECT id FROM flows WHERE project_id=@p AND code=@c FOR UPDATE", ct, ("p", actor.ProjectId), ("c", code));
        if (flows.Count == 0) throw new PilotHttpException(404, "Akış bu projede bulunamadı.");
        var flowId = (Guid)flows[0]["id"]!;
        if (await Value(c, tx, "SELECT 1 FROM flow_versions WHERE flow_id=@f AND state='draft'", ct, ("f", flowId)) is not null)
            throw new PilotHttpException(409, "Bu akışın zaten bir taslağı var. Önce onu yayınlayın ya da silin.");
        var latest = (await Rows(c, tx, "SELECT id,version,calendar_id FROM flow_versions WHERE flow_id=@f ORDER BY version DESC LIMIT 1", ct, ("f", flowId)))[0];
        var steps = await FlowVersions.LoadStepsAsync(c, tx, (Guid)latest["id"]!, ct);
        var id = Guid.CreateVersion7(); var number = (int)latest["version"]! + 1;
        await Run(c, tx, "INSERT INTO flow_versions(id,flow_id,version,state,calendar_id,created_by) VALUES(@id,@f,@v,'draft',CAST(@cal AS uuid),@u)", ct,
            ("id", id), ("f", flowId), ("v", number), ("cal", latest["calendar_id"]), ("u", actor.UserId));
        await FlowVersions.InsertStepsAsync(c, tx, id, steps, ct);
        await Audit(c, tx, actor.ProjectId, actor.UserId, "flow.draft", $"{code}@{number}", ct);
        await tx.CommitAsync(ct);
        return new { version = number, id };
    }

    public async Task UpdateDraft(ProjectIdentity actor, string code, int number, VersionInput input, CancellationToken ct)
    {
        var steps = ValidateSteps(input.Steps);
        var note = (input.Note ?? "").Trim();
        if (note.Length > 500) throw new PilotHttpException(400, "Not en fazla 500 karakter olabilir.");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var (flow, version) = await Find(c, tx, actor.ProjectId, code, number, ct, forUpdate: true);
        if ((string)version["state"]! != "draft") throw new PilotHttpException(409, "Yalnızca taslak düzenlenebilir. Yayınlanmış sürüm değişmez.");
        if (input.CalendarId is { } calendarId && await Value(c, tx, "SELECT 1 FROM business_calendars WHERE id=@id AND project_id=@p", ct,
            ("id", calendarId), ("p", actor.ProjectId)) is null) throw new PilotHttpException(400, "Takvim bu projede bulunamadı.");
        if (input.Name is not null)
            await Run(c, tx, "UPDATE flows SET name=@n WHERE id=@id", ct, ("n", Text(input.Name, 160, "Akış adı")), ("id", flow["id"]));
        var versionId = (Guid)version["id"]!;
        await Run(c, tx, "DELETE FROM flow_version_steps WHERE version_id=@v; UPDATE flow_versions SET calendar_id=CAST(@cal AS uuid),note=@note WHERE id=@v", ct,
            ("v", versionId), ("cal", input.CalendarId), ("note", note));
        await FlowVersions.InsertStepsAsync(c, tx, versionId, steps, ct);
        await Audit(c, tx, actor.ProjectId, actor.UserId, "flow.draft.update", $"{code}@{number}", ct);
        await tx.CommitAsync(ct);
    }

    public async Task<object> Publish(ProjectIdentity actor, string code, int number, PublishInput input, CancellationToken ct)
    {
        DateTimeOffset? effective = null;
        if (!string.IsNullOrWhiteSpace(input.EffectiveFrom))
        {
            var errors = new Dictionary<string, string[]>();
            effective = InputValidation.DateFilter(errors, "effectiveFrom", input.EffectiveFrom);
            if (errors.Count > 0) throw new PilotHttpException(400, "Etkinleşme zamanı için saat dilimli ISO 8601 zamanı kullanın.");
            if (effective > DateTimeOffset.UtcNow.AddDays(365)) throw new PilotHttpException(400, "Etkinleşme zamanı en fazla 1 yıl sonrası olabilir.");
        }
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var (_, version) = await Find(c, tx, actor.ProjectId, code, number, ct, forUpdate: true);
        if ((string)version["state"]! != "draft") throw new PilotHttpException(409, "Bu sürüm zaten yayınlanmış.");
        var versionId = (Guid)version["id"]!;
        var steps = await FlowVersions.LoadStepsAsync(c, tx, versionId, ct);
        if (steps.Count < 2) throw new PilotHttpException(400, "Yayınlamak için en az 2 adım gerekir.");
        // Never earlier than now: transactions already bound to the previous version keep it anyway.
        var at = (DateTime)(await Value(c, tx, "SELECT clock_timestamp()", ct))!;
        var effectiveFrom = effective is { } e && e > at ? e : new DateTimeOffset(at, TimeSpan.Zero);
        await Run(c, tx, "UPDATE flow_versions SET state='published',effective_from=@e,published_at=clock_timestamp() WHERE id=@v", ct,
            ("e", effectiveFrom), ("v", versionId));
        await Audit(c, tx, actor.ProjectId, actor.UserId, "flow.publish", $"{code}@{number}", ct);
        await tx.CommitAsync(ct);
        return new { version = number, effectiveFrom };
    }

    public async Task DeleteDraft(ProjectIdentity actor, string code, int number, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var (_, version) = await Find(c, tx, actor.ProjectId, code, number, ct, forUpdate: true);
        if ((string)version["state"]! != "draft") throw new PilotHttpException(409, "Yayınlanmış sürüm silinemez.");
        await Run(c, tx, "DELETE FROM flow_versions WHERE id=@v", ct, ("v", version["id"]));
        await Audit(c, tx, actor.ProjectId, actor.UserId, "flow.draft.delete", $"{code}@{number}", ct);
        await tx.CommitAsync(ct);
    }

    public async Task Rename(ProjectIdentity actor, string code, FlowNameInput input, CancellationToken ct)
    {
        var name = Text(input.Name, 160, "Akış adı");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        if (await Run(c, tx, "UPDATE flows SET name=@n WHERE project_id=@p AND code=@c", ct, ("n", name), ("p", actor.ProjectId), ("c", code)) == 0)
            throw new PilotHttpException(404, "Akış bu projede bulunamadı.");
        await Audit(c, tx, actor.ProjectId, actor.UserId, "flow.rename", code, ct);
        await tx.CommitAsync(ct);
    }

    private static IReadOnlyList<FlowStep> ValidateSteps(List<VersionStepInput>? input)
    {
        if (input is null || input.Count is < 2 or > 30) throw new PilotHttpException(400, "Akış 2–30 sıralı adımdan oluşmalı.");
        var codes = new HashSet<string>(StringComparer.Ordinal);
        var steps = new List<FlowStep>();
        for (var i = 0; i < input.Count; i++)
        {
            var s = input[i];
            var code = (s.Code ?? "").Trim();
            if (!CodePattern().IsMatch(code) || !codes.Add(code)) throw new PilotHttpException(400, $"{i + 1}. adımın kodu geçersiz ya da tekrar ediyor.");
            var name = Text(s.Name, 160, $"{i + 1}. adımın adı");
            if (i == 0 && s.MaxDelaySeconds is not null) throw new PilotHttpException(400, "Başlangıç adımının süresi olmaz.");
            if (i > 0 && s.MaxDelaySeconds is null or < 1 or > 2592000) throw new PilotHttpException(400, $"{i + 1}. adımın süresi 1 saniye–30 gün arasında olmalı.");
            Dictionary<string, int>? overrides = null;
            if (s.Overrides is { Count: > 0 })
            {
                if (i == 0) throw new PilotHttpException(400, "Başlangıç adımına grup süresi verilemez.");
                if (s.Overrides.Count > 20) throw new PilotHttpException(400, "Bir adımda en fazla 20 grup süresi tanımlanabilir.");
                overrides = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var (group, seconds) in s.Overrides)
                {
                    if (!CodePattern().IsMatch(group)) throw new PilotHttpException(400, $"Grup kodu geçersiz: {group}");
                    if (seconds is < 1 or > 2592000) throw new PilotHttpException(400, $"{group} grubunun süresi 1 saniye–30 gün arasında olmalı.");
                    overrides[group] = seconds;
                }
            }
            steps.Add(new FlowStep(code, name, i, s.MaxDelaySeconds, overrides));
        }
        return steps;
    }

    // ---------- Calendars ----------

    public async Task<object> Calendars(Guid project, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var calendars = await Rows(c, null, """
            SELECT b.id,b.name,b.timezone,b.work_days,b.start_time,b.end_time,b.created_at,
                (SELECT count(*) FROM flow_versions v WHERE v.calendar_id=b.id) version_count
            FROM business_calendars b WHERE b.project_id=@p ORDER BY b.name
            """, ct, ("p", project));
        var holidays = await Rows(c, null, "SELECT h.calendar_id,h.day,h.name FROM calendar_holidays h JOIN business_calendars b ON b.id=h.calendar_id WHERE b.project_id=@p ORDER BY h.day", ct, ("p", project));
        foreach (var cal in calendars)
        {
            cal["work_days"] = ((short[])cal["work_days"]!).Select(d => (int)d).ToArray();
            cal["start_time"] = ((TimeOnly)cal["start_time"]!).ToString("HH:mm", CultureInfo.InvariantCulture);
            cal["end_time"] = ((TimeOnly)cal["end_time"]!).ToString("HH:mm", CultureInfo.InvariantCulture);
            cal["holidays"] = holidays.Where(h => Equals(h["calendar_id"], cal["id"]))
                .Select(h => new { day = ((DateOnly)h["day"]!).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), name = h["name"] }).ToArray();
        }
        return calendars;
    }

    public async Task<object> SaveCalendar(ProjectIdentity actor, Guid? id, CalendarInput input, CancellationToken ct)
    {
        var name = Text(input.Name, 120, "Takvim adı");
        var tz = Text(input.TimeZone, 64, "Saat dilimi");
        try { TimeZoneInfo.FindSystemTimeZoneById(tz); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { throw new PilotHttpException(400, "Saat dilimi tanınmadı; örnek: Europe/Istanbul."); }
        var days = (input.WorkDays ?? []).Distinct().OrderBy(d => d).ToArray();
        if (days.Length == 0 || days.Any(d => d is < 1 or > 7)) throw new PilotHttpException(400, "Çalışma günleri 1 (Pazartesi) ile 7 (Pazar) arasında en az bir gün olmalı.");
        if (!TimeOnly.TryParseExact(input.StartTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            || !TimeOnly.TryParseExact(input.EndTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) || end <= start)
            throw new PilotHttpException(400, "Çalışma saatleri SS:DD biçiminde olmalı ve bitiş başlangıçtan sonra gelmeli.");
        var holidays = new List<(DateOnly Day, string Name)>();
        foreach (var h in input.Holidays ?? [])
        {
            if (!DateOnly.TryParseExact(h.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                throw new PilotHttpException(400, $"Tatil günü geçersiz: {h.Day}");
            holidays.Add((day, (h.Name ?? "").Trim() is { Length: <= 120 } n ? n : throw new PilotHttpException(400, "Tatil adı en fazla 120 karakter.")));
        }
        if (holidays.Count > 200) throw new PilotHttpException(400, "En fazla 200 tatil günü tanımlanabilir.");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var calendarId = id ?? Guid.CreateVersion7();
        try
        {
            if (id is null)
                await Run(c, tx, "INSERT INTO business_calendars(id,project_id,name,timezone,work_days,start_time,end_time) VALUES(@id,@p,@n,@tz,@d,@s,@e)", ct,
                    ("id", calendarId), ("p", actor.ProjectId), ("n", name), ("tz", tz), ("d", days.Select(d => (short)d).ToArray()), ("s", start), ("e", end));
            else if (await Run(c, tx, "UPDATE business_calendars SET name=@n,timezone=@tz,work_days=@d,start_time=@s,end_time=@e WHERE id=@id AND project_id=@p", ct,
                ("id", calendarId), ("p", actor.ProjectId), ("n", name), ("tz", tz), ("d", days.Select(d => (short)d).ToArray()), ("s", start), ("e", end)) == 0)
                throw new PilotHttpException(404, "Takvim bulunamadı.");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation) { throw new PilotHttpException(409, "Bu takvim adı zaten kullanılıyor."); }
        await Run(c, tx, "DELETE FROM calendar_holidays WHERE calendar_id=@id", ct, ("id", calendarId));
        foreach (var (day, holidayName) in holidays.DistinctBy(h => h.Day))
            await Run(c, tx, "INSERT INTO calendar_holidays(calendar_id,day,name) VALUES(@id,@d,@n)", ct, ("id", calendarId), ("d", day), ("n", holidayName));
        await Audit(c, tx, actor.ProjectId, actor.UserId, id is null ? "calendar.create" : "calendar.update", calendarId.ToString(), ct);
        await tx.CommitAsync(ct);
        return new { id = calendarId, name };
    }

    public async Task DeleteCalendar(ProjectIdentity actor, Guid id, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        if (await Value(c, tx, "SELECT 1 FROM flow_versions WHERE calendar_id=@id", ct, ("id", id)) is not null)
            throw new PilotHttpException(409, "Bu takvim bir akış sürümünde kullanılıyor; silinemez.");
        if (await Run(c, tx, "DELETE FROM business_calendars WHERE id=@id AND project_id=@p", ct, ("id", id), ("p", actor.ProjectId)) == 0)
            throw new PilotHttpException(404, "Takvim bulunamadı.");
        await Audit(c, tx, actor.ProjectId, actor.UserId, "calendar.delete", id.ToString(), ct);
        await tx.CommitAsync(ct);
    }

    // ---------- Breach report ----------

    public async Task<object> Breaches(Guid project, string? flow, string? from, string? to, string? test, int page, CancellationToken ct)
    {
        var query = InputValidation.TransactionList(flow, null, null, page, 25, null, from, to, test);
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        const string where = """
            FROM transaction_step_stats s JOIN transactions t ON t.id=s.transaction_id JOIN flows f ON f.id=t.flow_id
            WHERE s.project_id=@p AND s.breached
              AND (CAST(@flow AS text) IS NULL OR f.code=@flow)
              AND (CAST(@from AS timestamptz) IS NULL OR s.completed_at>=@from)
              AND (CAST(@to AS timestamptz) IS NULL OR s.completed_at<@to)
              AND (@test='include' OR (@test='only' AND t.is_test) OR (@test='exclude' AND NOT t.is_test))
            """;
        (string, object?)[] args = [("p", project), ("flow", query.Flow), ("from", query.From), ("to", query.To), ("test", query.Test)];
        var total = await Value(c, tx, "SELECT count(*) " + where, ct, args);
        var items = await Rows(c, tx, "SELECT t.business_id,f.code flow,s.step_code,s.started_at,s.completed_at,s.elapsed_seconds,s.allowance_seconds,t.group_code,t.is_test " + where
            + " ORDER BY s.completed_at DESC,t.id LIMIT 25 OFFSET @skip", ct, args.Append(("skip", (object?)((page - 1) * 25))).ToArray());
        var byStep = await Rows(c, tx, """
            SELECT f.code flow,s.step_code,count(*) FILTER (WHERE s.breached) breached,count(*) total,
                round(avg(s.elapsed_seconds)) avg_elapsed,max(s.allowance_seconds) allowance
            FROM transaction_step_stats s JOIN transactions t ON t.id=s.transaction_id JOIN flows f ON f.id=t.flow_id
            WHERE s.project_id=@p AND (CAST(@flow AS text) IS NULL OR f.code=@flow)
              AND (CAST(@from AS timestamptz) IS NULL OR s.completed_at>=@from)
              AND (CAST(@to AS timestamptz) IS NULL OR s.completed_at<@to)
              AND (@test='include' OR (@test='only' AND t.is_test) OR (@test='exclude' AND NOT t.is_test))
            GROUP BY f.code,s.step_code ORDER BY breached DESC,f.code,s.step_code LIMIT 100
            """, ct, args);
        await tx.CommitAsync(ct);
        return new { items, total, page, pageSize = 25, byStep };
    }
}
