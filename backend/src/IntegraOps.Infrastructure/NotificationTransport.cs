using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace IntegraOps.Infrastructure;

public sealed class DeliveryException(string code, bool permanent = false) : Exception(code)
{
    public bool Permanent { get; } = permanent;
}

public sealed class NotificationTransport : IDisposable
{
    private readonly IConfiguration configuration;
    private readonly HashSet<string> origins;
    private readonly HashSet<string> privateOrigins;
    private readonly HttpClient client;

    public NotificationTransport(IConfiguration configuration)
    {
        this.configuration = configuration;
        origins = ParseOrigins(configuration["Notifications:WebhookOrigins"]);
        privateOrigins = ParseOrigins(configuration["Notifications:PrivateWebhookOrigins"]);
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false,
            MaxConnectionsPerServer = 4, PooledConnectionLifetime = TimeSpan.FromMinutes(1), ConnectCallback = Connect };
        client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
    }

    private static HashSet<string> ParseOrigins(string? values) => (values ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => new Uri(x).GetLeftPart(UriPartial.Authority)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public Uri ValidateTarget(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment) || uri.Scheme is not ("https" or "http"))
            throw new PilotHttpException(400, "Webhook adresi geçersiz.");
        var origin = uri.GetLeftPart(UriPartial.Authority);
        if (!origins.Contains(origin) || (uri.Scheme != "https" && !privateOrigins.Contains(origin)))
            throw new PilotHttpException(400, "Webhook hedefi sunucunun izin verilen adresleri arasında değil.");
        return uri;
    }

    public static bool PublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var b = address.GetAddressBytes();
        if (b.Length == 4)
            return b[0] != 0 && b[0] != 10 && b[0] != 127 && b[0] < 224
                && !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] is >= 16 and <= 31)
                && !(b[0] == 192 && b[1] == 168) && !(b[0] == 100 && b[1] is >= 64 and <= 127)
                && !(b[0] == 198 && b[1] is 18 or 19);
        // Only ordinary global IPv6 unicast; excludes local, multicast and IPv4 transition ranges.
        return (b[0] & 0xe0) == 0x20 && !(b[0] == 0x20 && b[1] == 0x02)
            && !(b[0] == 0x20 && b[1] == 0x01 && b[2] == 0 && b[3] == 0);
    }

    private async ValueTask<Stream> Connect(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var uri = ValidateTarget(context.InitialRequestMessage.RequestUri!.AbsoluteUri);
        var allowPrivate = privateOrigins.Contains(uri.GetLeftPart(UriPartial.Authority));
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
        if (addresses.Length == 0 || (!allowPrivate && addresses.Any(a => !PublicAddress(a))))
            throw new DeliveryException("webhook_address_rejected", true);
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch { socket.Dispose(); if (ct.IsCancellationRequested) throw; }
        }
        throw new DeliveryException("webhook_connection_failed");
    }

    public async Task Send(Guid delivery, string kind, string target, string? secret, string body, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        if (kind == "webhook")
        {
            var uri = ValidateTarget(target);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
            var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret!),
                Encoding.UTF8.GetBytes($"{timestamp}.{delivery}.{body}"))).ToLowerInvariant();
            using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            request.Headers.Add("X-IntegraOps-Delivery", delivery.ToString());
            request.Headers.Add("X-IntegraOps-Time", timestamp);
            request.Headers.Add("X-IntegraOps-Signature", "sha256=" + signature);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new DeliveryException("webhook_http_" + (int)response.StatusCode,
                    (int)response.StatusCode is >= 300 and < 500 && (int)response.StatusCode is not (408 or 425 or 429));
            return;
        }
        var host = configuration["Smtp:Host"] ?? throw new DeliveryException("smtp_not_configured", true);
        var from = configuration["Smtp:From"] ?? throw new DeliveryException("smtp_from_missing", true);
        using var smtp = new SmtpClient(host, configuration.GetValue("Smtp:Port", 587))
        {
            EnableSsl = configuration.GetValue("Smtp:StartTls", true),
            UseDefaultCredentials = false,
            Timeout = 15000
        };
        if (!string.IsNullOrEmpty(configuration["Smtp:User"]))
            smtp.Credentials = new NetworkCredential(configuration["Smtp:User"], configuration["Smtp:Password"]);
        using var payload = JsonDocument.Parse(body);
        var data = payload.RootElement;
        string Field(string name) => data.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : "—";
        var change = Field("event") == "opened" ? "Alarm açıldı" : "Alarm kapandı";
        var reason = Field("alarm_kind") == "overdue" ? "Beklenen bildirim süresinde gelmedi" : "Kaynak sistem hata bildirdi";
        var emailBody = $"{change}\n\n{reason}\nİşlem: {Field("transaction_id")}\nAkış: {Field("flow")}\nAdım: {Field("step_code")}\n"
            + $"Beklenen son zaman: {Field("deadline_at")}\nAçıklama: {Field("error")}\n\nAlarm: {Field("alarm_id")}\nBildirim: {delivery}";
        using var message = new MailMessage(from, target, $"IntegraOps · {change} · {Field("transaction_id")}", emailBody) { IsBodyHtml = false };
        message.Headers.Add("Message-ID", $"<{delivery}@integraops.local>");
        await smtp.SendMailAsync(message, timeout.Token);
    }

    public void Dispose() => client.Dispose();
}
