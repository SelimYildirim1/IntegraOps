using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IntegraOps.Infrastructure;

public sealed class PilotHttpException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
public sealed record LoginInput(string Email, string Password);
public sealed record FirstOwnerInput(string Email, string Name, string Password);
public sealed record MemberInput(string Email, string Name, string Role, string? Password);
public sealed record KeyInput(string Name, string Scope);
public sealed record PasswordInput(string CurrentPassword, string NewPassword);
public sealed record ChannelInput(string Name, string Kind, string Target, string? Secret);
public sealed record EnabledInput(bool Enabled);
public sealed record AssignInput(string? UserId);
public sealed record NoteInput(string? Body);
public sealed record SavedViewInput(string Kind, string Name, string Query);
public sealed record InviteInput(string Email, string Name, string Role);
public sealed record ResetRequestInput(string Email);
public sealed record TokenPasswordInput(string Password);
public sealed record AgentHeartbeatInput(string? Name, string? Version, string? Host, string? SourceKind, int? Pending,
    long? SentTotal, long? DeadTotal, long? RetryTotal, string? LastError, string? LastSuccessAt, System.Text.Json.JsonElement? Config);
public sealed record RetentionInput(int? FinishedTransactionDays, int? NotificationDays, int? AuditDays);
public sealed record SessionUser(Guid Id, string Email, string Name);
public sealed record ProjectIdentity(Guid ProjectId, Guid OrganizationId, Guid? UserId, string Role, string? Scope);

public static class PilotSecurity
{
    public static string Token() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static string Text(string? text, int max, string field)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > max)
            throw new PilotHttpException(400, $"{field}: 1–{max} karakter gerekli.");
        return text;
    }
    public static string Email(string? value)
    {
        var email = Text(value, 254, "E-posta").ToLowerInvariant();
        if (!MailAddress.TryCreate(email, out var address) || address.Address != email)
            throw new PilotHttpException(400, "E-posta adresi geçersiz.");
        return email;
    }
    public static string Password(string? value) => value is { Length: >= 12 and <= 128 }
        ? value : throw new PilotHttpException(400, "Parola 12–128 karakter olmalı.");
    public static string Role(string value) => value is "owner" or "operator" or "viewer"
        ? value : throw new PilotHttpException(400, "Rol geçersiz.");
    public static IServiceCollection AddPilot(this IServiceCollection services, IConfiguration configuration)
    {
        var directory = configuration["Protection:KeyPath"] ?? Path.Combine(AppContext.BaseDirectory, "keys");
        Directory.CreateDirectory(directory);
        services.AddDataProtection().SetApplicationName("IntegraOps.V1")
            .PersistKeysToFileSystem(new DirectoryInfo(directory));
        services.AddSingleton<PostgresIdentityStore>();
        services.AddSingleton<PostgresPilotQueries>();
        services.AddSingleton<NotificationTransport>();
        services.AddSingleton<PostgresFlowVersionStore>();
        services.AddSingleton<IConfigurationReader, ConfigurationReader>();
        services.AddSingleton<PostgresReconciliationStore>();
        return services;
    }
}
