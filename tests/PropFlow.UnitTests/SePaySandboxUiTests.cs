using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropFlow.Web.Client.Features.Administration.Components;
using PropFlow.Web.Client.Features.Administration.Models;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.UnitTests;

public sealed class SePaySandboxUiTests
{
    [Fact]
    public async Task SetupExplainsTestModeUsesPasswordFieldAndOtpAndDoesNotRequestGatewayCredentials()
    {
        var html = await Render(new(false, false, 0, 0, null, null));
        Assert.Contains("SANDBOX", html); Assert.Contains("Test mode", html); Assert.Contains("API Access", html);
        Assert.Contains("type=\"password\"", html); Assert.Contains("Lưu bằng OTP email", html);
        Assert.DoesNotContain("Merchant ID", html); Assert.DoesNotContain("Secret Key", html);
        Assert.DoesNotContain("Ngắt kết nối Sandbox", html);
    }
    [Fact]
    public async Task SavedConnectionProvidesCheckSyncAndOtpDisconnectWithoutReturningToken()
    {
        var html = await Render(new(true, true, 1, 2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        Assert.Contains("Đã kiểm tra kết nối", html);
        Assert.Contains("Đồng bộ tài khoản thử nghiệm", html); Assert.Contains("Ngắt kết nối Sandbox", html);
        Assert.Contains("••••••••••••", html);
        Assert.Contains("Bật mô phỏng FE-05 bằng OTP", html);
    }

    [Fact]
    public async Task RateLimitedInitialLoadShowsRetryInsteadOfPermanentlyLockingForm()
    {
        var html = await Render(new(false, false, 0, 0, null, null), HttpStatusCode.TooManyRequests);
        Assert.Contains("Thử tải lại trạng thái Sandbox", html);
        Assert.Contains("role=\"alert\"", html);
        Assert.Contains("disabled", html);
    }

    private static async Task<string> Render(SePaySandboxState state, HttpStatusCode status = HttpStatusCode.OK)
    {
        using var http = new HttpClient(new Handler(state, status)) { BaseAddress = new("https://api.example.com/") };
        using var services = new ServiceCollection().AddLogging()
            .AddSingleton(new AuthenticatedApiClient(http, Microsoft.Extensions.Logging.Abstractions.NullLogger<ApiClient>.Instance)).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () => WebUtility.HtmlDecode((await renderer.RenderComponentAsync<SePaySandboxConnect>()).ToHtmlString()));
    }
    private sealed class Handler(SePaySandboxState state, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = status == HttpStatusCode.OK ? JsonContent.Create(state)
                : JsonContent.Create(new { title = "Bạn gửi yêu cầu quá nhanh. Vui lòng thử lại sau.", code = "rate_limited" }) });
    }
}
