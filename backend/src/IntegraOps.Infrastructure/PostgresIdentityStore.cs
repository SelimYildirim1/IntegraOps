using Microsoft.AspNetCore.Identity;
using Npgsql;
using static IntegraOps.Infrastructure.PilotDb;
using static IntegraOps.Infrastructure.PilotSecurity;

namespace IntegraOps.Infrastructure;

public sealed class PostgresIdentityStore(NpgsqlDataSource source)
{
    private readonly PasswordHasher<string> hasher = new();
    private readonly string dummyHash = new PasswordHasher<string>().HashPassword("dummy", Token());

    public async Task<bool> HasUsers(CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        return (bool)(await Value(c, null, "SELECT EXISTS(SELECT 1 FROM app_users)", ct))!;
    }

    public async Task FirstOwner(Guid project, FirstOwnerInput input, CancellationToken ct)
    {
        var email = Email(input.Email); var name = Text(input.Name, 120, "Ad"); var password = Password(input.Password);
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await Value(c, tx, "SELECT pg_advisory_xact_lock(71426458531002)", ct);
        if ((bool)(await Value(c, tx, "SELECT EXISTS(SELECT 1 FROM app_users)", ct))!)
            throw new PilotHttpException(409, "İlk kullanıcı zaten oluşturuldu. Giriş yapın.");
        var id = Guid.CreateVersion7();
        await Run(c, tx, "INSERT INTO app_users(id,email,name,password_hash) VALUES(@id,@e,@n,@h)", ct,
            ("id", id), ("e", email), ("n", name), ("h", hasher.HashPassword(email, password)));
        await Run(c, tx, "INSERT INTO project_members(project_id,user_id,role) VALUES(@p,@u,'owner')", ct, ("p", project), ("u", id));
        await Audit(c, tx, project, id, "owner.bootstrap", id.ToString(), ct);
        await tx.CommitAsync(ct);
    }

    public async Task<object> Login(LoginInput input, CancellationToken ct)
    {
        var email = Email(input.Email);
        if (input.Password is not { Length: > 0 and <= 128 }) throw new PilotHttpException(401, "E-posta veya parola doğrulanamadı.");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var rows = await Rows(c, tx, "SELECT * FROM app_users WHERE email=@e FOR UPDATE", ct, ("e", email));
        if (rows.Count == 0)
        {
            hasher.VerifyHashedPassword(email, dummyHash, input.Password);
            throw new PilotHttpException(401, "E-posta veya parola doğrulanamadı.");
        }
        var u = rows[0]; var id = (Guid)u["id"]!;
        var verified = hasher.VerifyHashedPassword(email, (string)u["password_hash"]!, input.Password);
        if (u["locked_until"] is DateTime locked && locked > DateTime.UtcNow)
            throw new PilotHttpException(401, "E-posta veya parola doğrulanamadı. Bir süre sonra tekrar deneyin.");
        if (verified == PasswordVerificationResult.Failed)
        {
            await Run(c, tx, """
                UPDATE app_users SET failed_logins=CASE WHEN failed_logins>=4 THEN 0 ELSE failed_logins+1 END,
                    locked_until=CASE WHEN failed_logins>=4 THEN clock_timestamp()+interval '15 minutes' ELSE NULL END
                WHERE id=@u
                """, ct, ("u", id));
            await tx.CommitAsync(ct);
            throw new PilotHttpException(401, "E-posta veya parola doğrulanamadı.");
        }
        await Run(c, tx, "UPDATE app_users SET failed_logins=0,locked_until=NULL,password_hash=@h WHERE id=@u", ct,
            ("u", id), ("h", verified == PasswordVerificationResult.SuccessRehashNeeded
                ? hasher.HashPassword(email, input.Password) : u["password_hash"]));
        var token = Token();
        await Run(c, tx, """
            DELETE FROM user_sessions WHERE expires_at<=clock_timestamp();
            INSERT INTO user_sessions(token_hash,user_id,expires_at) VALUES(@h,@u,clock_timestamp()+interval '8 hours')
            """, ct, ("h", Hash(token)), ("u", id));
        await tx.CommitAsync(ct);
        return new { token, expiresIn = 28800, user = new SessionUser(id, email, (string)u["name"]!) };
    }

    public async Task<SessionUser?> Session(string token, CancellationToken ct)
    {
        if (token.Length != 64) return null;
        await using var c = await source.OpenConnectionAsync(ct);
        var rows = await Rows(c, null, """
            SELECT u.id,u.email,u.name FROM user_sessions s JOIN app_users u ON u.id=s.user_id
            WHERE s.token_hash=@h AND s.expires_at>clock_timestamp()
            """, ct, ("h", Hash(token)));
        return rows.Count == 0 ? null : new((Guid)rows[0]["id"]!, (string)rows[0]["email"]!, (string)rows[0]["name"]!);
    }

    public async Task Logout(string token, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await Run(c, null, "DELETE FROM user_sessions WHERE token_hash=@h", ct, ("h", Hash(token)));
    }

    public async Task ChangePassword(Guid user, PasswordInput input, CancellationToken ct)
    {
        var password = Password(input.NewPassword);
        if (input.CurrentPassword is not { Length: > 0 and <= 128 }) throw new PilotHttpException(400, "Mevcut parola gerekli.");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var u = (await Rows(c, tx, "SELECT email,password_hash FROM app_users WHERE id=@u FOR UPDATE", ct, ("u", user)))[0];
        var email = (string)u["email"]!;
        if (hasher.VerifyHashedPassword(email, (string)u["password_hash"]!, input.CurrentPassword) == PasswordVerificationResult.Failed)
            throw new PilotHttpException(400, "Mevcut parola doğrulanamadı.");
        await Run(c, tx, "UPDATE app_users SET password_hash=@h WHERE id=@u; DELETE FROM user_sessions WHERE user_id=@u", ct,
            ("h", hasher.HashPassword(email, password)), ("u", user));
        await tx.CommitAsync(ct);
    }

    public async Task<ProjectIdentity?> Access(Guid? user, Guid project, string apiKey, CancellationToken ct)
    {
        if (user is null && apiKey.Length is < 32 or > 128) return null;
        await using var c = await source.OpenConnectionAsync(ct);
        var rows = user is not null
            ? await Rows(c, null, """
                SELECT p.id,p.organization_id,m.role,NULL::text scope FROM projects p
                JOIN project_members m ON m.project_id=p.id WHERE p.id=@p AND m.user_id=@u
                """, ct, ("p", project), ("u", user))
            : await Rows(c, null, """
                SELECT p.id,p.organization_id,'key'::text role,k.scope FROM api_keys k
                JOIN projects p ON p.id=k.project_id WHERE lower(k.key_hash)=@h AND k.revoked_at IS NULL
                """, ct, ("h", Hash(apiKey)));
        if (rows.Count == 0) return null;
        var r = rows[0];
        return new((Guid)r["id"]!, (Guid)r["organization_id"]!, user, (string)r["role"]!, r["scope"] as string);
    }

    public async Task<object> Projects(Guid user, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        return await Rows(c, null, """
            SELECT p.id,p.name,m.role FROM project_members m JOIN projects p ON p.id=m.project_id
            WHERE m.user_id=@u ORDER BY p.created_at,p.id
            """, ct, ("u", user));
    }

    public async Task<object> CreateProject(Guid user, string name, CancellationToken ct)
    {
        name = Text(name, 160, "Proje adı");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var id = Guid.CreateVersion7(); var org = Guid.CreateVersion7();
        await Run(c, tx, """
            INSERT INTO organizations(id,name) VALUES(@o,@n);
            INSERT INTO projects(id,organization_id,name) VALUES(@p,@o,@n);
            INSERT INTO project_members(project_id,user_id,role) VALUES(@p,@u,'owner')
            """, ct, ("o", org), ("p", id), ("n", name), ("u", user));
        await Audit(c, tx, id, user, "project.create", id.ToString(), ct);
        await tx.CommitAsync(ct);
        return new { id, name, role = "owner" };
    }

    public async Task<object> Members(Guid project, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        return await Rows(c, null, """
            SELECT u.id,u.email,u.name,m.role,u.activated_at IS NOT NULL AS activated FROM project_members m JOIN app_users u ON u.id=m.user_id
            WHERE m.project_id=@p ORDER BY u.email
            """, ct, ("p", project));
    }

    public async Task<object> Assignable(Guid project, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        return await Rows(c, null, """
            SELECT u.id,u.name,m.role FROM project_members m JOIN app_users u ON u.id=m.user_id
            WHERE m.project_id=@p AND m.role IN ('owner','operator') AND u.activated_at IS NOT NULL ORDER BY u.name
            """, ct, ("p", project));
    }

    public async Task PutMember(ProjectIdentity actor, MemberInput input, CancellationToken ct)
    {
        var email = Email(input.Email); var role = Role(input.Role); var name = Text(input.Name, 120, "Ad");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockProject(c, tx, actor, ct);
        // Serializes creation of one account by owners of different projects.
        await Value(c, tx, "SELECT pg_advisory_xact_lock(hashtextextended(@e,7142645))", ct, ("e", email));
        var existing = await Value(c, tx, "SELECT id FROM app_users WHERE email=@e", ct, ("e", email));
        var user = existing is Guid known ? known : Guid.CreateVersion7();
        if (existing is null)
            await Run(c, tx, "INSERT INTO app_users(id,email,name,password_hash) VALUES(@u,@e,@n,@h)", ct,
                ("u", user), ("e", email), ("n", name), ("h", hasher.HashPassword(email, Password(input.Password))));
        else if (!string.IsNullOrEmpty(input.Password))
            throw new PilotHttpException(409, "Hesap zaten var. Mevcut hesabı eklerken parola alanını boş bırakın.");
        if (role != "owner") await GuardLastOwner(c, tx, actor.ProjectId, user, ct);
        await Run(c, tx, """
            INSERT INTO project_members(project_id,user_id,role) VALUES(@p,@u,@r)
            ON CONFLICT(project_id,user_id) DO UPDATE SET role=EXCLUDED.role
            """, ct, ("p", actor.ProjectId), ("u", user), ("r", role));
        await Audit(c, tx, actor.ProjectId, actor.UserId, "member." + role, user.ToString(), ct);
        await tx.CommitAsync(ct);
    }

    public async Task RemoveMember(ProjectIdentity actor, Guid user, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockProject(c, tx, actor, ct);
        await GuardLastOwner(c, tx, actor.ProjectId, user, ct);
        await Run(c, tx, "DELETE FROM project_members WHERE project_id=@p AND user_id=@u", ct, ("p", actor.ProjectId), ("u", user));
        await Audit(c, tx, actor.ProjectId, actor.UserId, "member.remove", user.ToString(), ct);
        await tx.CommitAsync(ct);
    }

    // Re-check after acquiring the project lock: another owner may just have changed this actor's role.
    private static async Task LockProject(NpgsqlConnection c, NpgsqlTransaction tx, ProjectIdentity actor, CancellationToken ct)
    {
        await Value(c, tx, "SELECT id FROM projects WHERE id=@p FOR UPDATE", ct, ("p", actor.ProjectId));
        if (await Value(c, tx, "SELECT role FROM project_members WHERE project_id=@p AND user_id=@u", ct,
            ("p", actor.ProjectId), ("u", actor.UserId)) is not "owner") throw new PilotHttpException(403, "Proje sahibi yetkisi gerekli.");
    }
    private static async Task GuardLastOwner(NpgsqlConnection c, NpgsqlTransaction tx, Guid project, Guid user, CancellationToken ct)
    {
        if ((bool)(await Value(c, tx, """
            SELECT EXISTS(SELECT 1 FROM project_members WHERE project_id=@p AND user_id=@u AND role='owner')
                AND (SELECT count(*) FROM project_members WHERE project_id=@p AND role='owner')=1
            """, ct, ("p", project), ("u", user)))!)
            throw new PilotHttpException(409, "Projenin son sahibini kaldıramaz veya rolünü düşüremezsiniz.");
    }

    public async Task<object> Keys(Guid project, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        return await Rows(c, null, "SELECT id,name,scope,key_prefix,created_at,revoked_at FROM api_keys WHERE project_id=@p ORDER BY created_at DESC", ct, ("p", project));
    }
    public async Task<object> CreateKey(ProjectIdentity actor, KeyInput input, CancellationToken ct)
    {
        var name = Text(input.Name, 120, "Anahtar adı");
        if (input.Scope is not ("ingest" or "read")) throw new PilotHttpException(400, "Anahtar yetkisi ingest veya read olmalı.");
        var key = "io_v1_" + Token(); var id = Guid.CreateVersion7();
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockProject(c, tx, actor, ct);
        await Run(c, tx, "INSERT INTO api_keys(id,project_id,name,scope,key_hash,key_prefix) VALUES(@id,@p,@n,@s,@h,@prefix)", ct,
            ("id", id), ("p", actor.ProjectId), ("n", name), ("s", input.Scope), ("h", Hash(key)), ("prefix", key[..16]));
        await Audit(c, tx, actor.ProjectId, actor.UserId, "key.create", id.ToString(), ct);
        await tx.CommitAsync(ct);
        return new { id, key, name, scope = input.Scope };
    }
    public async Task RevokeKey(ProjectIdentity actor, Guid key, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockProject(c, tx, actor, ct);
        if (await Run(c, tx, "UPDATE api_keys SET revoked_at=COALESCE(revoked_at,clock_timestamp()) WHERE id=@id AND project_id=@p", ct,
            ("id", key), ("p", actor.ProjectId)) == 0) throw new PilotHttpException(404, "Anahtar bulunamadı.");
        await Audit(c, tx, actor.ProjectId, actor.UserId, "key.revoke", key.ToString(), ct);
        await tx.CommitAsync(ct);
    }
}
