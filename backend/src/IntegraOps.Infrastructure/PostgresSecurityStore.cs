using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using static IntegraOps.Infrastructure.PilotDb;
using static IntegraOps.Infrastructure.PilotSecurity;

namespace IntegraOps.Infrastructure;

public sealed record MfaCodeInput(string? Code);
public sealed record MfaChallengeInput(string? Challenge, string? Code);
public sealed record MfaDisableInput(string? Password, string? Code);
public sealed record SsoConfigInput(string? Issuer, string? ClientId, string? ClientSecret, string? Domain, bool? Required);
public sealed record SsoStartInput(string? Email);
public sealed record TicketInput(string? Ticket);
public sealed record DeletionInput(string? Password, bool? Cancel);

/// <summary>Second factor (TOTP), single sign-on (OIDC), security events, data export and organization deletion.</summary>
public sealed class PostgresSecurityStore(NpgsqlDataSource source, IDataProtectionProvider protection, IConfiguration configuration,
    IHttpClientFactory http, TimeProvider clock)
{
    private readonly IDataProtector protector = protection.CreateProtector("account-security.v1");
    private readonly PasswordHasher<string> hasher = new();
    private readonly Dictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> discovery = new();

    public string ApiUrl => (configuration["Api:PublicUrl"] ?? "http://localhost:8080").TrimEnd('/');
    private string PanelUrl => (configuration["Panel:BaseUrl"] ?? "http://localhost:3000").TrimEnd('/');
    private bool AllowHttp => configuration.GetValue("Sso:AllowHttp", false);

    // ---------- MFA ----------

    public async Task<object> BeginMfa(SessionUser user, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        if ((bool)(await Value(c, null, "SELECT mfa_enabled FROM app_users WHERE id=@u", ct, ("u", user.Id)))!)
            throw new PilotHttpException(409, "İki adımlı doğrulama zaten açık.");
        var secret = Totp.NewSecret();
        await Run(c, null, "UPDATE app_users SET mfa_secret_protected=@s WHERE id=@u", ct, ("s", protector.Protect(secret)), ("u", user.Id));
        return new { secret, uri = Totp.Uri(secret, user.Email) };
    }

    public async Task<object> EnableMfa(SessionUser user, MfaCodeInput input, RequestContext ctx, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var u = (await Rows(c, tx, "SELECT mfa_enabled,mfa_secret_protected FROM app_users WHERE id=@u FOR UPDATE", ct, ("u", user.Id)))[0];
        if ((bool)u["mfa_enabled"]!) throw new PilotHttpException(409, "İki adımlı doğrulama zaten açık.");
        if (u["mfa_secret_protected"] is not string protectedSecret) throw new PilotHttpException(400, "Önce kurulum başlatılmalı.");
        if (!Totp.Verify(protector.Unprotect(protectedSecret), input.Code?.Trim(), clock.GetUtcNow().ToUnixTimeSeconds()))
            throw new PilotHttpException(400, "Kod doğrulanamadı. Uygulamadaki güncel kodu girin.");
        var codes = Enumerable.Range(0, 8).Select(_ => RecoveryCode()).ToArray();
        await Run(c, tx, "UPDATE app_users SET mfa_enabled=true,mfa_recovery_hashes=@r WHERE id=@u", ct, ("r", codes.Select(Hash).ToArray()), ("u", user.Id));
        await PostgresOrganizationStore.Security(c, tx, user.Id, null, "mfa.enabled", ctx, null, ct);
        await tx.CommitAsync(ct);
        return new { recoveryCodes = codes };
    }

    public async Task DisableMfa(SessionUser user, MfaDisableInput input, RequestContext ctx, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var u = (await Rows(c, tx, "SELECT email,password_hash,mfa_enabled,mfa_secret_protected FROM app_users WHERE id=@u FOR UPDATE", ct, ("u", user.Id)))[0];
        if (!(bool)u["mfa_enabled"]!) throw new PilotHttpException(409, "İki adımlı doğrulama kapalı.");
        if (input.Password is not { Length: > 0 and <= 128 } || hasher.VerifyHashedPassword((string)u["email"]!, (string)u["password_hash"]!, input.Password) == PasswordVerificationResult.Failed)
            throw new PilotHttpException(400, "Parola doğrulanamadı.");
        if (!Totp.Verify(protector.Unprotect((string)u["mfa_secret_protected"]!), input.Code?.Trim(), clock.GetUtcNow().ToUnixTimeSeconds()))
            throw new PilotHttpException(400, "Kod doğrulanamadı.");
        await Run(c, tx, "UPDATE app_users SET mfa_enabled=false,mfa_secret_protected=NULL,mfa_recovery_hashes='{}' WHERE id=@u; DELETE FROM user_sessions WHERE user_id=@u", ct, ("u", user.Id));
        await PostgresOrganizationStore.Security(c, tx, user.Id, null, "mfa.disabled", ctx, null, ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>Second step of sign-in: the challenge issued by password login plus an authenticator or recovery code.</summary>
    public async Task<object> CompleteMfa(MfaChallengeInput input, RequestContext ctx, CancellationToken ct)
    {
        if (input.Challenge is not { Length: 64 }) throw new PilotHttpException(400, "Doğrulama isteği geçersiz.");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var rows = await Rows(c, tx, """
            SELECT ch.user_id,u.email,u.name,u.mfa_secret_protected,u.mfa_recovery_hashes,u.is_system_admin,u.failed_logins,u.locked_until FROM login_challenges ch
            JOIN app_users u ON u.id=ch.user_id WHERE ch.token_hash=@h AND ch.kind='mfa' AND ch.used_at IS NULL AND ch.expires_at>clock_timestamp() FOR UPDATE OF u
            """, ct, ("h", Hash(input.Challenge)));
        if (rows.Count == 0) throw new PilotHttpException(401, "Doğrulama süresi doldu. Tekrar giriş yapın.");
        var u = rows[0]; var user = (Guid)u["user_id"]!;
        if (u["locked_until"] is DateTime locked && locked > DateTime.UtcNow) throw new PilotHttpException(401, "Hesap geçici olarak kilitli. Bir süre sonra tekrar deneyin.");
        var code = input.Code?.Trim().Replace("-", "").ToUpperInvariant() ?? "";
        var ok = Totp.Verify(protector.Unprotect((string)u["mfa_secret_protected"]!), code, clock.GetUtcNow().ToUnixTimeSeconds());
        if (!ok)
        {
            var hashes = (string[])u["mfa_recovery_hashes"]!; var h = Hash(code);
            if (code.Length == 10 && hashes.Contains(h))
            {
                ok = true;
                await Run(c, tx, "UPDATE app_users SET mfa_recovery_hashes=array_remove(mfa_recovery_hashes,@h) WHERE id=@u", ct, ("h", h), ("u", user));
                await PostgresOrganizationStore.Security(c, tx, user, null, "mfa.recovery_used", ctx, $"{hashes.Length - 1} kod kaldı", ct);
            }
        }
        if (!ok)
        {
            await Run(c, tx, """
                UPDATE app_users SET failed_logins=CASE WHEN failed_logins>=4 THEN 0 ELSE failed_logins+1 END,
                    locked_until=CASE WHEN failed_logins>=4 THEN clock_timestamp()+interval '15 minutes' ELSE NULL END WHERE id=@u
                """, ct, ("u", user));
            await PostgresOrganizationStore.Security(c, tx, user, null, "mfa.failed", ctx, null, ct);
            await tx.CommitAsync(ct);
            throw new PilotHttpException(401, "Kod doğrulanamadı.");
        }
        await Run(c, tx, "UPDATE login_challenges SET used_at=clock_timestamp() WHERE token_hash=@h; UPDATE app_users SET failed_logins=0,locked_until=NULL,last_login_at=clock_timestamp() WHERE id=@u", ct,
            ("h", Hash(input.Challenge)), ("u", user));
        var session = await PostgresIdentityStore.CreateSession(c, tx, user, ct);
        await PostgresOrganizationStore.Security(c, tx, user, null, "login.mfa", ctx, null, ct);
        await tx.CommitAsync(ct);
        return new { token = session, expiresIn = 28800, user = new SessionUser(user, (string)u["email"]!, (string)u["name"]!, (bool)u["is_system_admin"]!, true) };
    }

    private static string RecoveryCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return string.Create(10, alphabet, (span, a) => { for (var i = 0; i < span.Length; i++) span[i] = a[RandomNumberGenerator.GetInt32(a.Length)]; });
    }

    // ---------- SSO (OpenID Connect, authorization code) ----------

    public async Task SaveSso(Guid org, Guid actor, SsoConfigInput input, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        if (string.IsNullOrWhiteSpace(input.Issuer))
        {
            await Run(c, null, "UPDATE organizations SET sso_issuer=NULL,sso_client_id=NULL,sso_client_secret_protected=NULL,sso_domain=NULL,sso_required=false WHERE id=@o", ct, ("o", org));
            await PostgresOrganizationStore.Security(c, null, actor, org, "sso.removed", new RequestContext(null, null), null, ct);
            return;
        }
        var issuer = input.Issuer.Trim().TrimEnd('/');
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && !(AllowHttp && uri.Scheme == "http")))
            throw new PilotHttpException(400, "Issuer https ile başlayan bir adres olmalı.");
        var clientId = Text(input.ClientId, 200, "Client ID");
        var domain = Text(input.Domain, 120, "E-posta alanı").ToLowerInvariant().TrimStart('@');
        if (domain.Contains('@') || !domain.Contains('.')) throw new PilotHttpException(400, "E-posta alanı 'sirket.com' biçiminde olmalı.");
        var existing = (await Rows(c, null, "SELECT sso_client_secret_protected FROM organizations WHERE id=@o", ct, ("o", org)))[0]["sso_client_secret_protected"] as string;
        var secret = string.IsNullOrEmpty(input.ClientSecret) ? existing : protector.Protect(input.ClientSecret.Trim());
        if (secret is null) throw new PilotHttpException(400, "Client secret gerekli.");
        try { await Discovery(issuer, ct); }
        catch (Exception ex) when (ex is not PilotHttpException) { throw new PilotHttpException(400, "Sağlayıcının OpenID ayar belgesi okunamadı: " + ex.GetType().Name); }
        try
        {
            await Run(c, null, "UPDATE organizations SET sso_issuer=@i,sso_client_id=@c,sso_client_secret_protected=@s,sso_domain=@d,sso_required=@r WHERE id=@o", ct,
                ("i", issuer), ("c", clientId), ("s", secret), ("d", domain), ("r", input.Required ?? false), ("o", org));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation) { throw new PilotHttpException(409, "Bu e-posta alanı başka bir organizasyona bağlı."); }
        await PostgresOrganizationStore.Security(c, null, actor, org, "sso.configured", new RequestContext(null, null), $"{issuer} {domain} zorunlu={input.Required ?? false}", ct);
    }

    private async Task<OpenIdConnectConfiguration> Discovery(string issuer, CancellationToken ct)
    {
        ConfigurationManager<OpenIdConnectConfiguration> manager;
        lock (discovery)
        {
            if (!discovery.TryGetValue(issuer, out manager!))
            {
                var retriever = new HttpDocumentRetriever(http.CreateClient("sso")) { RequireHttps = !AllowHttp };
                manager = new ConfigurationManager<OpenIdConnectConfiguration>(issuer + "/.well-known/openid-configuration", new OpenIdConnectConfigurationRetriever(), retriever)
                { AutomaticRefreshInterval = TimeSpan.FromHours(6) };
                discovery[issuer] = manager;
            }
        }
        return await manager.GetConfigurationAsync(ct);
    }

    /// <summary>Finds the organization for an e-mail domain and returns the provider's authorization URL.</summary>
    public async Task<object> StartSso(SsoStartInput input, string? returnTo, CancellationToken ct)
    {
        var email = Email(input.Email); var domain = email[(email.IndexOf('@') + 1)..];
        await using var c = await source.OpenConnectionAsync(ct);
        var rows = await Rows(c, null, "SELECT id,sso_issuer,sso_client_id FROM organizations WHERE lower(sso_domain)=@d AND sso_issuer IS NOT NULL", ct, ("d", domain));
        if (rows.Count == 0) throw new PilotHttpException(404, "Bu e-posta alanı için tek oturum açma tanımlı değil.");
        var org = rows[0]; var config = await Discovery((string)org["sso_issuer"]!, ct);
        var state = Token(); var nonce = Token();
        await Run(c, null, "DELETE FROM sso_handshakes WHERE created_at<clock_timestamp()-interval '15 minutes'; INSERT INTO sso_handshakes(state,organization_id,nonce,return_to) VALUES(@s,@o,@n,@r)", ct,
            ("s", state), ("o", org["id"]), ("n", nonce), ("r", returnTo is { Length: <= 500 } && returnTo.StartsWith('/') ? returnTo : null));
        var query = new Dictionary<string, string?>
        {
            ["response_type"] = "code", ["client_id"] = (string)org["sso_client_id"]!, ["redirect_uri"] = ApiUrl + "/api/v1/auth/sso/callback",
            ["scope"] = "openid email profile", ["state"] = state, ["nonce"] = nonce, ["login_hint"] = email,
        };
        var url = config.AuthorizationEndpoint + (config.AuthorizationEndpoint.Contains('?') ? "&" : "?")
            + string.Join('&', query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}"));
        return new { url };
    }

    /// <summary>Provider callback: exchanges the code, validates the ID token and returns the panel URL carrying a one-time ticket.</summary>
    public async Task<string> CompleteSso(string? code, string? state, string? error, RequestContext ctx, CancellationToken ct)
    {
        if (error is not null) return PanelUrl + "/login?sso=" + Uri.EscapeDataString(error.Length > 60 ? error[..60] : error);
        if (state is not { Length: 64 } || string.IsNullOrEmpty(code) || code.Length > 4096) return PanelUrl + "/login?sso=invalid";
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var rows = await Rows(c, tx, """
            SELECT h.organization_id,h.nonce,h.return_to,o.sso_issuer,o.sso_client_id,o.sso_client_secret_protected,o.sso_domain,o.name org_name FROM sso_handshakes h
            JOIN organizations o ON o.id=h.organization_id WHERE h.state=@s AND h.used_at IS NULL AND h.created_at>clock_timestamp()-interval '15 minutes' FOR UPDATE OF h
            """, ct, ("s", state));
        if (rows.Count == 0) return PanelUrl + "/login?sso=expired";
        var h = rows[0]; var org = (Guid)h["organization_id"]!;
        await Run(c, tx, "UPDATE sso_handshakes SET used_at=clock_timestamp() WHERE state=@s", ct, ("s", state));
        var issuer = (string)h["sso_issuer"]!; var config = await Discovery(issuer, ct);
        var client = http.CreateClient("sso");
        using var tokenResponse = await client.PostAsync(config.TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = ApiUrl + "/api/v1/auth/sso/callback",
            ["client_id"] = (string)h["sso_client_id"]!, ["client_secret"] = protector.Unprotect((string)h["sso_client_secret_protected"]!),
        }), ct);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            await PostgresOrganizationStore.Security(c, tx, null, org, "sso.failed", ctx, "token endpoint " + (int)tokenResponse.StatusCode, ct);
            await tx.CommitAsync(ct);
            return PanelUrl + "/login?sso=token";
        }
        var payload = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var idToken = payload.TryGetProperty("id_token", out var t) ? t.GetString() : null;
        if (idToken is null) return PanelUrl + "/login?sso=token";
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidIssuer = config.Issuer is { Length: > 0 } ? config.Issuer : issuer, ValidAudience = (string)h["sso_client_id"]!,
            IssuerSigningKeys = config.SigningKeys, ClockSkew = TimeSpan.FromMinutes(2),
        });
        if (!result.IsValid)
        {
            await PostgresOrganizationStore.Security(c, tx, null, org, "sso.failed", ctx, "id_token " + result.Exception?.GetType().Name, ct);
            await tx.CommitAsync(ct);
            return PanelUrl + "/login?sso=invalid";
        }
        var claims = result.Claims;
        static string? Claim(IDictionary<string, object> claims, string name) => claims.TryGetValue(name, out var v) ? v?.ToString() : null;
        var email = (Claim(claims, "email") ?? "").ToLowerInvariant();
        var subject = Claim(claims, "sub") ?? "";
        var nonce = Claim(claims, "nonce");
        if (nonce != (string)h["nonce"]! || subject.Length == 0 || !email.EndsWith("@" + (string)h["sso_domain"]!, StringComparison.Ordinal))
        {
            await PostgresOrganizationStore.Security(c, tx, null, org, "sso.failed", ctx, "claims", ct);
            await tx.CommitAsync(ct);
            return PanelUrl + "/login?sso=claims";
        }
        var name = Claim(claims, "name") ?? email[..email.IndexOf('@')];
        var user = await Value(c, tx, "SELECT id FROM app_users WHERE email=@e", ct, ("e", email)) as Guid?;
        if (user is null)
        {
            // First sign-in through the provider creates the account; access is granted by an organization administrator.
            user = Guid.CreateVersion7();
            await Run(c, tx, "INSERT INTO app_users(id,email,name,password_hash,activated_at,sso_subject) VALUES(@u,@e,@n,@h,clock_timestamp(),@s); INSERT INTO organization_members(organization_id,user_id,role) VALUES(@o,@u,'member')", ct,
                ("u", user), ("e", email), ("n", name.Length > 120 ? name[..120] : name), ("h", hasher.HashPassword(email, Token())), ("s", subject), ("o", org));
            await PostgresOrganizationStore.Security(c, tx, user, org, "sso.provisioned", ctx, email, ct);
        }
        else
            await Run(c, tx, "UPDATE app_users SET sso_subject=COALESCE(sso_subject,@s),activated_at=COALESCE(activated_at,clock_timestamp()),last_login_at=clock_timestamp(),failed_logins=0,locked_until=NULL WHERE id=@u", ct, ("s", subject), ("u", user));
        var ticket = Token();
        await Run(c, tx, "INSERT INTO login_challenges(token_hash,user_id,kind,expires_at) VALUES(@h,@u,'sso',clock_timestamp()+interval '2 minutes')", ct, ("h", Hash(ticket)), ("u", user));
        await PostgresOrganizationStore.Security(c, tx, user, org, "login.sso", ctx, issuer, ct);
        await tx.CommitAsync(ct);
        var returnTo = h["return_to"] as string;
        return PanelUrl + "/sso?ticket=" + ticket + (returnTo is null ? "" : "&next=" + Uri.EscapeDataString(returnTo));
    }

    /// <summary>The panel swaps the one-time ticket for a session so the session token never travels through a URL.</summary>
    public async Task<object> RedeemTicket(TicketInput input, CancellationToken ct)
    {
        if (input.Ticket is not { Length: 64 }) throw new PilotHttpException(400, "Bilet geçersiz.");
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var rows = await Rows(c, tx, """
            UPDATE login_challenges SET used_at=clock_timestamp() WHERE token_hash=@h AND kind='sso' AND used_at IS NULL AND expires_at>clock_timestamp() RETURNING user_id
            """, ct, ("h", Hash(input.Ticket)));
        if (rows.Count == 0) throw new PilotHttpException(401, "Bilet süresi doldu. Tekrar giriş yapın.");
        var user = (Guid)rows[0]["user_id"]!;
        var u = (await Rows(c, tx, "SELECT email,name,is_system_admin,mfa_enabled FROM app_users WHERE id=@u", ct, ("u", user)))[0];
        var token = await PostgresIdentityStore.CreateSession(c, tx, user, ct);
        await tx.CommitAsync(ct);
        return new { token, expiresIn = 28800, user = new SessionUser(user, (string)u["email"]!, (string)u["name"]!, (bool)u["is_system_admin"]!, (bool)u["mfa_enabled"]!) };
    }

    // ---------- security events ----------

    public async Task<object> MyEvents(Guid user, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        return await Rows(c, null, "SELECT id,kind,ip,user_agent,detail,created_at FROM security_events WHERE user_id=@u ORDER BY created_at DESC LIMIT 100", ct, ("u", user));
    }

    public async Task<object> OrganizationEvents(Guid org, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        return await Rows(c, null, """
            SELECT s.id,s.kind,s.ip,s.user_agent,s.detail,s.created_at,u.email actor FROM security_events s LEFT JOIN app_users u ON u.id=s.user_id
            WHERE s.organization_id=@o OR (s.organization_id IS NULL AND s.user_id IN (SELECT user_id FROM organization_members WHERE organization_id=@o))
            ORDER BY s.created_at DESC LIMIT 200
            """, ct, ("o", org));
    }

    public async Task<object> OrganizationAudit(Guid org, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        return await Rows(c, null, """
            SELECT a.id,a.action,a.target,a.created_at,p.name project,u.email actor FROM audit_log a JOIN projects p ON p.id=a.project_id LEFT JOIN app_users u ON u.id=a.actor_id
            WHERE p.organization_id=@o ORDER BY a.created_at DESC LIMIT 200
            """, ct, ("o", org));
    }

    // ---------- data export and deletion ----------

    public async Task<object> RequestExport(Guid org, Guid user, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        if ((long)(await Value(c, null, "SELECT count(*) FROM data_exports WHERE organization_id=@o AND state IN ('pending','running')", ct, ("o", org)))! > 0)
            throw new PilotHttpException(409, "Devam eden bir dışa aktarım var.");
        var id = Guid.CreateVersion7();
        await Run(c, null, "INSERT INTO data_exports(id,organization_id,requested_by) VALUES(@id,@o,@u)", ct, ("id", id), ("o", org), ("u", user));
        await PostgresOrganizationStore.Security(c, null, user, org, "data.export_requested", new RequestContext(null, null), id.ToString(), ct);
        return new { id, state = "pending" };
    }

    public async Task<object> Exports(Guid org, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        return await Rows(c, null, """
            SELECT e.id,e.state,e.size_bytes,e.error,e.created_at,e.completed_at,e.expires_at,u.email requested_by FROM data_exports e LEFT JOIN app_users u ON u.id=e.requested_by
            WHERE e.organization_id=@o ORDER BY e.created_at DESC LIMIT 20
            """, ct, ("o", org));
    }

    public async Task<(string Path, string FileName)> ExportFile(Guid org, Guid id, Guid user, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var rows = await Rows(c, null, "SELECT path,completed_at FROM data_exports WHERE id=@id AND organization_id=@o AND state='ready' AND expires_at>clock_timestamp()", ct, ("id", id), ("o", org));
        if (rows.Count == 0 || rows[0]["path"] is not string path || !File.Exists(path)) throw new PilotHttpException(404, "Dışa aktarım bulunamadı ya da süresi doldu.");
        await PostgresOrganizationStore.Security(c, null, user, org, "data.export_downloaded", new RequestContext(null, null), id.ToString(), ct);
        return (path, $"integraops-export-{((DateTime)rows[0]["completed_at"]!):yyyyMMdd}.zip");
    }

    public async Task RequestDeletion(Guid org, SessionUser user, DeletionInput input, RequestContext ctx, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        if (input.Cancel == true)
        {
            await Run(c, tx, "UPDATE organizations SET deletion_requested_at=NULL,deletion_scheduled_at=NULL,deletion_requested_by=NULL WHERE id=@o", ct, ("o", org));
            await PostgresOrganizationStore.Security(c, tx, user.Id, org, "org.deletion_cancelled", ctx, null, ct);
            await tx.CommitAsync(ct);
            return;
        }
        var u = (await Rows(c, tx, "SELECT password_hash FROM app_users WHERE id=@u", ct, ("u", user.Id)))[0];
        if (input.Password is not { Length: > 0 and <= 128 } || hasher.VerifyHashedPassword(user.Email, (string)u["password_hash"]!, input.Password) == PasswordVerificationResult.Failed)
            throw new PilotHttpException(400, "Parola doğrulanamadı.");
        await Run(c, tx, "UPDATE organizations SET plan='suspended',plan_updated_at=clock_timestamp(),deletion_requested_at=clock_timestamp(),deletion_scheduled_at=clock_timestamp()+interval '7 days',deletion_requested_by=@u WHERE id=@o", ct, ("u", user.Id), ("o", org));
        await PostgresOrganizationStore.Security(c, tx, user.Id, org, "org.deletion_requested", ctx, "7 gün sonra silinecek", ct);
        var owners = await Rows(c, tx, "SELECT u.email,u.name FROM organization_members m JOIN app_users u ON u.id=m.user_id WHERE m.organization_id=@o AND m.role='owner'", ct, ("o", org));
        var orgName = (string)(await Value(c, tx, "SELECT name FROM organizations WHERE id=@o", ct, ("o", org)))!;
        foreach (var o in owners)
            await PostgresOperationsQueries.QueueEmail(c, tx, (string)o["email"]!, $"IntegraOps · {orgName} silme isteği",
                $"Merhaba {o["name"]},\n\n\"{orgName}\" organizasyonunun silinmesi {user.Email} tarafından istendi. Hesap askıya alındı; tüm veriler 7 gün sonra kalıcı olarak silinecek.\n\n"
                + $"Vazgeçmek için 7 gün içinde panelden isteği iptal edin: {PanelUrl}/organization\n", ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>Removes every record of an organization after the grace period. Runs in the worker; returns the number of projects purged.</summary>
    public async Task<int> PurgeDue(CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var due = await Rows(c, null, "SELECT id,name FROM organizations WHERE deletion_scheduled_at IS NOT NULL AND deletion_scheduled_at<=clock_timestamp()", ct);
        var purged = 0;
        foreach (var org in due)
        {
            var id = (Guid)org["id"]!;
            await using var tx = await c.BeginTransactionAsync(ct);
            purged += (int)(long)(await Value(c, tx, PurgeSql, ct, ("o", id)))!;
            await tx.CommitAsync(ct);
        }
        return purged;
    }

    private const string PurgeSql = """
        CREATE TEMP TABLE doomed_projects ON COMMIT DROP AS SELECT id FROM projects WHERE organization_id=@o;
        CREATE TEMP TABLE doomed_tx ON COMMIT DROP AS SELECT id FROM transactions WHERE project_id IN (SELECT id FROM doomed_projects);
        CREATE TEMP TABLE doomed_flows ON COMMIT DROP AS SELECT id FROM flows WHERE project_id IN (SELECT id FROM doomed_projects);
        DELETE FROM ai_analyses WHERE project_id IN (SELECT id FROM doomed_projects);
        DELETE FROM alarm_notes WHERE alarm_id IN (SELECT id FROM alarms WHERE project_id IN (SELECT id FROM doomed_projects));
        DELETE FROM alarm_events WHERE alarm_id IN (SELECT id FROM alarms WHERE project_id IN (SELECT id FROM doomed_projects));
        DELETE FROM notification_deliveries WHERE channel_id IN (SELECT id FROM notification_channels WHERE project_id IN (SELECT id FROM doomed_projects));
        DELETE FROM alarms WHERE project_id IN (SELECT id FROM doomed_projects);
        DELETE FROM reconciliation_discrepancies WHERE project_id IN (SELECT id FROM doomed_projects);
        DELETE FROM reconciliation_runs WHERE project_id IN (SELECT id FROM doomed_projects);
        DELETE FROM transaction_step_stats WHERE project_id IN (SELECT id FROM doomed_projects);
        DELETE FROM transaction_delays WHERE project_id IN (SELECT id FROM doomed_projects);
        DELETE FROM transaction_events WHERE project_id IN (SELECT id FROM doomed_projects);
        DELETE FROM transactions WHERE project_id IN (SELECT id FROM doomed_projects);
        DELETE FROM outbox_messages WHERE project_id IN (SELECT id FROM doomed_projects);
        DELETE FROM flow_step_overrides WHERE version_id IN (SELECT id FROM flow_versions WHERE flow_id IN (SELECT id FROM doomed_flows));
        DELETE FROM flow_version_steps WHERE version_id IN (SELECT id FROM flow_versions WHERE flow_id IN (SELECT id FROM doomed_flows));
        DELETE FROM flow_versions WHERE flow_id IN (SELECT id FROM doomed_flows);
        DELETE FROM flow_steps WHERE flow_id IN (SELECT id FROM doomed_flows);
        DELETE FROM flows WHERE id IN (SELECT id FROM doomed_flows);
        DELETE FROM calendar_holidays WHERE calendar_id IN (SELECT id FROM business_calendars WHERE project_id IN (SELECT id FROM doomed_projects));
        DELETE FROM business_calendars WHERE project_id IN (SELECT id FROM doomed_projects);
        DELETE FROM notification_channels WHERE project_id IN (SELECT id FROM doomed_projects);
        DELETE FROM api_keys WHERE project_id IN (SELECT id FROM doomed_projects);
        DELETE FROM connections WHERE project_id IN (SELECT id FROM doomed_projects);
        DELETE FROM agent_heartbeats WHERE project_id IN (SELECT id FROM doomed_projects);
        DELETE FROM report_digests WHERE project_id IN (SELECT id FROM doomed_projects);
        DELETE FROM retention_runs WHERE project_id IN (SELECT id FROM doomed_projects);
        DELETE FROM retention_policies WHERE project_id IN (SELECT id FROM doomed_projects);
        DELETE FROM saved_views WHERE project_id IN (SELECT id FROM doomed_projects);
        DELETE FROM project_settings WHERE project_id IN (SELECT id FROM doomed_projects);
        DELETE FROM audit_log WHERE project_id IN (SELECT id FROM doomed_projects);
        DELETE FROM project_members WHERE project_id IN (SELECT id FROM doomed_projects);
        DELETE FROM project_group_projects WHERE project_id IN (SELECT id FROM doomed_projects);
        DELETE FROM project_groups WHERE organization_id=@o;
        DELETE FROM data_exports WHERE organization_id=@o;
        DELETE FROM usage_daily WHERE organization_id=@o;
        DELETE FROM organization_limits WHERE organization_id=@o;
        DELETE FROM sso_handshakes WHERE organization_id=@o;
        DELETE FROM security_events WHERE organization_id=@o;
        DELETE FROM organization_members WHERE organization_id=@o;
        DELETE FROM projects WHERE organization_id=@o;
        DELETE FROM organizations WHERE id=@o;
        SELECT count(*) FROM doomed_projects
        """;
}
