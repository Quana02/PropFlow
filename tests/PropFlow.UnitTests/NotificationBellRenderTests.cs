using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Web.Client.Features.Communication.Components;
using PropFlow.Web.Client.Features.Finance.Services;
using PropFlow.Web.Client.Features.Resident.Portal.Services;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.UnitTests;

public sealed class NotificationBellRenderTests
{
    [Fact]
    public async Task Badge_uses_the_server_total_and_refreshes_after_a_successful_read()
    {
        using var transport = new NoticeTransport();
        using var http = new HttpClient(transport) { BaseAddress = new Uri("https://example.invalid/") };
        var state = new ResidentFinanceNoticeState(new FinanceCycleApi(new AuthenticatedApiClient(http, NullLogger<ApiClient>.Instance)));
        var navigation = new TestNavigation();
        await using var services = new ServiceCollection().AddLogging().AddSingleton(state)
            .AddSingleton<NavigationManager>(navigation)
            .AddSingleton(new ResidentNotificationConnectionFactory(new ConfigurationBuilder().Build(), navigation, null!))
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await state.ReloadAsync();
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<NotificationBell>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(NotificationBell.Connect)] = false })));
        Assert.Contains("99+", WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString)));

        Assert.True(await renderer.Dispatcher.InvokeAsync(() => state.ReadAsync(transport.NoticeId)));
        Assert.Equal(99, state.UnreadCount);
        Assert.True(Assert.Single(state.Items).IsRead);
        var afterRead = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        Assert.DoesNotContain("99+", afterRead);
        Assert.Contains("99 chưa đọc", afterRead);

        await renderer.Dispatcher.InvokeAsync(state.ReadAllAsync);
        Assert.Equal(0, state.UnreadCount);
        Assert.DoesNotContain("notification-badge", await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
    }

    [Theory]
    [InlineData("LIVING_ROOM", "Phòng sinh hoạt chung")]
    [InlineData("NOT_A_REAL_ROOM", "Chưa xác định khu vực")]
    public void Request_area_text_has_a_human_readable_fallback(string code, string expected) =>
        Assert.Equal(expected, PropFlow.Web.Client.Features.ServiceRequests.Models.ServiceRequestAreaText.Label(code));

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("https://example.invalid/", "https://example.invalid/");
    }

    private sealed class NoticeTransport : HttpMessageHandler
    {
        public Guid NoticeId { get; } = Guid.NewGuid();
        private int count = 100;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/read", StringComparison.Ordinal)) count = 99;
            if (path.EndsWith("/read-all", StringComparison.Ordinal)) count = 0;
            object body = path.EndsWith("/unread-count", StringComparison.Ordinal) ? new { count } :
                request.Method == HttpMethod.Post ? new { read = true } :
                new[] { new ResidentNotice(NoticeId, "Yêu cầu mới", "Có việc mới", "/manager/service-requests", count < 100, DateTimeOffset.UtcNow) };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
        }
    }
}
