using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IntegraOps.Infrastructure;

/// <summary>The alarm notification as the channels present it: parsed once from the delivery body, rendered per channel kind.</summary>
public sealed record AlarmMessage(
    bool Opened, string AlarmKind, string AlarmId, string Flow, string TransactionId, string StepCode, string StepName,
    string? Error, string? DeadlineAt, string? Resolution, string Instructions, string? Link, string? PanelUrl)
{
    public string Title => Opened ? "Alarm açıldı" : "Alarm kapandı";
    public string Reason => AlarmKind == "overdue" ? "Beklenen bildirim süresinde gelmedi" : "Kaynak sistem hata bildirdi";
    public string StepLabel => string.IsNullOrEmpty(StepName) || StepName == StepCode ? StepCode : $"{StepName} ({StepCode})";
    public string ResolutionText => Resolution == "step_completed" ? "Eksik adım bildirildi" : Resolution == "cancelled" ? "İşlem iptal edildi" : "Koşul artık geçerli değil";

    public static AlarmMessage Parse(string body, string? panelBaseUrl)
    {
        using var payload = JsonDocument.Parse(body);
        var data = payload.RootElement;
        string? Field(string name) => data.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : null;
        var alarmId = Field("alarm_id") ?? "";
        var panel = string.IsNullOrWhiteSpace(panelBaseUrl) || alarmId.Length == 0 ? null : panelBaseUrl.TrimEnd('/') + "/alarms/" + alarmId;
        return new AlarmMessage(Field("event") == "opened", Field("alarm_kind") ?? "", alarmId, Field("flow") ?? "", Field("transaction_id") ?? "",
            Field("step_code") ?? "", Field("step_name") ?? "", Field("error"), FormatTime(Field("deadline_at")), Field("resolution"),
            Field("instructions") ?? "", Field("link"), panel);
    }

    private static string? FormatTime(string? value)
    {
        if (value is null || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)) return value;
        var istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        return TimeZoneInfo.ConvertTime(at, istanbul).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) + " (TR)";
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
    // Chat services render the text as-is; keep Turkish characters readable instead of \uXXXX escapes.
    private static readonly JsonSerializerOptions Readable = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Plain-text e-mail body.</summary>
    public string EmailBody(Guid delivery)
    {
        var lines = new List<string> { Title, "", Reason, $"İşlem: {TransactionId}", $"Akış: {Flow}", $"Adım: {StepLabel}" };
        if (Opened) lines.Add($"Beklenen son zaman: {DeadlineAt ?? "—"}"); else lines.Add($"Kapanış nedeni: {ResolutionText}");
        if (IntegraOps.Domain.ErrorExplainer.Plain(Error) is { } plain) lines.Add($"Ne oldu: {plain}");
        if (!string.IsNullOrEmpty(Error)) lines.Add($"Kaynak sistemin ham hatası: {Error}");
        if (Opened && Instructions.Length > 0) { lines.Add(""); lines.Add("Ne yapılmalı:"); lines.Add(Instructions); }
        if (Link is not null) lines.Add($"Kaynak sistemde aç: {Link}");
        if (PanelUrl is not null) lines.Add($"Panelde aç: {PanelUrl}");
        lines.Add(""); lines.Add($"Alarm: {AlarmId}"); lines.Add($"Bildirim: {delivery}");
        return string.Join("\n", lines);
    }

    /// <summary>Slack incoming-webhook payload (Block Kit with a plain-text fallback).</summary>
    public string Slack()
    {
        var icon = Opened ? (AlarmKind == "overdue" ? "⏰" : "🔴") : "✅";
        var headline = $"{icon} {Title}: {TransactionId}";
        var fields = new JsonArray(
            Field("Akış", Flow), Field("Adım", StepLabel),
            Opened ? Field("Neden", Reason) : Field("Kapanış", ResolutionText),
            Opened ? Field("Beklenen son zaman", DeadlineAt ?? "—") : Field("Alarm", AlarmKind == "overdue" ? "Süre aşımı" : "Hata"));
        var blocks = new JsonArray(
            new JsonObject { ["type"] = "header", ["text"] = new JsonObject { ["type"] = "plain_text", ["text"] = Clip(headline, 150), ["emoji"] = true } },
            new JsonObject { ["type"] = "section", ["fields"] = fields });
        if (IntegraOps.Domain.ErrorExplainer.Plain(Error) is { } plain)
            blocks.Add(new JsonObject { ["type"] = "section", ["text"] = Md($"*Ne oldu*\n{Clip(plain, 900)}") });
        if (!string.IsNullOrEmpty(Error))
            blocks.Add(new JsonObject { ["type"] = "section", ["text"] = Md($"*Kaynak sistemin ham hatası*\n```{Clip(Error, 900)}```") });
        if (Opened && Instructions.Length > 0)
            blocks.Add(new JsonObject { ["type"] = "section", ["text"] = Md($"*Ne yapılmalı*\n{Clip(Instructions, 1500)}") });
        var buttons = new JsonArray();
        if (PanelUrl is not null) buttons.Add(Button("Panelde aç", PanelUrl, primary: true));
        if (Link is not null) buttons.Add(Button("Kaynak sistemde aç", Link));
        if (buttons.Count > 0) blocks.Add(new JsonObject { ["type"] = "actions", ["elements"] = buttons });
        blocks.Add(new JsonObject { ["type"] = "context", ["elements"] = new JsonArray(Md($"IntegraOps · alarm {AlarmId}")) });
        return new JsonObject { ["text"] = headline + $" · {Flow} / {StepLabel}", ["blocks"] = blocks }.ToJsonString(Readable);

        static JsonObject Md(string text) => new() { ["type"] = "mrkdwn", ["text"] = text };
        static JsonObject Field(string label, string value) => Md($"*{label}*\n{value}");
        static JsonObject Button(string text, string url, bool primary = false)
        {
            var b = new JsonObject { ["type"] = "button", ["text"] = new JsonObject { ["type"] = "plain_text", ["text"] = text }, ["url"] = url };
            if (primary) b["style"] = "primary";
            return b;
        }
    }

    /// <summary>Microsoft Teams payload: an Adaptive Card, accepted by both Incoming Webhook connectors and Workflows.</summary>
    public string Teams()
    {
        var color = Opened ? "attention" : "good";
        var facts = new JsonArray(Fact("İşlem", TransactionId), Fact("Akış", Flow), Fact("Adım", StepLabel),
            Opened ? Fact("Neden", Reason) : Fact("Kapanış", ResolutionText));
        if (Opened) facts.Add(Fact("Beklenen son zaman", DeadlineAt ?? "—"));
        var body = new JsonArray(
            new JsonObject { ["type"] = "TextBlock", ["size"] = "Large", ["weight"] = "Bolder", ["color"] = color, ["text"] = $"{Title}: {TransactionId}", ["wrap"] = true },
            new JsonObject { ["type"] = "FactSet", ["facts"] = facts });
        if (IntegraOps.Domain.ErrorExplainer.Plain(Error) is { } plain)
            body.Add(new JsonObject { ["type"] = "TextBlock", ["text"] = "Ne oldu: " + Clip(plain, 900), ["wrap"] = true });
        if (!string.IsNullOrEmpty(Error))
            body.Add(new JsonObject { ["type"] = "TextBlock", ["text"] = "Kaynak sistemin ham hatası: " + Clip(Error, 900), ["wrap"] = true, ["fontType"] = "Monospace" });
        if (Opened && Instructions.Length > 0)
        {
            body.Add(new JsonObject { ["type"] = "TextBlock", ["weight"] = "Bolder", ["text"] = "Ne yapılmalı", ["spacing"] = "Medium" });
            body.Add(new JsonObject { ["type"] = "TextBlock", ["text"] = Clip(Instructions, 1500), ["wrap"] = true });
        }
        var actions = new JsonArray();
        if (PanelUrl is not null) actions.Add(new JsonObject { ["type"] = "Action.OpenUrl", ["title"] = "Panelde aç", ["url"] = PanelUrl });
        if (Link is not null) actions.Add(new JsonObject { ["type"] = "Action.OpenUrl", ["title"] = "Kaynak sistemde aç", ["url"] = Link });
        var card = new JsonObject
        {
            ["type"] = "AdaptiveCard", ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json", ["version"] = "1.4",
            ["msteams"] = new JsonObject { ["width"] = "Full" }, ["body"] = body,
        };
        if (actions.Count > 0) card["actions"] = actions;
        return new JsonObject
        {
            ["type"] = "message",
            ["summary"] = $"{Title}: {TransactionId}",
            ["attachments"] = new JsonArray(new JsonObject { ["contentType"] = "application/vnd.microsoft.card.adaptive", ["contentUrl"] = null, ["content"] = card }),
        }.ToJsonString(Readable);

        static JsonObject Fact(string title, string value) => new() { ["title"] = title, ["value"] = value };
    }
}
