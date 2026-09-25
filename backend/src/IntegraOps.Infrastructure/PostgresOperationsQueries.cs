using System.Text;
using IntegraOps.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Npgsql;
using static IntegraOps.Infrastructure.PilotDb;
using static IntegraOps.Infrastructure.PilotSecurity;

namespace IntegraOps.Infrastructure;

/// <summary>V1.1 daily-operation features: saved views, invitations, password reset, retention, exports.</summary>
public sealed class PostgresOperationsQueries(NpgsqlDataSource source, IConfiguration configuration, PostgresTransactionQueries transactions)
{
    private readonly PasswordHasher<string> hasher = new();
    public const int ExportLimit = 20000;

    private string PanelUrl => (configuration["Panel:BaseUrl"] ?? "http://localhost:3000").TrimEnd('/');

    // ---------- Saved views ----------

    public async Task<object> SavedViews(ProjectIdentity actor, string? kind, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        return await Rows(c, null, """
            SELECT id,kind,name,query,created_at FROM saved_views
            WHERE project_id=@p AND user_id=@u AND (@k='' OR kind=@k) ORDER BY kind,name
            """, ct, ("p", actor.ProjectId), ("u", actor.UserId), ("k", kind ?? ""));
    }

    public async Task<object> SaveView(ProjectIdentity actor, SavedViewInput input, CancellationToken ct)
    {
        if (input.Kind is not ("transactions" or "alarms")) throw new PilotHttpException(400, "Görünüm türü transactions veya alarms olmalı.");
        var name = Text(input.Name, 80, "Görünüm adı");
        var query = (input.Query ?? "").Trim().TrimStart('?');
        if (query.Length > 1000 || query.Any(ch => ch is '\n' or '\r' or '#'))
            throw new PilotHttpException(400, "Filtre sorgusu geçersiz.");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var count = (long)(await Value(c, tx, "SELECT count(*) FROM saved_views WHERE project_id=@p AND user_id=@u", ct,
            ("p", actor.ProjectId), ("u", actor.UserId)))!;
        if (count >= 50) throw new PilotHttpException(409, "En fazla 50 kayıtlı görünüm tutulabilir.");
        var id = Guid.CreateVersion7();
        await Run(c, tx, """
            INSERT INTO saved_views(id,project_id,user_id,kind,name,query) VALUES(@id,@p,@u,@k,@n,@q)
            ON CONFLICT(project_id,user_id,kind,name) DO UPDATE SET query=EXCLUDED.query
            """, ct, ("id", id), ("p", actor.ProjectId), ("u", actor.UserId), ("k", input.Kind), ("n", name), ("q", query));
        await tx.CommitAsync(ct);
        return new { id, kind = input.Kind, name, query };
    }

    public async Task DeleteView(ProjectIdentity actor, Guid id, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        if (await Run(c, null, "DELETE FROM saved_views WHERE id=@id AND project_id=@p AND user_id=@u", ct,
            ("id", id), ("p", actor.ProjectId), ("u", actor.UserId)) == 0) throw new PilotHttpException(404, "Görünüm bulunamadı.");
    }

    // ---------- Invitations and password reset ----------

    public async Task<object> Invite(ProjectIdentity actor, InviteInput input, CancellationToken ct)
    {
        var email = Email(input.Email); var role = Role(input.Role); var name = Text(input.Name, 120, "Ad");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await Value(c, tx, "SELECT pg_advisory_xact_lock(hashtextextended(@e,7142645))", ct, ("e", email));
        var existing = await Rows(c, tx, "SELECT id,activated_at FROM app_users WHERE email=@e", ct, ("e", email));
        Guid user; var fresh = existing.Count == 0;
        if (fresh)
        {
            user = Guid.CreateVersion7();
            // An unguessable placeholder; login is impossible until the invitation sets a password.
            await Run(c, tx, "INSERT INTO app_users(id,email,name,password_hash,activated_at) VALUES(@u,@e,@n,@h,NULL)", ct,
                ("u", user), ("e", email), ("n", name), ("h", hasher.HashPassword(email, Token())));
        }
        else user = (Guid)existing[0]["id"]!;
        var activated = !fresh && existing[0]["activated_at"] is not null;
        await Run(c, tx, """
            INSERT INTO project_members(project_id,user_id,role) VALUES(@p,@u,@r)
            ON CONFLICT(project_id,user_id) DO UPDATE SET role=EXCLUDED.role
            """, ct, ("p", actor.ProjectId), ("u", user), ("r", role));
        var projectName = (string)(await Value(c, tx, "SELECT name FROM projects WHERE id=@p", ct, ("p", actor.ProjectId)))!;
        var inviter = (string)(await Value(c, tx, "SELECT name FROM app_users WHERE id=@u", ct, ("u", actor.UserId)))!;
        string subject, body;
        if (activated)
        {
            subject = $"IntegraOps · {projectName} projesine eklendiniz";
            body = $"Merhaba,\n\n{inviter} sizi IntegraOps üzerinde \"{projectName}\" projesine {RoleName(role)} olarak ekledi.\n\n"
                + $"Mevcut hesabınızla giriş yapabilirsiniz: {PanelUrl}/login\n";
        }
        else
        {
            var token = await IssueToken(c, tx, user, "invite", actor.UserId, TimeSpan.FromDays(7), ct);
            subject = $"IntegraOps · {projectName} projesine davet";
            body = $"Merhaba {name},\n\n{inviter} sizi IntegraOps üzerinde \"{projectName}\" projesine {RoleName(role)} olarak davet etti.\n\n"
                + $"Hesabınızı tamamlamak için 7 gün içinde bu bağlantıyı açın ve parolanızı belirleyin:\n{PanelUrl}/invite/{token}\n\n"
                + "Bu daveti beklemiyorsanız e-postayı yok sayabilirsiniz.\n";
        }
        await QueueEmail(c, tx, email, subject, body, ct);
        await Audit(c, tx, actor.ProjectId, actor.UserId, activated ? "member." + role : "member.invite", user.ToString(), ct);
        await tx.CommitAsync(ct);
        return new { userId = user, invited = !activated, role };
    }

    public async Task RequestPasswordReset(ResetRequestInput input, CancellationToken ct)
    {
        var email = Email(input.Email);
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var rows = await Rows(c, tx, "SELECT id,name,activated_at FROM app_users WHERE email=@e", ct, ("e", email));
        // Same response for unknown addresses; nothing reveals whether an account exists.
        if (rows.Count == 0 || rows[0]["activated_at"] is null) { await tx.CommitAsync(ct); return; }
        var user = (Guid)rows[0]["id"]!;
        var recent = (long)(await Value(c, tx, """
            SELECT count(*) FROM account_tokens WHERE user_id=@u AND kind='reset' AND created_at>clock_timestamp()-interval '15 minutes'
            """, ct, ("u", user)))!;
        if (recent >= 3) { await tx.CommitAsync(ct); return; }
        var token = await IssueToken(c, tx, user, "reset", null, TimeSpan.FromHours(1), ct);
        await QueueEmail(c, tx, email, "IntegraOps · Parola sıfırlama",
            $"Merhaba {rows[0]["name"]},\n\nParolanızı yenilemek için 1 saat içinde bu bağlantıyı açın:\n{PanelUrl}/reset/{token}\n\n"
            + "Bu isteği siz yapmadıysanız e-postayı yok sayın; parolanız değişmez.\n", ct);
        await tx.CommitAsync(ct);
    }

    public async Task<object> TokenInfo(string token, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var rows = await Rows(c, null, """
            SELECT t.kind,u.email,u.name FROM account_tokens t JOIN app_users u ON u.id=t.user_id
            WHERE t.token_hash=@h AND t.used_at IS NULL AND t.expires_at>clock_timestamp()
            """, ct, ("h", Hash(ValidToken(token))));
        if (rows.Count == 0) throw new PilotHttpException(404, "Bağlantı geçersiz ya da süresi dolmuş.");
        return new { kind = rows[0]["kind"], email = rows[0]["email"], name = rows[0]["name"] };
    }

    public async Task CompleteToken(string token, string kind, TokenPasswordInput input, CancellationToken ct)
    {
        var password = Password(input.Password);
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var rows = await Rows(c, tx, """
            SELECT t.user_id,u.email FROM account_tokens t JOIN app_users u ON u.id=t.user_id
            WHERE t.token_hash=@h AND t.kind=@k AND t.used_at IS NULL AND t.expires_at>clock_timestamp() FOR UPDATE OF t
            """, ct, ("h", Hash(ValidToken(token))), ("k", kind));
        if (rows.Count == 0) throw new PilotHttpException(404, "Bağlantı geçersiz ya da süresi dolmuş.");
        var user = (Guid)rows[0]["user_id"]!; var email = (string)rows[0]["email"]!;
        await Run(c, tx, """
            UPDATE account_tokens SET used_at=clock_timestamp() WHERE token_hash=@h;
            UPDATE account_tokens SET used_at=clock_timestamp() WHERE user_id=@u AND kind=@k AND used_at IS NULL;
            UPDATE app_users SET password_hash=@p,failed_logins=0,locked_until=NULL,activated_at=COALESCE(activated_at,clock_timestamp()) WHERE id=@u;
            DELETE FROM user_sessions WHERE user_id=@u
            """, ct, ("h", Hash(token)), ("u", user), ("k", kind), ("p", hasher.HashPassword(email, password)));
        await tx.CommitAsync(ct);
    }

    private static string ValidToken(string token) => token.Length == 64 && token.All(Uri.IsHexDigit) ? token
        : throw new PilotHttpException(404, "Bağlantı geçersiz ya da süresi dolmuş.");

    private static async Task<string> IssueToken(NpgsqlConnection c, NpgsqlTransaction tx, Guid user, string kind, Guid? by, TimeSpan life, CancellationToken ct)
    {
        var token = Token();
        await Run(c, tx, """
            UPDATE account_tokens SET used_at=clock_timestamp() WHERE user_id=@u AND kind=@k AND used_at IS NULL;
            INSERT INTO account_tokens(token_hash,user_id,kind,expires_at,created_by) VALUES(@h,@u,@k,clock_timestamp()+@life,CAST(@by AS uuid))
            """, ct, ("u", user), ("k", kind), ("h", Hash(token)), ("life", life), ("by", by));
        return token;
    }

    internal static Task QueueEmail(NpgsqlConnection c, NpgsqlTransaction? tx, string to, string subject, string body, CancellationToken ct) =>
        Run(c, tx, "INSERT INTO outbound_emails(id,recipient,subject,body) VALUES(@id,@r,@s,@b)", ct,
            ("id", Guid.CreateVersion7()), ("r", to), ("s", subject), ("b", body));

    private static string RoleName(string role) => role switch { "owner" => "proje sahibi", "operator" => "operatör", _ => "izleyici" };

    // ---------- Retention ----------

    public async Task<object> Retention(Guid project, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var policy = await Rows(c, null, "SELECT finished_transaction_days,notification_days,audit_days,updated_at FROM retention_policies WHERE project_id=@p", ct, ("p", project));
        var runs = await Rows(c, null, "SELECT ran_at,transactions_deleted,deliveries_deleted,audit_deleted FROM retention_runs WHERE project_id=@p ORDER BY ran_at DESC LIMIT 10", ct, ("p", project));
        return new
        {
            policy = policy.Count == 0 ? new Dictionary<string, object?> { ["finished_transaction_days"] = null, ["notification_days"] = null, ["audit_days"] = null, ["updated_at"] = null } : policy[0],
            runs
        };
    }

    public async Task SetRetention(ProjectIdentity actor, RetentionInput input, CancellationToken ct)
    {
        static int? Days(int? value, int min, string field) => value is null ? null
            : value is >= 1 and <= 3650 && value >= min ? value
            : throw new PilotHttpException(400, $"{field}: {min}–3650 gün arasında olmalı ya da boş bırakılmalı.");
        var finished = Days(input.FinishedTransactionDays, 7, "Tamamlanan işlemler");
        var notifications = Days(input.NotificationDays, 7, "Bildirimler");
        var audit = Days(input.AuditDays, 30, "Denetim kayıtları");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await Run(c, tx, """
            INSERT INTO retention_policies(project_id,finished_transaction_days,notification_days,audit_days,updated_by)
            VALUES(@p,CAST(@f AS integer),CAST(@n AS integer),CAST(@a AS integer),@u)
            ON CONFLICT(project_id) DO UPDATE SET finished_transaction_days=EXCLUDED.finished_transaction_days,
                notification_days=EXCLUDED.notification_days,audit_days=EXCLUDED.audit_days,updated_at=clock_timestamp(),updated_by=EXCLUDED.updated_by
            """, ct, ("p", actor.ProjectId), ("f", finished), ("n", notifications), ("a", audit), ("u", actor.UserId));
        await Audit(c, tx, actor.ProjectId, actor.UserId, "retention.update",
            $"transactions={finished?.ToString() ?? "-"} notifications={notifications?.ToString() ?? "-"} audit={audit?.ToString() ?? "-"}", ct);
        await tx.CommitAsync(ct);
    }

    // ---------- Exports ----------

    public async Task<string> TransactionsCsv(Guid project, TransactionListQuery query, CancellationToken ct)
    {
        var rows = await transactions.ListForExportAsync(project, query, ExportLimit, ct);
        var csv = new CsvWriter(["islem", "akis", "durum", "beklenen_adim", "sure_durumu", "son_zaman", "olusturulma", "guncelleme", "test"]);
        foreach (var r in rows)
            csv.Row(r.TransactionId, r.Flow, r.Status.ToString(), r.Delay.Step, r.Delay.State, r.Delay.DeadlineAt, r.CreatedAt, r.UpdatedAt, r.IsTest ? "evet" : "hayır");
        return csv.ToString();
    }

    public string AlarmsCsv(List<Dictionary<string, object?>> rows)
    {
        var csv = new CsvWriter(["islem", "akis", "alarm", "adim", "acilis", "son_zaman", "kapanis", "cozum", "neden", "cozum_notu", "goruldu", "sorumlu", "hata", "test"]);
        foreach (var r in rows)
            csv.Row(r["business_id"], r["flow"], r["kind"], r["step_code"], r["opened_at"], r["deadline_at"], r["resolved_at"], r["resolution"],
                r["cause"], r["fix"], r["acknowledged_at"], r["assigned_name"], r["error"], r["is_test"] is true ? "evet" : "hayır");
        return csv.ToString();
    }
}

/// <summary>RFC 4180 CSV with a UTF-8 BOM so spreadsheet programs open Turkish text correctly.</summary>
public sealed class CsvWriter
{
    private readonly StringBuilder text = new("\uFEFF");

    public CsvWriter(IEnumerable<string> header) => Row(header.Cast<object?>().ToArray());

    public void Row(params object?[] values)
    {
        text.Append(string.Join(",", values.Select(Field))).Append("\r\n");
    }

    public static string Field(object? value)
    {
        var s = value switch
        {
            null => "",
            DateTimeOffset at => at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            DateTime at => new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            bool b => b ? "true" : "false",
            _ => value.ToString() ?? ""
        };
        // A leading formula character is neutralised so exported cells never execute in spreadsheets.
        if (s.Length > 0 && s[0] is '=' or '+' or '-' or '@' or '\t' or '\r') s = "'" + s;
        return s.IndexOfAny(['"', ',', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    public override string ToString() => text.ToString();
}
