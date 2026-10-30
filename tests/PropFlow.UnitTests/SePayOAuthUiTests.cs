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

public sealed class SePayOAuthUiTests
{
    [Fact]
    public async Task UnconfiguredApplicationShowsGuidanceWithoutKeyInput()
    {
        var html = await Render(new(false, false, false, null, false, "Vui lòng liên hệ đơn vị vận hành PropFlow."));
        Assert.Contains("Kết nối SePay", html);
        Assert.Contains("liên hệ đơn vị vận hành", html);
        Assert.Contains("disabled", html);
        Assert.DoesNotContain("<input", html);
    }
    [Fact]
    public async Task ConnectedOwnerIsPromptedToSelectBankAndProvisionNotifications()
    {
        var html = await Render(new(true, true, false, DateTimeOffset.UtcNow, false, "Đã kết nối"), true);
        Assert.Contains("Kết nối lại SePay", html);
        Assert.Contains("Thiết lập nhận giao dịch", html);
        Assert.Contains("Mỗi thay đổi đều cần OTP email", html);
        Assert.Contains("https://my.sepay.vn", html);
        Assert.DoesNotContain("<input", html);
    }

    [Fact]
    public async Task RateLimitedInitialLoadOffersRetryForLiveConnection()
    {
        var html = await Render(new(false, false, false, null, false, ""), status: HttpStatusCode.TooManyRequests);
        Assert.Contains("Thử tải lại trạng thái SePay", html);
        Assert.Contains("role=\"alert\"", html);
    }

    private static async Task<string> Render(SePayOAuthState state, bool hasDestination = false, HttpStatusCode status = HttpStatusCode.OK)
    {
        using var http = new HttpClient(new Handler(state, status)) { BaseAddress = new("https://api.example.com/") };
        using var services = new ServiceCollection().AddLogging().AddSingleton<NavigationManager>(new Navigation())
            .AddSingleton(new AuthenticatedApiClient(http, Microsoft.Extensions.Logging.Abstractions.NullLogger<ApiClient>.Instance)).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () => WebUtility.HtmlDecode((await renderer.RenderComponentAsync<SePayOAuthConnect>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["HasDestination"] = hasDestination }))).ToHtmlString()));
    }
    private sealed class Handler(SePayOAuthState state, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = status == HttpStatusCode.OK ? JsonContent.Create(state)
                : JsonContent.Create(new { title = "Bạn gửi yêu cầu quá nhanh. Vui lòng thử lại sau.", code = "rate_limited" }) });
    }
    private sealed class Navigation : NavigationManager
    {
        public Navigation() => Initialize("https://app.example.com/", "https://app.example.com/admin/dashboard?tab=payment");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Rendering must not navigate.");
    }
}
