using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropFlow.Web.Client.Shared.Tables;

namespace PropFlow.UnitTests;

public sealed class ListRefreshTests
{
    [Fact]
    public async Task Refresh_uses_one_request_at_a_time_and_recovers_after_loading()
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new RefreshRenderer(services);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        await renderer.Render(async () =>
        {
            calls++;
            started.TrySetResult();
            await finished.Task;
        });

        var first = renderer.Click();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal("true", await renderer.Busy());
            await renderer.Click();
            Assert.Equal(1, calls);
        }
        finally { finished.TrySetResult(); }

        await first;
        Assert.Equal("false", await renderer.Busy());
        await renderer.Click();
        Assert.Equal(2, calls);
    }

#pragma warning disable BL0006
    private sealed class RefreshRenderer(IServiceProvider services) : Renderer(services, services.GetRequiredService<ILoggerFactory>())
    {
        private int id;
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => throw new InvalidOperationException("Refresh rendering failed.", exception);

        public Task Render(Func<Task> onRefresh) => Dispatcher.InvokeAsync(() =>
        {
            id = AssignRootComponentId(InstantiateComponent(typeof(PropFlowListRefresh)));
            return RenderRootComponentAsync(id, ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(PropFlowListRefresh.OnRefresh)] = EventCallback.Factory.Create(this, onRefresh)
            }));
        });

        public Task Click() => Dispatcher.InvokeAsync(() =>
            DispatchEventAsync(Attribute("onclick").AttributeEventHandlerId, null, new MouseEventArgs()));

        public Task<string> Busy() => Dispatcher.InvokeAsync(() => (string)Attribute("aria-busy").AttributeValue);

        private RenderTreeFrame Attribute(string name)
        {
            var frames = GetCurrentRenderTreeFrames(id);
            return frames.Array.Take(frames.Count)
                .Single(frame => frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == name);
        }
    }
#pragma warning restore BL0006
}
