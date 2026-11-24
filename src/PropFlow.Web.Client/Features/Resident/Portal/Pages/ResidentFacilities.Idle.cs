using PropFlow.Web.Client.Features.Resident.Portal.Services;

namespace PropFlow.Web.Client.Features.Resident.Portal.Pages;

public partial class ResidentFacilities
{
    private ResidentAssistantIdleWatch idleWatch = new();
    private CancellationTokenSource? idleCancellation;
    private Task? idleMonitor;
    private int idleGeneration;
    private bool idleExiting;
    private bool IdleExpired => idleWatch.State == AssistantIdleState.Expired;
    private bool IdleOccupied => liveConnecting || liveEnding || busy || speaking || preparingSpeech || requestPreparing || requestSubmitting || liveTools.Count > 0;

    private void StartIdleWatch()
    {
        if (disposed || !conversationActive || idleCancellation is not null) return;
        idleExiting = false; idleWatch.Start();
        var version = ++idleGeneration;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        idleCancellation = cancellation;
        idleMonitor = MonitorIdle(version, cancellation);
    }
    private void StopIdleWatch()
    {
        ++idleGeneration; idleWatch.Stop();
        var previous = idleCancellation; idleCancellation = null; previous?.Cancel();
    }
    private async Task MonitorIdle(int version, CancellationTokenSource cancellation)
    {
        using (cancellation)
        using (var timer = new PeriodicTimer(TimeSpan.FromSeconds(1)))
        {
            try
            {
                while (await timer.WaitForNextTickAsync(cancellation.Token))
                    await InvokeAsync(() => CheckIdleTimeout(version));
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (disposed || cancellation.IsCancellationRequested) { }
        }
    }
    private async Task CheckIdleTimeout(int version)
    {
        if (disposed || version != idleGeneration || !conversationActive || idleExiting) return;
        var before = idleWatch.State; var seconds = idleWatch.SecondsRemaining;
        if (idleWatch.Tick(IdleOccupied) == AssistantIdleState.Expired) { await ExitIdleConversation(version); return; }
        if (before != idleWatch.State || seconds != idleWatch.SecondsRemaining) StateHasChanged();
    }
    private void MarkIdleActivity()
    {
        if (disposed || !conversationActive || idleExiting) return;
        var before = idleWatch.State;
        idleWatch.Activity(IdleOccupied);
        if (before != idleWatch.State) StateHasChanged();
    }
    private Task ContinueIdleConversation()
    {
        MarkIdleActivity();
        return CheckIdleTimeout(idleGeneration);
    }
    private Task EndIdleConversationNow() => ExitIdleConversation(idleGeneration);
    private async Task ExitIdleConversation(int version)
    {
        if (disposed || version != idleGeneration || !conversationActive || idleExiting || requestSubmitting) return;
        idleExiting = true; StateHasChanged();
        await StopConversation();
        if (!disposed) DashboardNavigation.NavigateTo("/resident");
    }
}
