using System.Text.Json;
using IntegraOps.Domain;
using IntegraOps.Infrastructure;
using Xunit;

namespace IntegraOps.Tests;

/// <summary>Dalga 1: chat channels, step runbooks and source links, sector templates.</summary>
public sealed class WaveOneTests
{
    private const string Body = """
        {"alarm_id":"0192f3c4-8b1a-7c2e-9f10-3a4b5c6d7e8f","project_id":"p","flow":"ecommerce-order","transaction_id":"SO-1001",
         "alarm_kind":"overdue","step_code":"invoice-created","step_name":"Fatura kesildi","error":null,
         "deadline_at":"2026-09-24T10:00:00+00:00","opened_at":"2026-09-24T10:00:01+00:00","resolved_at":null,"resolution":null,
         "instructions":"e-Fatura kuyruğuna bakın.","link":"https://erp.example.com/orders/SO-1001","event":"opened","schema_version":1}
        """;

    [Fact]
    public void Link_templates_render_placeholders_and_escape_the_transaction_id()
    {
        Assert.Equal("https://erp.example.com/o/SO%201?f=order-flow&s=invoice",
            StepLinks.Render("https://erp.example.com/o/{transactionId}?f={flow}&s={step}", "order-flow", "SO 1", "invoice"));
        Assert.Null(StepLinks.Render("", "f", "t", "s"));
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("https://erp.example.com/orders/{transactionId}", null)]
    [InlineData("http://10.0.0.5/orders/{transactionId}", null)]
    [InlineData("erp.example.com/orders", "tam bir adres")]
    [InlineData("https://erp.example.com/{order}", "yalnızca")]
    [InlineData("ftp://erp.example.com/x", "tam bir adres")]
    [InlineData("https://user:pw@erp.example.com/x", "tam bir adres")]
    public void Link_templates_are_validated(string template, string? expectedFragment)
    {
        var error = StepLinks.Validate(template);
        if (expectedFragment is null) Assert.Null(error);
        else Assert.Contains(expectedFragment, error);
    }

    [Fact]
    public void Diff_reports_runbook_and_link_changes()
    {
        var v1 = new FlowDefinition(Guid.NewGuid(), Guid.NewGuid(), "f", "F", [new("a", "A", 0, null), new("b", "B", 1, 60)]);
        var v2 = new FlowDefinition(v1.Id, v1.ProjectId, "f", "F", [new("a", "A", 0, null), new("b", "B", 1, 60, Instructions: "Kuyruğa bak", LinkTemplate: "https://x.example.com/{transactionId}")]);
        var changes = FlowDiff.Compare(v1, v2);
        Assert.Contains(changes, c => c.Kind == "instructions" && c.Code == "b" && c.Detail == "talimat eklendi");
        Assert.Contains(changes, c => c.Kind == "link" && c.Code == "b");
        Assert.Empty(FlowDiff.Compare(v2, v2));
    }

    [Fact]
    public void Projection_renders_links_for_the_transaction()
    {
        var flow = new FlowDefinition(Guid.NewGuid(), Guid.NewGuid(), "f", "F",
            [new("a", "A", 0, null, LinkTemplate: "https://x.example.com/{transactionId}", Instructions: "Bak"), new("b", "B", 1, 60)]);
        var p = TimelineProjector.Project(flow, [], null, "SO-7");
        Assert.Equal("https://x.example.com/SO-7", p.Steps[0].Link);
        Assert.Equal("Bak", p.Steps[0].Instructions);
        Assert.Null(p.Steps[1].Link);
        Assert.Null(TimelineProjector.Project(flow, []).Steps[0].Link); // no transaction id, no link
    }

    [Fact]
    public void Slack_payload_carries_runbook_and_buttons()
    {
        var message = AlarmMessage.Parse(Body, "https://panel.example.com/");
        using var doc = JsonDocument.Parse(message.Slack());
        var root = doc.RootElement;
        Assert.Contains("SO-1001", root.GetProperty("text").GetString());
        var blocks = root.GetProperty("blocks").EnumerateArray().ToList();
        Assert.Equal("header", blocks[0].GetProperty("type").GetString());
        var text = string.Join(" ", blocks.Select(b => b.ToString()));
        Assert.Contains("e-Fatura kuyruğuna bakın.", text);
        Assert.Contains("Fatura kesildi (invoice-created)", text);
        var buttons = blocks.Single(b => b.GetProperty("type").GetString() == "actions").GetProperty("elements").EnumerateArray()
            .Select(e => e.GetProperty("url").GetString()).ToList();
        Assert.Equal(["https://panel.example.com/alarms/0192f3c4-8b1a-7c2e-9f10-3a4b5c6d7e8f", "https://erp.example.com/orders/SO-1001"], buttons);
    }

    [Fact]
    public void Teams_payload_is_an_adaptive_card()
    {
        var message = AlarmMessage.Parse(Body.Replace("\"event\":\"opened\"", "\"event\":\"resolved\"").Replace("\"resolution\":null", "\"resolution\":\"step_completed\""), null);
        using var doc = JsonDocument.Parse(message.Teams());
        var attachment = doc.RootElement.GetProperty("attachments")[0];
        Assert.Equal("application/vnd.microsoft.card.adaptive", attachment.GetProperty("contentType").GetString());
        var card = attachment.GetProperty("content");
        Assert.Equal("AdaptiveCard", card.GetProperty("type").GetString());
        var body = card.GetProperty("body").ToString();
        Assert.Contains("Alarm kapandı", body);
        Assert.Contains("Eksik adım bildirildi", body);
        Assert.DoesNotContain("Ne yapılmalı", body); // runbook only while the alarm is open
        Assert.Single(card.GetProperty("actions").EnumerateArray()); // no panel url configured, only the source link
    }

    [Fact]
    public void Email_body_lists_runbook_and_links()
    {
        var text = AlarmMessage.Parse(Body, "https://panel.example.com").EmailBody(Guid.Empty);
        Assert.Contains("Ne yapılmalı:", text);
        Assert.Contains("Kaynak sistemde aç: https://erp.example.com/orders/SO-1001", text);
        Assert.Contains("Panelde aç: https://panel.example.com/alarms/", text);
    }

    [Theory]
    [InlineData("slack", "hooks.slack.com", true)]
    [InlineData("slack", "hooks.slack.com.evil.example", false)]
    [InlineData("teams", "contoso.webhook.office.com", true)]
    [InlineData("teams", "prod-12.westeurope.logic.azure.com", true)]
    [InlineData("teams", "hooks.slack.com", false)]
    [InlineData("webhook", "hooks.slack.com", false)]
    public void Chat_hosts_are_recognised_per_kind(string kind, string host, bool expected) =>
        Assert.Equal(expected, NotificationTransport.ChatHost(kind, host));

    [Fact]
    public void Every_template_passes_version_validation_and_has_runbooks()
    {
        Assert.Equal(6, FlowTemplates.All.Count);
        foreach (var t in FlowTemplates.All)
        {
            var input = t.Steps.Select(s => new VersionStepInput(s.Code, s.Name, s.MaxDelaySeconds,
                s.Overrides is null ? null : new Dictionary<string, int>(s.Overrides), s.EffectiveStage, s.Kind, s.Groups?.ToList(), s.Instructions, s.LinkTemplate)).ToList();
            var steps = PostgresFlowVersionStore.ValidateSteps(input);
            Assert.Equal(t.Steps.Count, steps.Count);
            Assert.All(t.Steps, s => Assert.False(string.IsNullOrWhiteSpace(s.Instructions), $"{t.Code}/{s.Code} talimatsız"));
            Assert.All(t.Errors.Keys, k => Assert.Contains(t.Steps, s => s.Code == k));
        }
        Assert.Equal(FlowTemplates.All.Count, FlowTemplates.All.Select(t => t.Code).Distinct().Count());
    }

    [Theory]
    [InlineData("GİB e-fatura entegratörü 503 Service Unavailable; 3 deneme başarısız (fatura no atanamadı)", "unavailable", "E-fatura servisi yanıt vermedi (geçici kesinti)", "(HTTP 503)", "(3 deneme yapıldı)")]
    [InlineData("ERP outbox timeout after 30000 ms (job 48121)", "timeout", "ERP sistemi beklenen sürede yanıt vermedi", "", "")]
    [InlineData("Vergi numarası geçersiz", "validation", "Gönderilen bilgi kabul edilmedi (veri hatası)", "", "")]
    [InlineData("Kargo API 502", "unavailable", "Kargo firması servisi yanıt vermedi (geçici kesinti)", "(HTTP 502)", "")]
    [InlineData("payment declined by bank", "payment", "Ödeme reddedildi", "", "")]
    public void Source_errors_are_explained_in_plain_language(string error, string kind, string title, string http, string retries)
    {
        var x = IntegraOps.Domain.ErrorExplainer.Explain(error)!;
        Assert.Equal(kind, x.Kind); Assert.Equal(title, x.Title);
        if (http.Length > 0) Assert.Contains(http, x.Detail);
        if (retries.Length > 0) Assert.Contains(retries, x.Detail);
        Assert.False(string.IsNullOrWhiteSpace(x.Who));
    }

    [Fact]
    public void Unrecognised_or_empty_errors_are_not_explained()
    {
        Assert.Null(IntegraOps.Domain.ErrorExplainer.Explain(""));
        Assert.Null(IntegraOps.Domain.ErrorExplainer.Explain("  "));
        Assert.Null(IntegraOps.Domain.ErrorExplainer.Explain("X7Q"));
        Assert.Null(IntegraOps.Domain.ErrorExplainer.Plain(null));
    }
}
