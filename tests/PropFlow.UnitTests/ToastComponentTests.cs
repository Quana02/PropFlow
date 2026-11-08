using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using PropFlow.Web.Client.Shared.Feedback;

namespace PropFlow.UnitTests;

// Render events and lifecycle without adding a UI test package or requiring a browser.
#pragma warning disable BL0006
public sealed class ToastComponentTests
{
    [Fact]
    public async Task Timed_toast_dismisses_once_and_cleans_up_its_js_registration()
    {
        await using var services = Services();
        await using var renderer = new ToastRenderer(services);
        var dismissed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        await renderer.Mount<PropFlowToast>(new()
        {
            ["Message"] = "Đã lưu", ["DurationMs"] = 100,
            ["OnDismiss"] = EventCallback.Factory.Create(this, () => { count++; dismissed.TrySetResult(); })
        });
        await dismissed.Task.WaitAsync(TimeSpan.FromSeconds(4));
        await Task.Delay(250);
        Assert.Equal(1, count);
        Assert.Contains("hide", services.GetRequiredService<JavaScript>().Calls);
        Assert.Empty(renderer.Errors);
    }

    [Theory]
    [InlineData("onmouseenter", "onmouseleave")]
    [InlineData("onfocusin", "onfocusout")]
    public async Task Hover_or_keyboard_focus_pauses_the_countdown(string pause, string resume)
    {
        await using var services = Services();
        await using var renderer = new ToastRenderer(services);
        var dismissed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = await renderer.Mount<PropFlowToast>(new()
        {
            ["Message"] = "Kết quả", ["DurationMs"] = 300,
            ["OnDismiss"] = EventCallback.Factory.Create(this, () => dismissed.TrySetResult())
        });
        await renderer.Event(id, pause);
        await Task.Delay(550);
        Assert.False(dismissed.Task.IsCompleted);
        await renderer.Event(id, resume);
        await dismissed.Task.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.Empty(renderer.Errors);
    }

    [Fact]
    public async Task Persistent_toast_can_be_closed_manually()
    {
        await using var services = Services();
        await using var renderer = new ToastRenderer(services);
        var dismissed = false;
        var id = await renderer.Mount<PropFlowToast>(new()
        {
            ["Message"] = "Cần xử lý", ["DurationMs"] = 0,
            ["OnDismiss"] = EventCallback.Factory.Create(this, () => dismissed = true)
        });
        await Task.Delay(250);
        Assert.False(dismissed);
        await renderer.Event(id, "onclick");
        Assert.True(dismissed);
        Assert.Empty(renderer.Errors);
    }

    [Fact]
    public async Task Disposing_the_toast_cancels_its_pending_callback_and_releases_js()
    {
        await using var services = Services();
        var renderer = new ToastRenderer(services);
        var dismissed = false;
        await renderer.Mount<PropFlowToast>(new()
        {
            ["Message"] = "Kết quả", ["DurationMs"] = 300,
            ["OnDismiss"] = EventCallback.Factory.Create(this, () => dismissed = true)
        });
        await renderer.DisposeAsync();
        await Task.Delay(550);
        Assert.False(dismissed);
        Assert.True(services.GetRequiredService<JavaScript>().Disposed);
        Assert.Empty(renderer.Errors);
    }

    [Fact]
    public async Task Navigation_removes_visible_toasts_and_disposed_host_does_not_render_new_messages()
    {
        await using var services = Services();
        var renderer = new ToastRenderer(services);
        await renderer.Mount<ToastHost>(new());
        var toasts = services.GetRequiredService<ToastService>();
        await renderer.Dispatcher.InvokeAsync(() => toasts.Success("Đã lưu"));
        Assert.Single(toasts.Messages);
        await renderer.Dispatcher.InvokeAsync(() => services.GetRequiredService<Navigation>().Go("next"));
        Assert.Empty(toasts.Messages);
        await renderer.DisposeAsync();
        var registrations = services.GetRequiredService<JavaScript>().Calls.Count;
        toasts.Success("Sau khi dispose");
        Assert.Equal(registrations, services.GetRequiredService<JavaScript>().Calls.Count);
        Assert.Empty(renderer.Errors);
    }

    private static ServiceProvider Services() => new ServiceCollection().AddLogging()
        .AddSingleton<JavaScript>().AddSingleton<IJSRuntime>(sp => sp.GetRequiredService<JavaScript>())
        .AddSingleton<ToastService>().AddSingleton<Navigation>()
        .AddSingleton<NavigationManager>(sp => sp.GetRequiredService<Navigation>()).BuildServiceProvider();

    private sealed class ToastRenderer(IServiceProvider services) : Renderer(services, services.GetRequiredService<ILoggerFactory>())
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        public List<Exception> Errors { get; } = [];
        protected override void HandleException(Exception exception) => Errors.Add(exception);
        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
        public Task<int> Mount<T>(Dictionary<string, object?> parameters) where T : IComponent => Dispatcher.InvokeAsync(async () =>
        {
            var id = AssignRootComponentId(InstantiateComponent(typeof(T)));
            await RenderRootComponentAsync(id, ParameterView.FromDictionary(parameters));
            return id;
        });
        public Task Event(int id, string name) => Dispatcher.InvokeAsync(async () =>
        {
            var frames = GetCurrentRenderTreeFrames(id);
            var handler = Enumerable.Range(0, frames.Count).Select(index => frames.Array[index])
                .Single(frame => frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == name).AttributeEventHandlerId;
            await DispatchEventAsync(handler, null, name.StartsWith("onfocus", StringComparison.Ordinal) ? new FocusEventArgs() : new MouseEventArgs());
        });
    }
    private sealed class Navigation : NavigationManager
    {
        public Navigation() => Initialize("https://test.invalid/", "https://test.invalid/account");
        public void Go(string path) { Uri = ToAbsoluteUri(path).ToString(); NotifyLocationChanged(false); }
        protected override void NavigateToCore(string uri, bool forceLoad) => Go(uri);
    }
    private sealed class JavaScript : IJSRuntime, IJSObjectReference
    {
        public List<string> Calls { get; } = [];
        public bool Disposed { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add(identifier);
            return ValueTask.FromResult(identifier == "import" ? (TValue)(object)this : default!);
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
#pragma warning restore BL0006
