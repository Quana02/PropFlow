using System.Net;
using System.Text;
using System.Text.Json;
using PropFlow.Modules.AiRecommendation.Application.FacilityDiscovery;
using PropFlow.Modules.AiRecommendation.Infrastructure.FacilityDiscovery;
using PropFlow.Shared;

namespace PropFlow.UnitTests;

public sealed class FacilityProviderPrivacyTests
{
    [Fact]
    public async Task Actual_facility_providers_fail_safely_on_timeout_http_failure_and_malformed_response()
    {
        foreach(var mode in new[]{"timeout","http-failure","malformed"})
        {
            using var handler=new Failure(mode);using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
            var advisor=new GeminiFacilityNeedAdvisor(new GeminiStructuredClient(http,"fixture-key","test-model",true));
            var speech=new GeminiFacilitySpeech(http,"fixture-key","test-model",true);
            Assert.Null(await advisor.RecommendAsync("Đọc sách",[new("0","Thư viện",null,"Đọc sách","GENERAL",null,null,"GUARDIAN_REQUIRED")],default));
            Assert.Null(await speech.GenerateAsync("Phản hồi công khai từ máy chủ.",default));
            Assert.Equal(2,handler.Calls);
        }
    }
    private sealed class Failure(string mode):HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Calls++;
            if(mode=="timeout")throw new TaskCanceledException("Synthetic provider timeout");
            return Task.FromResult(new HttpResponseMessage(mode=="http-failure"?HttpStatusCode.ServiceUnavailable:HttpStatusCode.OK)
            {Content=new StringContent("{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"inlineData\":{\"mimeType\":\"audio/mp3\",\"data\":\"invalid\"}}]}}]}",Encoding.UTF8,"application/json")});
        }
    }
    [Fact]
    public async Task Actual_Gemini_facility_transport_redacts_credentials_in_need_and_public_context()
    {
        using var handler = new Capture(); using var http = new HttpClient(handler) { BaseAddress = new("https://fixture.invalid/") };
        var advisor = new GeminiFacilityNeedAdvisor(new GeminiStructuredClient(http, "fixture-configured-key", "test-model", true));
        const string secrets = "canary@example.invalid 0912345678 012345678901 jwt=eyJjYW5hcnkiOiJ4In0.cGF5bG9hZA.c2ln API key=FACILITY_KEY_CANARY password=FACILITY_PASS_CANARY OTP=564321";
        var input = new FacilityAdvisorInput("0", FacilityNeedMatcher.Redact("Vườn " + secrets), null,
            FacilityNeedMatcher.Redact("Thư giãn " + secrets), "GENERAL", null, null, "GUARDIAN_REQUIRED");
        Assert.NotNull(await advisor.RecommendAsync("Thư giãn một mình hôm nay " + secrets, [input], default));
        foreach (var secret in new[] { "canary@example.invalid", "0912345678", "012345678901", "eyJjYW5hcnkiOiJ4In0", "FACILITY_KEY_CANARY", "FACILITY_PASS_CANARY", "564321" })
            Assert.False(handler.Body.Contains(secret, StringComparison.Ordinal), "A synthetic sensitive canary reached the provider request.");
    }
    private sealed class Capture : HttpMessageHandler
    {
        public string Body = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { candidates = new[] {
                new { finishReason = "STOP", content = new { parts = new[] { new { text = "{\"keys\":[\"0\"],\"forChildren\":false,\"needsSupervision\":false,\"clarification\":\"NONE\"}" } } } }
            } }), Encoding.UTF8, "application/json") };
        }
    }
}
