using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using PropFlow.Web.Client.Features.Maintenance.Models;
using PropFlow.Web.Client.Features.Maintenance.Pages;
using PropFlow.Web.Client.Features.Maintenance.Services;
using PropFlow.Web.Client.Features.ServiceRequests.Services;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Services.Authentication;
using PropFlow.Web.Client.Shared.Feedback;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-07")]
public sealed class MaintenanceReviewUiTests
{
    [Theory]
    [InlineData("Chấp nhận & đóng", "approve")]
    [InlineData("Yêu cầu xử lý thêm", "request-revision")]
    public async Task Review_button_sends_the_API_decision_only_after_confirmation(string label, string decision)
    {
        var transport = new Transport();
        var api = new AuthenticatedApiClient(new HttpClient(transport) { BaseAddress = new("https://test.invalid/") }, NullLogger<ApiClient>.Instance);
        await using var services = new ServiceCollection().AddLogging()
            .AddSingleton<NavigationManager, TestNavigation>()
            .AddSingleton<IJSRuntime, NoJs>()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton(new AuthSession(api)).AddSingleton<ToastService>()
            .AddSingleton<MaintenanceHubConnectionFactory>()
            .AddSingleton<IMaintenanceApiClient>(new MaintenanceApiClient(api))
            .AddSingleton<IManagerServiceRequestApiClient>(new ManagerServiceRequestApiClient(api))
            .BuildServiceProvider();
        await using var renderer = new ReviewRenderer(services);
        var root = await renderer.Mount(transport.Task.Id);
        await renderer.Click(root, label);
        Assert.Null(transport.Decision);
        await renderer.Confirm(root);
        Assert.Equal(decision, transport.Decision);
        Assert.Equal($"/api/v1/maintenance/tasks/{transport.Task.Id}/results/{transport.Task.LatestResult!.Id}/review", transport.ReviewPath);
    }

    private sealed class Transport : HttpMessageHandler
    {
        public MaintenanceTaskModel Task { get; } = new(Guid.NewGuid(), "MT-UI", Guid.NewGuid(), null, null,
            "Bảo trì theo lịch", "Kiểm tra thiết bị", "HIGH", MaintenanceTaskStatus.COMPLETED, null, null,
            Guid.NewGuid(), DateTimeOffset.UtcNow, LatestResult: new(Guid.NewGuid(), 1, Guid.NewGuid(), "Đã kiểm tra",
                null, null, null, null, MaintenanceResultStatus.SUBMITTED, null, null, DateTimeOffset.UtcNow, null));
        public string? Decision { get; private set; }
        public string? ReviewPath { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path.EndsWith("/review"))
            {
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Decision = payload.RootElement.GetProperty("decision").GetString();
                ReviewPath = path;
            }
            if (path.EndsWith("/assets")) return new(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<AssetModel>()) };
            if (path.Contains("history")) return new(HttpStatusCode.NotFound);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(Task) };
        }
    }

    private sealed class ReviewRenderer(IServiceProvider services) : Renderer(services, services.GetRequiredService<ILoggerFactory>())
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        protected override void HandleException(Exception exception) => throw new InvalidOperationException("Maintenance rendering failed.", exception);
        protected override Task UpdateDisplayAsync(in RenderBatch batch) => System.Threading.Tasks.Task.CompletedTask;
        public Task<int> Mount(Guid taskId) => Dispatcher.InvokeAsync(async () =>
        {
            var root = AssignRootComponentId(InstantiateComponent(typeof(MaintenanceTaskDetail)));
            await RenderRootComponentAsync(root, ParameterView.FromDictionary(new Dictionary<string, object?> { ["TaskId"] = taskId }));
            return root;
        });
        public Task Confirm(int root) => Dispatcher.InvokeAsync(() =>
        {
            var frames = GetCurrentRenderTreeFrames(root);
            var dialog = frames.Array.Take(frames.Count).Single(f => f.FrameType == RenderTreeFrameType.Component && f.ComponentType == typeof(PropFlowConfirmationDialog));
            return Click(dialog.ComponentId, "Xác nhận", confirmation: true);
        });
        public Task Click(int root, string label, bool confirmation = false) => Dispatcher.InvokeAsync(() =>
        {
            var frames = GetCurrentRenderTreeFrames(root);
            for (var i = 0; i < frames.Count; i++)
            {
                var frame = frames.Array[i];
                if (frame.FrameType != RenderTreeFrameType.Element || frame.ElementName != "button") continue;
                var subtree = frames.Array.Skip(i + 1).Take(frame.ElementSubtreeLength - 1).ToArray();
                if (confirmation ? !subtree.Any(f => f.FrameType == RenderTreeFrameType.Attribute && f.AttributeName == "class" && Equals(f.AttributeValue, "pf-dialog__primary"))
                    : !subtree.Any(f => f.FrameType == RenderTreeFrameType.Text && WebUtility.HtmlDecode(f.TextContent).Trim() == label
                        || f.FrameType == RenderTreeFrameType.Markup && WebUtility.HtmlDecode(f.MarkupContent).Trim() == label)) continue;
                var handler = subtree.First(f => f.FrameType == RenderTreeFrameType.Attribute && f.AttributeName == "onclick").AttributeEventHandlerId;
                return DispatchEventAsync(handler, null, new MouseEventArgs());
            }
            throw new InvalidOperationException("Review button not found: " + label);
        });
    }
    private sealed class TestNavigation : NavigationManager { public TestNavigation() => Initialize("https://test.invalid/", "https://test.invalid/maintenance/tasks/test"); }
    private sealed class NoJs : IJSRuntime, IJSObjectReference
    {
        public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => new(id is "import" or "openDialog" ? (T)(object)this : default!);
        public ValueTask<T> InvokeAsync<T>(string id, CancellationToken ct, object?[]? args) => InvokeAsync<T>(id, args);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
