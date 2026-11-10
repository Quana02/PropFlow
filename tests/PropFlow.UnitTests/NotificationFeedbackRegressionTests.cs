using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Web.Client.Features.ServiceRequests.Pages;
using PropFlow.Web.Client.Features.ServiceRequests.Services;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Services.Authentication;
using PropFlow.Web.Client.Shared.Feedback;

namespace PropFlow.UnitTests;

public sealed class NotificationFeedbackRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Staff_progress_keeps_server_validation_at_the_input_and_toasts_server_failure(bool serverFailure)
    {
        using var http = new HttpClient(new FailureTransport(serverFailure)) { BaseAddress = new("https://test.invalid/") };
        var api = new AuthenticatedApiClient(http, NullLogger<ApiClient>.Instance);
        var capture = new Capture();
        await using var services = new ServiceCollection().AddLogging().AddSingleton<ToastService>()
            .AddSingleton<AuthenticatedApiClient>(api).AddSingleton<ApiClient>(api)
            .AddSingleton<IConfiguration>(new ConfigurationManager()).AddSingleton<NavigationManager, Navigation>()
            .AddSingleton<AuthSession>().AddSingleton<ServiceRequestRealtimeClient>()
            .AddSingleton<IStaffServiceRequestApiClient, StaffServiceRequestApiClient>()
            .AddSingleton<IComponentActivator>(capture).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<IsolatedWork>(ParameterView.Empty);
            await (Task)typeof(StaffServiceRequestWork).GetMethod("SaveProgress", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(capture.Work, null)!;
            typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(capture.Work, null);
            var html = WebUtility.HtmlDecode(root.ToHtmlString());
            var messages = services.GetRequiredService<ToastService>().Messages;
            if (serverFailure)
            {
                Assert.Equal("error", Assert.Single(messages).Severity);
                Assert.DoesNotContain("Nội dung cần được kiểm tra.", html);
            }
            else
            {
                Assert.Empty(messages);
                Assert.Contains("id=\"staff-progress-error\"", html);
                Assert.Contains("Nội dung cần được kiểm tra.", html);
                Assert.Contains("aria-describedby=\"staff-progress-error\"", html);
            }
            Assert.Contains("Lưu tiến độ", html);
        });
    }

    [Fact]
    public async Task Closing_the_staff_progress_dialog_discards_late_api_feedback_without_clearing_new_toasts()
    {
        var transport = new DelayedFailureTransport();
        using var http = new HttpClient(transport) { BaseAddress = new("https://test.invalid/") };
        var api = new AuthenticatedApiClient(http, NullLogger<ApiClient>.Instance);
        var capture = new Capture();
        await using var services = new ServiceCollection().AddLogging().AddSingleton<ToastService>()
            .AddSingleton<AuthenticatedApiClient>(api).AddSingleton<ApiClient>(api)
            .AddSingleton<IConfiguration>(new ConfigurationManager()).AddSingleton<NavigationManager, Navigation>()
            .AddSingleton<AuthSession>().AddSingleton<ServiceRequestRealtimeClient>()
            .AddSingleton<IStaffServiceRequestApiClient, StaffServiceRequestApiClient>()
            .AddSingleton<IComponentActivator>(capture).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<IsolatedWork>(ParameterView.Empty);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var work = (Task)typeof(StaffServiceRequestWork).GetMethod("SaveProgress", flags)!.Invoke(capture.Work, null)!;
            await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            typeof(StaffServiceRequestWork).GetMethod("CancelProgressFeedbackCloseProgressDialog", flags)!.Invoke(capture.Work, null);
            var toasts = services.GetRequiredService<ToastService>(); toasts.Info("Thông báo mới");
            transport.Response.SetResult(new(HttpStatusCode.InternalServerError) { Content = JsonContent.Create(new { title = "Lỗi response cũ" }) });
            await work;
            Assert.Equal("Thông báo mới", Assert.Single(toasts.Messages).Message);
        });
    }
    private sealed class DelayedFailureTransport : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Started.TrySetResult(); return Response.Task; }
    }

    // Isolate the write form from initial GET/realtime startup, retaining its actual Razor markup and handlers.
    public sealed class IsolatedWork : StaffServiceRequestWork
    {
        protected override Task OnInitializedAsync() => Task.CompletedTask;
        protected override Task OnParametersSetAsync() => Task.CompletedTask;
    }
    private sealed class Capture : IComponentActivator
    {
        public IsolatedWork? Work { get; private set; }
        public IComponent CreateInstance(Type type)
        {
            var instance = (IComponent)Activator.CreateInstance(type)!;
            if (instance is IsolatedWork work)
            {
                Work = work;
                var now = DateTimeOffset.UtcNow;
                Set("detail", new StaffServiceRequestDetailResponse(Guid.NewGuid(), "SR-1", "Yêu cầu", "Mô tả", null, null, null,
                    null, null, "IN_PROGRESS", null, null, null, null, now, now, now, [], []));
                Set("showProgressDialog", true); Set("progressNote", "Nội dung kiểm tra tiến độ.");
                void Set(string field, object value) => typeof(StaffServiceRequestWork).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(work, value);
            }
            return instance;
        }
    }
    private sealed class Navigation : NavigationManager
    {
        public Navigation() => Initialize("https://test.invalid/", "https://test.invalid/staff/service-requests/SR-1");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
    private sealed class FailureTransport(bool serverFailure) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/progress", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(serverFailure ? HttpStatusCode.InternalServerError : HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new { title = "Không thể lưu tiến độ.", errors = serverFailure ? null : new Dictionary<string, string[]> { ["Detail"] = ["Nội dung cần được kiểm tra."] } })
            });
        }
    }
}
