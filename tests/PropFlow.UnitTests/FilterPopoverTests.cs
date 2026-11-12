using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using PropFlow.Web.Client.Features.Administration.Components;
using PropFlow.Web.Client.Features.Administration.Models;
using PropFlow.Web.Client.Features.Administration.Services;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Shared.Forms;
using PropFlow.Web.Client.Shared.Tables;

namespace PropFlow.UnitTests;

public sealed class FilterPopoverTests
{
    [Fact]
    public async Task Audit_filter_commits_only_changed_drafts_and_preserves_paging_until_apply()
    {
        using var transport = new AuditTransport();
        using var http = new HttpClient(transport) { BaseAddress = new Uri("https://test.invalid") };
        var api = new AuthenticatedApiClient(http, NullLogger<ApiClient>.Instance);
        var js = new PopoverJs();
        using var services = new ServiceCollection().AddLogging()
            .AddSingleton<IJSRuntime>(js)
            .AddSingleton<IAdministrationApiClient>(new AdministrationApiClient(api)).BuildServiceProvider();
        await using var renderer = new AuditRenderer(services);

        await renderer.Render();
        await renderer.NextPage();
        Assert.Equal(["?page=1&pageSize=20", "?page=2&pageSize=20"], transport.Queries);

        await renderer.Open();
        await renderer.Actor("bỏ thay đổi");
        await renderer.Dispatcher.InvokeAsync(js.Dismiss);
        Assert.Equal(2, transport.Queries.Count);

        await renderer.Open();
        await renderer.Apply();
        Assert.Equal(2, transport.Queries.Count);

        await renderer.Open();
        await renderer.Actor("Kế toán");
        Assert.Equal(2, transport.Queries.Count);
        await renderer.Apply();
        Assert.Equal("?page=1&pageSize=20&actor=K%E1%BA%BF%20to%C3%A1n", transport.Queries[^1]);
        Assert.Equal(3, transport.Queries.Count);
        Assert.True(await renderer.ShowsEmptyResults());

        await renderer.Open();
        await renderer.Reset();
        Assert.Equal(3, transport.Queries.Count);
        await renderer.Apply();
        Assert.Equal("?page=1&pageSize=20", transport.Queries[^1]);
        Assert.Equal(4, transport.Queries.Count);
    }

    private sealed class AuditTransport : HttpMessageHandler
    {
        public List<string> Queries { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = request.RequestUri!.Query;
            Queries.Add(query);
            var item = new AdministrationActivity(Guid.NewGuid(), DateTimeOffset.UtcNow, "created",
                ActorUserId: null, ActorUsername: null, ActorDisplayName: null, TargetAccountId: null,
                TargetUsername: null, TargetDisplayName: null, TargetRole: null, TargetStatus: null,
                OldRole: null, NewRole: null, OldStatus: null, NewStatus: null);
            var page = query.Contains("page=2", StringComparison.Ordinal) ? 2 : 1;
            var data = query.Contains("actor=", StringComparison.Ordinal)
                ? new PagedAdministrationActivities(0, page, 20, [])
                : new PagedAdministrationActivities(40, page, 20, [item]);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(data) });
        }
    }

    private sealed class PopoverJs : IJSRuntime, IJSObjectReference
    {
        private DotNetObjectReference<PropFlowFilterPopover>? reference;
        public void Dismiss() => reference!.Value.Closed();

        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args)
        {
            if (identifier == "connect" && args?.LastOrDefault() is DotNetObjectReference<PropFlowFilterPopover> filter)
                reference = filter;
            if (identifier == "close" && reference is not null) Dismiss();
            return ValueTask.FromResult(identifier is "import" or "connect" ? (T)(object)this : default!);
        }
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken token, object?[]? args) => InvokeAsync<T>(identifier, args);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

#pragma warning disable BL0006
    private sealed class AuditRenderer(IServiceProvider services) : Renderer(services, services.GetRequiredService<ILoggerFactory>())
    {
        private int root;
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => throw new InvalidOperationException("Audit panel rendering failed.", exception);

        public Task Render() => Dispatcher.InvokeAsync(() =>
        {
            root = AssignRootComponentId(InstantiateComponent(typeof(SystemActivityPanel)));
            return RenderRootComponentAsync(root);
        });

        public Task Open() => Click<PropFlowFilterPopover>("Bộ lọc");
        public Task Apply() => Click<PropFlowFilterPopover>("Áp dụng");
        public Task Reset() => Click<PropFlowFilterPopover>("Đặt lại");
        public Task NextPage() => Click<TablePagination>("Trang sau");
        public Task Actor(string value) => Dispatch<PropFlowFilterPopover>("input", "oninput", new ChangeEventArgs { Value = value },
            attrs => attrs.Any(a => a.AttributeName == "maxlength" && Equals(a.AttributeValue, "100")));
        public Task<bool> ShowsEmptyResults() => Dispatcher.InvokeAsync(() =>
            Frames(root).Any(f => Content(f).Contains("Không có hoạt động phù hợp", StringComparison.Ordinal)));

        private Task Click<T>(string text) => Dispatch<T>("button", "onclick", new MouseEventArgs(),
            attrs => attrs.Any(a => (a.AttributeName is "title" or "aria-label") && Equals(a.AttributeValue, text)), text);

        private RenderTreeFrame[] Frames(int id)
        {
            var frames = GetCurrentRenderTreeFrames(id);
            return frames.Array[..frames.Count];
        }

        private static string Content(RenderTreeFrame frame) => frame.FrameType switch
        {
            RenderTreeFrameType.Text => frame.TextContent,
            RenderTreeFrameType.Markup => WebUtility.HtmlDecode(frame.MarkupContent),
            _ => ""
        };

        private Task Dispatch<T>(string tag, string name, EventArgs args, Func<RenderTreeFrame[], bool> match, string? text = null) => Dispatcher.InvokeAsync(() =>
        {
            var child = Frames(root).Single(f => f.FrameType == RenderTreeFrameType.Component && f.ComponentType == typeof(T));
            var frames = Frames(child.ComponentId);
            for (var index = 0; index < frames.Length; index++)
            {
                var frame = frames[index];
                if (frame.FrameType != RenderTreeFrameType.Element || frame.ElementName != tag) continue;
                var attributes = frames.Skip(index + 1).TakeWhile(f => f.FrameType == RenderTreeFrameType.Attribute).ToArray();
                var handler = attributes.SingleOrDefault(f => f.AttributeName == name).AttributeEventHandlerId;
                if (handler == 0) continue;
                var next = frames[index + 1 + attributes.Length];
                if (!match(attributes) && (text is null || Content(next).Trim() != text)) continue;
                return DispatchEventAsync(handler, null, args);
            }
            throw new InvalidOperationException($"No {tag} found for {text ?? name}.");
        });
    }
#pragma warning restore BL0006
}
