using Microsoft.Extensions.Configuration;
using IntegraOps.Infrastructure;
using Xunit;

public class AiTests
{
    private static readonly HashSet<string> Ids = ["A1", "E1", "B1"];

    [Fact]
    public void Keeps_only_known_evidence_and_marks_ungrounded_findings()
    {
        var insight = InsightPrompt.Parse("""
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

    [Theory]
    [InlineData(1, 300)] [InlineData(600, 600)] [InlineData(601, 900)] [InlineData(1044, 1200)] [InlineData(5000, 5400)] [InlineData(700000, 777600)] [InlineData(9_999_999, 2_592_000)]
    public void Suggested_allowances_round_up_to_values_operators_type(long seconds, int expected) =>
        Assert.Equal(expected, PostgresThresholdAdvisor.Nice(seconds));

    [Fact]
    public void Threshold_output_keeps_system_recommendations_and_grounding()
    {
        var ids = new HashSet<string> { "S1", "S2", "F1" };
        var parsed = ThresholdPrompt.Parse("""{"summary":"Fatura sınırı yükseltilmeli.","steps":[{"evidence":["S1","F1"],"recommendation":"raise","reason":"Sahte alarm."},{"evidence":["F1"],"recommendation":"double","reason":"Kanıtsız."}],"unknowns":[]}""", ids);
        Assert.Equal(2, parsed.Steps.Count);
        Assert.True(parsed.Steps[0].Grounded); Assert.Equal("raise", parsed.Steps[0].Recommendation);
        Assert.False(parsed.Steps[1].Grounded); Assert.Equal("keep", parsed.Steps[1].Recommendation);
    }

    [Fact]
    public void Triage_output_is_grounded_per_group_and_classes_are_bounded()
    {
        var ids = new HashSet<string> { "G1", "G2", "C1", "T1" };
        var triage = TriagePrompt.Parse("""
            {"summary":"İki grup var.","groups":[
              {"evidence":["G1","C1","Z9"],"priority":"critical","hypothesis":"ERP göndericisi durdu.","action":"ERP ekibine yaz.","likely_cause":"source_outage"},
              {"evidence":["C1"],"priority":"urgent","hypothesis":"Kanıtsız grup.","action":"","likely_cause":"gremlins"},
              {"evidence":["G2"],"priority":"low"}],
             "unknowns":["Faturaların ERP'de kesilip kesilmediği"]}
            """, ids);
        Assert.Equal(2, triage.Groups.Count);
        Assert.True(triage.Groups[0].Grounded); Assert.Equal(["G1", "C1"], triage.Groups[0].Evidence); Assert.Equal("critical", triage.Groups[0].Priority);
        Assert.False(triage.Groups[1].Grounded); Assert.Equal("normal", triage.Groups[1].Priority); Assert.Equal("unknown", triage.Groups[1].LikelyCause);
        Assert.Single(triage.Unknowns);
        Assert.Throws<FormatException>(() => TriagePrompt.Parse("{\"groups\":[]}", ids));
    }

    [Fact]
    public void Model_json_with_bad_escapes_raw_newlines_or_trailing_commas_is_repaired()
    {
        var ids = new HashSet<string> { "A1" };
        // "\u" followed by a Turkish character (the model tried to escape and gave up), a raw line break, a trailing comma.
        var broken = "Yanıt:\n{\"summary\":\"Fatura \\uşleminde\nhata\",\"cause\":\"source_error\",\"findings\":[{\"text\":\"E-fatura 503 \\x\",\"evidence\":[\"A1\"]},],\"checks\":[\"Servisi kontrol edin\",],\"unknowns\":[]}";
        var insight = InsightPrompt.Parse(broken, ids);
        Assert.Equal("Fatura \\uşleminde\nhata", insight.Summary);
        Assert.Equal("source_error", insight.Cause);
        Assert.Single(insight.Findings); Assert.True(insight.Findings[0].Grounded);
        Assert.Equal(["Servisi kontrol edin"], insight.Checks);

        // Valid escapes are left alone and structural damage still fails loudly.
        Assert.Equal("Türkçe \"tırnak\" ve \\ ters bölü", InsightPrompt.Parse("{\"summary\":\"T\\u00fcrk\\u00e7e \\\"tırnak\\\" ve \\\\ ters bölü\",\"cause\":\"delay\"}", ids).Summary);
        Assert.ThrowsAny<Exception>(() => InsightPrompt.Parse("{\"summary\":\"eksik", ids));
    }

    [Fact]
    public void Likely_cause_is_kept_only_with_memory_or_source_evidence()
    {
        var ids = new HashSet<string> { "A1", "M1", "H1", "C1" };
        var grounded = InsightPrompt.Parse("""{"summary":"s","cause":"delay","likely_cause":"source_outage","likely_cause_evidence":["H1","A1","Z9"]}""", ids);
        Assert.Equal("source_outage", grounded.LikelyCause);
        Assert.Equal(["H1"], grounded.LikelyCauseEvidence);

        var ungrounded = InsightPrompt.Parse("""{"summary":"s","cause":"delay","likely_cause":"threshold","likely_cause_evidence":["A1"]}""", ids);
        Assert.Equal("unknown", ungrounded.LikelyCause);
        Assert.Empty(ungrounded.LikelyCauseEvidence!);

        var unknownClass = InsightPrompt.Parse("""{"summary":"s","cause":"delay","likely_cause":"gremlins","likely_cause_evidence":["M1"]}""", ids);
        Assert.Equal("unknown", unknownClass.LikelyCause);
        Assert.Equal("unknown", InsightPrompt.Parse("""{"summary":"s","cause":"delay"}""", ids).LikelyCause);
    }

    [Fact]
    public void Unknown_cause_falls_back_and_lists_are_bounded()
    {
        var checks = string.Join(",", Enumerable.Range(0, 20).Select(i => $"\"c{i}\""));
        var insight = InsightPrompt.Parse($$"""{"summary":"x","cause":"network","checks":[{{checks}}]}""", Ids);
        Assert.Equal("unknown", insight.Cause);
        Assert.Equal(8, insight.Checks.Count);
        Assert.Empty(insight.Findings);
    }

    [Fact]
    public void Healthy_transaction_may_report_no_problem() =>
        Assert.Equal("none", InsightPrompt.Parse("""{"summary":"Adımlar zamanında bildirildi.","cause":"none"}""", Ids).Cause);

    [Theory]
    [InlineData("yanıt yok")]
    [InlineData("{\"cause\":\"delay\"}")]
    [InlineData("{\"summary\":")]
    public void Rejects_output_without_a_summary(string text) =>
        Assert.ThrowsAny<Exception>(() => InsightPrompt.Parse(text, Ids));

    // --- Gemini adapter: request contract and response validation, checked offline (no network) ---

    [Fact]
    public void Gemini_request_uses_json_mode_single_candidate_and_minimal_thinking()
    {
        var body = GeminiAiClient.BuildRequest(new AiPrompt("sistem", "{\"evidence\":[]}", 800));
        Assert.Equal("sistem", body["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("user", body["contents"]![0]!["role"]!.GetValue<string>());
        var config = body["generationConfig"]!;
        Assert.Equal("application/json", config["responseMimeType"]!.GetValue<string>());
        Assert.Equal(1, config["candidateCount"]!.GetValue<int>());
        Assert.Equal(800, config["maxOutputTokens"]!.GetValue<int>());
        Assert.Equal("MINIMAL", config["thinkingConfig"]!["thinkingLevel"]!.GetValue<string>());
    }

    [Fact]
    public void Gemini_valid_response_yields_text_and_token_counts_without_thoughts()
    {
        var completion = GeminiAiClient.ParseResponse("""
            {"candidates":[{"content":{"parts":[{"thought":true,"text":"gizli düşünce"},{"text":"{\"summary\":\"a\","},{"text":"\"cause\":\"delay\"}"}]},"finishReason":"STOP"}],
             "usageMetadata":{"promptTokenCount":120,"candidatesTokenCount":40,"thoughtsTokenCount":10,"totalTokenCount":170}}
            """);
        Assert.Equal("""{"summary":"a","cause":"delay"}""", completion.Text);
        Assert.DoesNotContain("gizli", completion.Text);
        Assert.Equal(120, completion.InputTokens);
        Assert.Equal(50, completion.OutputTokens);
    }

    [Theory]
    [InlineData("""{"promptFeedback":{"blockReason":"SAFETY"}}""", "blocked")]
    [InlineData("""{"candidates":[]}""", "no_candidate")]
    [InlineData("""{"candidates":[{"content":{"parts":[{"text":"{"}]},"finishReason":"MAX_TOKENS"}]}""", "incomplete")]
    [InlineData("""{"candidates":[{"content":{"parts":[{"text":"x"}]}}]}""", "incomplete")]
    [InlineData("""{"candidates":[{"content":{"parts":[{"inlineData":{}}]},"finishReason":"STOP"}]}""", "shape")]
    [InlineData("""{"candidates":[{"content":{"parts":[{"thought":true,"text":"t"}]},"finishReason":"STOP"}]}""", "empty")]
    [InlineData("""[]""", "shape")]
    public void Gemini_rejects_blocked_incomplete_or_malformed_responses(string json, string code) =>
        Assert.Equal(code, Assert.Throws<AiProviderException>(() => GeminiAiClient.ParseResponse(json)).Code);

    [Fact]
    public async Task Gemini_oversized_request_is_not_sent()
    {
        using var client = new GeminiAiClient("test-key", GeminiAiClient.DefaultModel, maxRequestBytes: 200, TimeSpan.FromSeconds(5), new NoNetworkHandler());
        var ex = await Assert.ThrowsAsync<AiProviderException>(() => client.CompleteAsync(new AiPrompt(new string('s', 300), "u", 10), CancellationToken.None));
        Assert.Equal("request_too_large", ex.Code);
    }

    [Fact]
    public async Task Gemini_sends_key_in_header_and_maps_quota_errors_without_retry()
    {
        var handler = new NoNetworkHandler { Status = System.Net.HttpStatusCode.TooManyRequests, Body = """{"error":{"code":429,"message":"RESOURCE_EXHAUSTED secret"}}""" };
        using var client = new GeminiAiClient("test-key", GeminiAiClient.DefaultModel, 12_000, TimeSpan.FromSeconds(5), handler);
        var ex = await Assert.ThrowsAsync<AiProviderException>(() => client.CompleteAsync(new AiPrompt("s", "u", 10), CancellationToken.None));
        Assert.Equal("http_429", ex.Code);
        Assert.DoesNotContain("secret", ex.Message);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("test-key", handler.LastKeyHeader);
        Assert.DoesNotContain("key=", handler.LastUri);
        Assert.EndsWith("/models/gemini-3.5-flash-lite:generateContent", handler.LastUri);
    }

    [Fact]
    public async Task Gemini_does_not_follow_redirects()
    {
        var handler = new NoNetworkHandler { Status = System.Net.HttpStatusCode.Found };
        using var client = new GeminiAiClient("k", GeminiAiClient.DefaultModel, 12_000, TimeSpan.FromSeconds(5), handler);
        var ex = await Assert.ThrowsAsync<AiProviderException>(() => client.CompleteAsync(new AiPrompt("s", "u", 10), CancellationToken.None));
        Assert.Equal("redirect", ex.Code);
    }

    private static IAiModelClient Configure(params (string, string?)[] settings)
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(settings.GroupBy(s => s.Item1).Select(g => new KeyValuePair<string, string?>(g.Key, g.Last().Item2))).Build(); // later values override
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddAi(configuration);
        return Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IAiModelClient>(
            Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services));
    }

    [Fact]
    public void Gemini_key_falls_back_to_environment_variable_when_Ai_ApiKey_is_empty()
    {
        var client = Configure(("Ai:Provider", "gemini"), ("Ai:ApiKey", ""), ("GEMINI_API_KEY", "test-key"), ("Ai:FreeTierConfirmed", "true"));
        Assert.True(client.Enabled);
        Assert.Equal("gemini", client.Provider);
        Assert.Equal(GeminiAiClient.DefaultModel, client.Model);
    }

    [Theory]
    [InlineData("anahtar", "Ai:ApiKey", "", "GEMINI_API_KEY", "")]
    [InlineData("Free Tier", "GEMINI_API_KEY", "k", "Ai:FreeTierConfirmed", "false")]
    [InlineData("modeller", "GEMINI_API_KEY", "k", "Ai:Model", "gemini-3.5-pro")]
    [InlineData("production", "GEMINI_API_KEY", "k", "Telemetry:Environment", "production")]
    public void Gemini_stays_disabled_with_a_reason_when_a_gate_is_not_met(string reasonPart, string k1, string v1, string k2, string v2)
    {
        var client = Configure(("Ai:Provider", "gemini"), ("Ai:FreeTierConfirmed", "true"), ("Ai:TestMode", "true"), (k1, v1), (k2, v2));
        Assert.False(client.Enabled);
        Assert.Equal("gemini", client.Provider);
        Assert.Contains(reasonPart, client.Reason);
    }

    [Fact]
    public void Ai_Enabled_false_keeps_a_configured_provider_off()
    {
        var client = Configure(("Ai:Provider", "fake"), ("Ai:Enabled", "false"));
        Assert.False(client.Enabled);
        Assert.Contains("Ai__Enabled", client.Reason);
    }

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        public System.Net.HttpStatusCode Status { get; init; } = System.Net.HttpStatusCode.OK;
        public string Body { get; init; } = "{}";
        public int Calls { get; private set; }
        public string? LastKeyHeader { get; private set; }
        public string LastUri { get; private set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; LastUri = request.RequestUri!.ToString();
            LastKeyHeader = request.Headers.TryGetValues("x-goog-api-key", out var v) ? string.Join(",", v) : null;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body, System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
