using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using PropFlow.Modules.Payments.Infrastructure.PaymentSettings;
using PropFlow.Modules.Payments.Infrastructure.ResidentPayments;

namespace PropFlow.UnitTests;

public sealed class SePayConnectionTests
{
    [Fact]
    public async Task MissingTokenDoesNotContactSePay()
    {
        var handler = new FakeHandler(_ => throw new InvalidOperationException("Must not call provider"));
        using var http = new HttpClient(handler);
        var probe = new SePayConnectionProbe(http, Options.Create(new SePayOptions()));
        var result = await probe.CheckAsync(default);
        Assert.False(result.Connected);
        Assert.Equal("sepay_token_missing", result.Code);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task SuccessfulCheckUsesOfficialApiAndNeverReturnsTokenOrAccountData()
    {
        const string secret = "private-sepay-api-token";
        using var http = new HttpClient(new FakeHandler(request =>
        {
            Assert.Equal("https://userapi.sepay.vn/v2/bank-accounts?page=1&per_page=1", request.RequestUri!.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal(secret, request.Headers.Authorization.Parameter);
            return Json(HttpStatusCode.OK, """{"status":"success","data":[{"account_number":"123456789","accumulated":2000000}]}""");
        }));
        var options = Options.Create(new SePayOptions { ApiToken = secret });
        var settings = new SePayConnectionSettings(options, new SePayConnectionProbe(http, options));
        var result = await settings.CheckAsync(default);
        Assert.True(result.ApiCheck!.Connected);
        Assert.Equal(1, result.ApiCheck.AccountsOnFirstPage);
        var serialized = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.DoesNotContain(secret, serialized);
        Assert.DoesNotContain("123456789", serialized);
        Assert.DoesNotContain("2000000", serialized);
    }

    [Theory]
    [InlineData(401, "sepay_unauthorized")]
    [InlineData(403, "sepay_forbidden")]
    [InlineData(429, "sepay_rate_limited")]
    [InlineData(503, "sepay_unavailable")]
    [InlineData(302, "sepay_unavailable")]
    public async Task ProviderErrorsAreTranslatedWithoutLeakingResponseBody(int status, string expected)
    {
        using var http = new HttpClient(new FakeHandler(_ => Json((HttpStatusCode)status, "sensitive provider details")));
        var result = await new SePayConnectionProbe(http, Options.Create(new SePayOptions { ApiToken = "token" })).CheckAsync(default);
        Assert.False(result.Connected);
        Assert.Equal(expected, result.Code);
        Assert.DoesNotContain("sensitive", result.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{\"status\":\"error\",\"data\":[]}")]
    [InlineData("{\"status\":\"success\",\"data\":{}}")]
    public async Task InvalidResponseDoesNotReportConnected(string body)
    {
        using var http = new HttpClient(new FakeHandler(_ => Json(HttpStatusCode.OK, body)));
        var result = await new SePayConnectionProbe(http, Options.Create(new SePayOptions { ApiToken = "token" })).CheckAsync(default);
        Assert.False(result.Connected);
        Assert.Equal("sepay_invalid_response", result.Code);
    }

    [Fact]
    public async Task CancellationIsPropagatedAndTimeoutIsReported()
    {
        using var http = new HttpClient(new FakeHandler(_ => throw new TaskCanceledException()));
        var probe = new SePayConnectionProbe(http, Options.Create(new SePayOptions { ApiToken = "token" }));
        Assert.Equal("sepay_timeout", (await probe.CheckAsync(default)).Code);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe.CheckAsync(cancellation.Token));
    }

    [Fact]
    public void WebhookReadinessDoesNotImplyBankConnectionOrPublicReachability()
    {
        var config = new SePayOptions { Enabled = true, ApiToken = "token", WebhookApiKey = new string('k', 40),
            Gateway = "Vietcombank", BankCode = "VCB", WebhookPublicBaseUrl = "https://payments.example.com" };
        using var http = new HttpClient(new FakeHandler(_ => throw new InvalidOperationException()));
        var options = Options.Create(config);
        var settings = new SePayConnectionSettings(options, new SePayConnectionProbe(http, options));
        Assert.True(settings.Current().WebhookConfigurationReady);
        Assert.Null(settings.Current().ApiCheck);
        Assert.Equal("https://payments.example.com/api/v1/payments/webhooks/sepay", settings.Current().PublicWebhookUrl);
        config.WebhookPublicBaseUrl = "http://localhost:7101";
        Assert.False(settings.Current().WebhookConfigurationReady);
        config.WebhookPublicBaseUrl = "https://user:password@example.com";
        Assert.Null(settings.Current().PublicWebhookUrl);
        config.WebhookPublicBaseUrl = "https://payments.example.com";
        config.Enabled = false;
        Assert.False(settings.Current().WebhookConfigurationReady);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; ct.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
    }
}
