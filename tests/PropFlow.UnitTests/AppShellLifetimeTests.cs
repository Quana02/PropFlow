using System.Reflection;
using Microsoft.JSInterop;
using PropFlow.Web.Client.Layout.Shared;

namespace PropFlow.UnitTests;

public sealed class AppShellLifetimeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Navigation_during_module_import_skips_initializing_detached_shell(bool useAmbient)
    {
        var js = new DeferredModule();
        var shell = new ShellProbe(useAmbient);
        typeof(AppShell).GetProperty("JS", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shell, js);

        var initialization = shell.Render();
        var disposal = shell.DisposeAsync().AsTask();
        js.Imported.SetResult(js);
        await Task.WhenAll(initialization, disposal);

        Assert.Equal(0, js.InitializeCalls);
        Assert.Equal(1, js.ModuleDisposals);
    }

    private sealed class ShellProbe : AppShell
    {
        public ShellProbe(bool useAmbient)
        {
            ShowAmbientBackground = useAmbient;
            ShowWeatherAndClock = !useAmbient;
        }

        public Task Render() => OnAfterRenderAsync(firstRender: true);
    }

    private sealed class DeferredModule : IJSRuntime, IJSObjectReference
    {
        public TaskCompletionSource<IJSObjectReference> Imported { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int InitializeCalls { get; private set; }
        public int ModuleDisposals { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "import") return (TValue)await Imported.Task;
            Assert.Equal("initialize", identifier);
            InitializeCalls++;
            return (TValue)(object)this;
        }

        public ValueTask DisposeAsync()
        {
            ModuleDisposals++;
            return ValueTask.CompletedTask;
        }
    }
}
