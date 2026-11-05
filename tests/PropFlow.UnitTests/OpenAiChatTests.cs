using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PropFlow.Modules.AiChatbot.Application;
using PropFlow.Modules.AiChatbot.Infrastructure;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-12")]
public sealed class OpenAiChatTests
{
    [Fact]
    public async Task ResponsesRequestIsStatelessStructuredAndRedactsDocumentContacts()
    {
        var handler = new Handler(JsonSerializer.Serialize(new {
            status = "completed", usage = new { total_tokens = 100 },
            output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = "{\"supported\":true,\"answer\":\"Theo nguồn [1].\",\"citations\":[1]}" } } } }
        }));
        using var provider = Provider(handler);
        var answer = await provider.AnswerAsync("Nội quy", [new(Guid.NewGuid(), 1, 1, "Hướng dẫn", "Gửi test@example.com, 0901234567")], default);
        Assert.True(answer!.Supported); Assert.Equal(new[] { 1 }, answer.Citations);
        using var request = JsonDocument.Parse(handler.Body);
        Assert.False(request.RootElement.GetProperty("store").GetBoolean());
        Assert.Equal("json_schema", request.RootElement.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
        Assert.DoesNotContain("test@example.com", handler.Body); Assert.DoesNotContain("0901234567", handler.Body);
        Assert.DoesNotContain("previous_response_id", handler.Body); Assert.DoesNotContain("tools", handler.Body);
        Assert.EndsWith("/responses", handler.Url);
    }
    [Fact]
    public async Task EmbeddingsAreBoundToConfiguredDimensionsAndValidated()
    {
        var vector = Enumerable.Repeat(0.1f, 64).ToArray();
        var handler = new Handler(JsonSerializer.Serialize(new { data = new[] { new { index = 0, embedding = vector } } }));
        using var provider = Provider(handler);
        Assert.Single(await provider.EmbedAsync(["Nội dung"], default));
        Assert.Contains("\"dimensions\":64", handler.Body); Assert.EndsWith("/embeddings", handler.Url);
        handler.BodyResponse = "{\"data\":[{\"index\":0,\"embedding\":[0]}]}";
        await Assert.ThrowsAsync<ChatAiUnavailableException>(() => provider.EmbedAsync(["Nội dung"], default));
    }
    [Fact]
    public async Task ProviderErrorsAndExhaustedBudgetDoNotLeakExternalPayload()
    {
        var handler = new Handler("private provider error") { Status = HttpStatusCode.Unauthorized };
        using var provider = Provider(handler, 1);
        await Assert.ThrowsAsync<ChatAiUnavailableException>(() => provider.EmbedAsync(["Nội dung"], default));
        handler.Status = HttpStatusCode.OK;
        await Assert.ThrowsAsync<ChatAiUnavailableException>(() => provider.EmbedAsync(["Nội dung"], default));
        Assert.Equal(1, handler.Calls);
    }
    [Theory]
    [InlineData("credit_balance_exhausted", "credit_balance_exhausted")]
    [InlineData("sensitive-unrecognized-code", "other")]
    public async Task DependencyLogsOnlyAllowListedErrorCodeAndNeverProviderMessage(string code, string expected)
    {
        var logger = new CaptureLogger();
        var handler = new Handler(JsonSerializer.Serialize(new { error = new { code, message = "private-provider-message", type = "insufficient_quota" } })) { Status = HttpStatusCode.TooManyRequests };
        using var provider = Provider(handler, logger: logger);
        await Assert.ThrowsAsync<ChatAiUnavailableException>(() => provider.EmbedAsync(["Không được ghi vào log"], default));
        Assert.Contains("code " + expected, string.Join(" ", logger.Messages));
        Assert.DoesNotContain("private-provider-message", string.Join(" ", logger.Messages));
        Assert.DoesNotContain("sensitive-unrecognized-code", string.Join(" ", logger.Messages));
        Assert.DoesNotContain("Không được ghi vào log", string.Join(" ", logger.Messages));
    }
    [Theory]
    [InlineData("[]")][InlineData("null")][InlineData("not json")]
    public async Task MalformedProviderErrorsStillUseSafeUnavailableFallback(string body)
    {
        using var provider = Provider(new Handler(body) { Status = HttpStatusCode.ServiceUnavailable });
        await Assert.ThrowsAsync<ChatAiUnavailableException>(() => provider.EmbedAsync(["Nguồn kiểm thử"], default));
    }
    private static OpenAiChat Provider(Handler handler, int budget = 60, ILogger<OpenAiChat>? logger = null) => new(new Factory(handler), Options.Create(new ChatAiOptions {
        Enabled = true, ApiKey = "test-key-not-a-real-secret", EmbeddingDimensions = 64, RequestsPerHour = budget
    }), logger ?? NullLogger<OpenAiChat>.Instance);
    private sealed class CaptureLogger : ILogger<OpenAiChat>
    {
        public List<string> Messages = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
    private sealed class Factory(Handler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, false); }
    private sealed class Handler(string response) : HttpMessageHandler
    {
        public string BodyResponse = response, Body = "", Url = "";
        public HttpStatusCode Status = HttpStatusCode.OK;
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; Body = await request.Content!.ReadAsStringAsync(ct); Url = request.RequestUri!.ToString(); return new(Status) { Content = new StringContent(BodyResponse, Encoding.UTF8, "application/json") }; }
    }
}
