using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Web.Client.Features.Communication.Components;
using PropFlow.Web.Client.Features.Communication.Services;
using PropFlow.Web.Client.Features.Finance.Services;
using PropFlow.Web.Client.Features.Resident.Portal.Services;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Services.Authentication;

namespace PropFlow.UnitTests;

public sealed class NotificationBellRenderTests
{
    [Fact]
    public async Task Badge_receives_a_notification_after_the_initial_hub_connection_fails()
    {
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = BuildHub("/hubs/notifications", connected);
        await app.StartAsync();

        using var transport = new NoticeTransport(0);
        using var http = new HttpClient(transport) { BaseAddress = new Uri("https://example.invalid/") };
        var api = new AuthenticatedApiClient(http, NullLogger<ApiClient>.Instance);
        var state = new ResidentFinanceNoticeState(new FinanceCycleApi(api));
        var session = Session(api, "STAFF");
        var navigation = new TestNavigation();
        var configuration = HubConfiguration(app);
        var bell = new InteractiveBell();
        await using var services = new ServiceCollection().AddLogging().AddSingleton(state)
            .AddSingleton<NavigationManager>(navigation)
            .AddSingleton(new ResidentNotificationConnectionFactory(configuration, navigation, session))
            .AddSingleton<IComponentActivator>(new BellActivator(bell)).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<InteractiveBell>());
        Assert.DoesNotContain("notification-badge", await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        await renderer.Dispatcher.InvokeAsync(bell.StartInteractiveAsync);
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(6));

        var notice = new ResidentNotice(Guid.NewGuid(), "Công việc mới", "Có việc mới", "/staff/service-requests", false, DateTimeOffset.UtcNow);
        transport.Publish(notice);
        await app.Services.GetRequiredService<IHubContext<NotificationTestHub>>().Clients.All.SendAsync("FinanceNotificationCreated", notice);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString)).Contains("notification-badge"))
            await Task.Delay(30, timeout.Token);
        Assert.Contains("1 chưa đọc", WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString)));
    }

    [Fact]
    public async Task Announcements_reconcile_immediately_when_a_failed_first_connection_recovers()
    {
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = BuildHub("/hubs/announcements", connected);
        await app.StartAsync();
        using var http = new HttpClient();
        var session = Session(new ApiClient(http, NullLogger<ApiClient>.Instance), "RESIDENT");
        await using var realtime = new AnnouncementRealtimeClient(HubConfiguration(app), new TestNavigation(), session,
            NullLogger<AnnouncementRealtimeClient>.Instance);
        var refreshed = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        realtime.Changed += id => { refreshed.TrySetResult(id); return Task.CompletedTask; };

        await realtime.EnsureStartedAsync();
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(6));
        Assert.Equal(Guid.Empty, await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(3)));
    }

    private static WebApplication BuildHub(string hubPath, TaskCompletionSource connected)
    {
        var failedNegotiation = 0;
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSignalR();
        builder.Services.AddSingleton(connected);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path == hubPath + "/negotiate" && Interlocked.Increment(ref failedNegotiation) == 1)
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            else
                await next(context);
        });
        app.MapHub<NotificationTestHub>(hubPath);
        return app;
    }

    private static IConfiguration HubConfiguration(WebApplication app) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Api:BaseUrl"] = app.Urls.Single() }).Build();

    private static AuthSession Session(ApiClient api, string role)
    {
        var session = new AuthSession(api);
        session.Accept(new("test-token", DateTimeOffset.UtcNow.AddMinutes(5),
            new(Guid.NewGuid(), "test", "Người kiểm thử", null, null, "ACTIVE", true, role, [])));
        return session;
    }

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

    private sealed class InteractiveBell : NotificationBell
    {
        public Task StartInteractiveAsync() => OnAfterRenderAsync(true);
    }

    private sealed class BellActivator(InteractiveBell bell) : IComponentActivator
    {
        public IComponent CreateInstance(Type componentType) => componentType == typeof(InteractiveBell)
            ? bell : (IComponent)Activator.CreateInstance(componentType)!;
    }

    public sealed class NotificationTestHub(TaskCompletionSource connected) : Hub
    {
        public override Task OnConnectedAsync()
        {
            connected.TrySetResult();
            return base.OnConnectedAsync();
        }
    }

    private sealed class NoticeTransport(int count = 100) : HttpMessageHandler
    {
        public Guid NoticeId { get; } = Guid.NewGuid();
        private ResidentNotice? published;

        public void Publish(ResidentNotice notice) { published = notice; count++; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/read", StringComparison.Ordinal)) count = 99;
            if (path.EndsWith("/read-all", StringComparison.Ordinal)) count = 0;
            object body = path.EndsWith("/unread-count", StringComparison.Ordinal) ? new { count } :
                request.Method == HttpMethod.Post ? new { read = true } :
                published is not null ? new[] { published } : count == 0 ? Array.Empty<ResidentNotice>() :
                new[] { new ResidentNotice(NoticeId, "Yêu cầu mới", "Có việc mới", "/manager/service-requests", count < 100, DateTimeOffset.UtcNow) };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
        }
    }
}
