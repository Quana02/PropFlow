using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using PropFlow.Web.Client.Features.Complaints.Components;
using PropFlow.Web.Client.Features.Complaints.Services;
using PropFlow.Web.Client.Features.Communication.Components;
using PropFlow.Web.Client.Features.Communication.Services;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.IntegrationTests;

public sealed class ComplaintAnnouncementUiTests
{
    private sealed class NoJs : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken ct, object?[]? args) => ValueTask.FromResult(default(T)!);
    }
    private sealed class Navigation : NavigationManager
    {
        public Navigation() => Initialize("https://example.invalid/", "https://example.invalid/resident/complaints");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
    private sealed class Transport(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond());
    }
    private sealed class DeferredTransport(Task<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => response.WaitAsync(ct);
    }
    private static ServiceProvider Services(Func<HttpResponseMessage> response)
        => Services(new Transport(response));
    private static ServiceProvider Services(HttpMessageHandler transport)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddSingleton(new ApiClient(new HttpClient(transport) { BaseAddress = new Uri("https://example.invalid/") }, NullLogger<ApiClient>.Instance));
        services.AddSingleton<PropFlow.Web.Client.Services.Authentication.AuthSession>();
        services.AddSingleton<AnnouncementRealtimeClient>();
        services.AddSingleton<IJSRuntime, NoJs>();
        services.AddSingleton<NavigationManager, Navigation>();
        services.AddSingleton(new AuthenticatedApiClient(new HttpClient(transport) { BaseAddress = new Uri("https://example.invalid/") }, NullLogger<ApiClient>.Instance));
        services.AddSingleton<ComplaintApiClient>(); services.AddSingleton<AnnouncementApiClient>();
        return services.BuildServiceProvider();
    }
    private static string Text(string value) => value;

    [Fact]
    public async Task Loading_disables_forms_until_the_api_returns()
    {
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var services = Services(new DeferredTransport(response.Task));
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.BeginRenderingComponent<ComplaintWorkspace>());
        var loading = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.Contains("Đang tải dữ liệu", loading);
        Assert.Contains("<fieldset disabled", loading);
        response.SetResult(new(HttpStatusCode.OK) { Content = JsonContent.Create(new { items = Array.Empty<object>(), total = 0, page = 1, pageSize = 20 }) });
        await root.QuiescenceTask;
        var loaded = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.DoesNotContain("<fieldset disabled", loaded);
        Assert.Contains("Chưa có phản ánh phù hợp", loaded);
    }

    [Theory, InlineData("RESIDENT", true), InlineData("MANAGER", false), InlineData("STAFF", false)]
    public async Task Complaint_empty_state_only_offers_submission_to_resident(string role, bool canSubmit)
    {
        await using var services = Services(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { items = Array.Empty<object>(), total = 0, page = 1, pageSize = 20 }) });
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<ComplaintWorkspace>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Role"] = role }))).ToHtmlString());
        Assert.Contains(Text("Chưa có phản ánh phù hợp"), html);
        Assert.Equal(canSubmit, WebUtility.HtmlDecode(html).Contains(Text("Gửi phản ánh mới")));
        Assert.Contains("maxlength=\"200\"", html);
    }

    [Fact]
    public async Task Complaint_api_failure_is_visible_and_can_be_retried()
    {
        await using var services = Services(() => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = JsonContent.Create(new { title = "Không có quyền", detail = "Chưa xác minh cư trú", status = 403, code = "forbidden" }) });
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<ComplaintWorkspace>()).ToHtmlString());
        Assert.Contains("role=\"alert\"", html);
        Assert.Contains(Text("Chưa xác minh cư trú"), WebUtility.HtmlDecode(html));
        Assert.Contains(Text("Tải lại"), html);
    }

    [Theory, InlineData(true), InlineData(false)]
    public async Task Announcement_shows_full_content_safely_and_only_manager_can_compose(bool manager)
    {
        await using var services = Services(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                items = new[] { new { id = Guid.NewGuid(), title = "Bảo trì", content = "Dòng đầu\n<script>alert(1)</script>", publishedAt = DateTimeOffset.UtcNow } },
                total = 1,
                page = 1,
                pageSize = 20,
                timeZoneId = "Asia/Ho_Chi_Minh"
            })
        });
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<AnnouncementWorkspace>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Manager"] = manager }))).ToHtmlString());
        Assert.DoesNotContain("<form", html);
        Assert.Equal(manager, html.Contains(Text("Đăng thông báo")));
        Assert.Contains("id=\"announcement-detail-title\"", html);
        Assert.Contains(Text("Xem chi tiết"), html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
    }
    [Fact]
    public async Task Dashboard_card_shows_published_data_and_links_to_the_exact_announcement()
    {
        var id = Guid.NewGuid();
        await using var services = Services(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { items = new[] { new { id, title = "Thông báo vừa đăng", content = "Nội dung thật", publishedAt = DateTimeOffset.UtcNow } }, total = 1, page = 1, pageSize = 20, timeZoneId = "Asia/Ho_Chi_Minh" })
        });
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<ResidentAnnouncementCard>()).ToHtmlString());
        Assert.Contains("Thông báo vừa đăng", WebUtility.HtmlDecode(html));
        Assert.Contains($"/resident?tab=notifications&amp;category=general&amp;announcement={id}", html);
        Assert.DoesNotContain("20 phút trước", WebUtility.HtmlDecode(html));
    }

    [Fact]
    public async Task Dashboard_card_explains_missing_residence_instead_of_showing_sample_notices()
    {
        await using var services = Services(() => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = JsonContent.Create(new { detail = "Chưa xác minh cư trú" })
        });
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<ResidentAnnouncementCard>()).ToHtmlString());
        Assert.Contains("liên kết và xác minh cư trú", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("Bảo trì hệ thống thang máy", WebUtility.HtmlDecode(html));
    }}

