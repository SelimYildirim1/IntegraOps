using IntegraOps.Infrastructure;
using Xunit;

public class AiTests
{
    private static readonly HashSet<string> Ids = ["A1", "E1", "B1"];

    [Fact]
    public void Keeps_only_known_evidence_and_marks_ungrounded_findings()
    {
        var insight = AlarmInsightPrompt.Parse("""
            Açıklama:
            ```json
            {"summary":"Fatura bildirimi gelmedi.","cause":"delay",
             "findings":[{"text":"Süre doldu.","evidence":["A1","E1","Z9"]},{"text":"Tahmin.","evidence":["Z9"]},{"text":"","evidence":["A1"]}],
             "checks":["Kuyruğu kontrol edin."],"unknowns":["Fatura kaynakta oluştu mu bilinmiyor."]}
            ```
            """, Ids);
        Assert.Equal("delay", insight.Cause);
        Assert.Equal(2, insight.Findings.Count);
        Assert.Equal(["A1", "E1"], insight.Findings[0].Evidence);
        Assert.True(insight.Findings[0].Grounded);
        Assert.False(insight.Findings[1].Grounded);
        Assert.Single(insight.Checks);
        Assert.Single(insight.Unknowns);
    }

    [Fact]
    public void Unknown_cause_falls_back_and_lists_are_bounded()
    {
        var checks = string.Join(",", Enumerable.Range(0, 20).Select(i => $"\"c{i}\""));
        var insight = AlarmInsightPrompt.Parse($$"""{"summary":"x","cause":"network","checks":[{{checks}}]}""", Ids);
        Assert.Equal("unknown", insight.Cause);
        Assert.Equal(8, insight.Checks.Count);
        Assert.Empty(insight.Findings);
    }

    [Theory]
    [InlineData("yanıt yok")]
    [InlineData("{\"cause\":\"delay\"}")]
    [InlineData("{\"summary\":")]
    public void Rejects_output_without_a_summary(string text) =>
        Assert.ThrowsAny<Exception>(() => AlarmInsightPrompt.Parse(text, Ids));
}
