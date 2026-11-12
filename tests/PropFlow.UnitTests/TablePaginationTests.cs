using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using PropFlow.Web.Client.Shared.Tables;

namespace PropFlow.UnitTests;

#pragma warning disable BL0006
public sealed class TablePaginationTests
{
    [Theory]
    [InlineData("Trang đầu", 1)]
    [InlineData("Trang trước", 7)]
    [InlineData("Trang sau", 9)]
    [InlineData("Trang cuối", 69)]
    public async Task Navigation_invokes_one_callback_with_the_target(string button, int target)
    {
        await using var services = Services();
        await using var renderer = new PagerRenderer(services);
        var pages = new List<int>();
        await renderer.SetPage(8, 69, pages.Add);
        await renderer.Click(button);
        Assert.Equal(new[] { target }, pages);
    }

    [Theory]
    [InlineData("18", 18)]
    [InlineData("0", 1)]
    [InlineData("-5", 1)]
    [InlineData("100", 69)]
    [InlineData("999999999999999999999999999999999999", 69)]
    [InlineData("-999999999999999999999999999999999999", 1)]
    [InlineData(" +007 ", 7)]
    [InlineData("8", null)]
    [InlineData("", null)]
    [InlineData("3.5", null)]
    [InlineData("1e2", null)]
    [InlineData("abc", null)]
    [InlineData("9999999999999999999999999x", null)]
    public async Task Jump_validates_integer_then_clamps_without_reloading_the_same_page(string input, int? target)
    {
        await using var services = Services();
        await using var renderer = new PagerRenderer(services);
        var pages = new List<int>();
        await renderer.SetPage(8, 69, pages.Add);
        await renderer.Type(input);
        await renderer.Submit();

        Assert.Equal(target is { } expected ? new[] { expected } : Array.Empty<int>(), pages);
        var js = services.GetRequiredService<PagerJs>();
        Assert.Equal(target.HasValue || input == "8", js.Calls.Contains("close"));
    }

    [Fact]
    public async Task Shrinking_total_corrects_once_after_loading_finishes()
    {
        await using var services = Services();
        await using var renderer = new PagerRenderer(services);
        var pages = new List<int>();
        await renderer.SetPage(8, 69, pages.Add);
        await renderer.SetPage(8, 3, pages.Add, busy: true);
        Assert.Empty(pages);

        await renderer.SetPage(8, 3, pages.Add);
        await renderer.SetPage(8, 3, pages.Add);
        Assert.Equal(new[] { 3 }, pages);
        await renderer.SetPage(3, 3, pages.Add);
        await renderer.SetPage(3, 1, pages.Add);
        await renderer.SetPage(1, 0, pages.Add);
        Assert.Equal(new[] { 3, 1 }, pages);
    }

    [Fact]
    public async Task Changing_filters_can_reset_to_page_one_without_an_extra_request()
    {
        await using var services = Services();
        await using var renderer = new PagerRenderer(services);
        var pages = new List<int>();
        await renderer.SetPage(18, 69, pages.Add);
        await renderer.SetPage(1, 69, pages.Add, busy: true);
        await renderer.SetPage(1, 2, pages.Add);
        Assert.Empty(pages);

        await renderer.Click("Trang sau");
        Assert.Equal(new[] { 2 }, pages);
    }

    [Fact]
    public async Task Page_indicator_controls_the_jump_dialog()
    {
        await using var services = Services();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var pager = await renderer.RenderComponentAsync<TablePagination>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(TablePagination.Page)] = 2,
                [nameof(TablePagination.TotalPages)] = 69,
                [nameof(TablePagination.PageChanged)] = EventCallback.Factory.Create<int>(this, _ => { })
            }));
            return pager.ToHtmlString();
        });
        Assert.Contains("aria-haspopup=\"dialog\"", html);
        Assert.Contains("popover=\"auto\"", html);
        Assert.Contains("autofocus", html);
        Assert.Contains("placeholder=\"Nhập số trang\"", System.Net.WebUtility.HtmlDecode(html));
        Assert.Contains("value=\"\"", html);
        Assert.DoesNotContain("<label", html);
        if (Environment.GetEnvironmentVariable("PROPFLOW_PAGINATION_PREVIEW") is not { Length: > 0 } path) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, html);
    }

    private static ServiceProvider Services() => new ServiceCollection().AddLogging().AddSingleton<PagerJs>()
        .AddSingleton<IJSRuntime>(sp => sp.GetRequiredService<PagerJs>()).BuildServiceProvider();

    private sealed class PagerRenderer(IServiceProvider services) : Renderer(services, services.GetRequiredService<ILoggerFactory>())
    {
        private int? componentId;
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => throw new InvalidOperationException("Pager rendering failed.", exception);

        public Task SetPage(int page, int total, Action<int> changePage, bool busy = false) => Dispatcher.InvokeAsync(() =>
        {
            componentId ??= AssignRootComponentId(InstantiateComponent(typeof(TablePagination)));
            return RenderRootComponentAsync(componentId.Value, ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(TablePagination.Page)] = page,
                [nameof(TablePagination.TotalPages)] = total,
                [nameof(TablePagination.Busy)] = busy,
                [nameof(TablePagination.PageChanged)] = EventCallback.Factory.Create<int>(this, changePage)
            }));
        });

        public Task Click(string title) => Dispatch("button", "onclick", new MouseEventArgs(), title);
        public Task Type(string value) => Dispatch("input", "oninput", new ChangeEventArgs { Value = value });
        public Task Submit() => Dispatch("form", "onsubmit", EventArgs.Empty);

        private Task Dispatch(string tag, string name, EventArgs args, string? title = null) => Dispatcher.InvokeAsync(() =>
        {
            var frames = GetCurrentRenderTreeFrames(componentId!.Value);
            for (var index = 0; index < frames.Count; index++)
            {
                var frame = frames.Array[index];
                if (frame.FrameType != RenderTreeFrameType.Element || frame.ElementName != tag) continue;
                var attributes = frames.Array.Take(frames.Count).Skip(index + 1)
                    .TakeWhile(entry => entry.FrameType == RenderTreeFrameType.Attribute).ToArray();
                if (title is not null && !attributes.Any(entry => entry.AttributeName == "title" && Equals(entry.AttributeValue, title))) continue;
                return DispatchEventAsync(attributes.Single(entry => entry.AttributeName == name).AttributeEventHandlerId, null, args);
            }
            throw new InvalidOperationException($"No {tag} found for {name}.");
        });
    }

    private sealed class PagerJs : IJSRuntime, IJSObjectReference
    {
        public List<string> Calls { get; } = [];

        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args)
        {
            Calls.Add(identifier);
            return ValueTask.FromResult(identifier is "import" or "connect" ? (T)(object)this : default!);
        }

        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
#pragma warning restore BL0006
