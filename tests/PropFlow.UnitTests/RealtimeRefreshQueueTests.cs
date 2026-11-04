using PropFlow.Web.Client.Features.ServiceRequests.Services;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-05")]
public sealed class RealtimeRefreshQueueTests
{
    [Fact]
    public async Task Burst_of_realtime_events_is_coalesced_into_one_refresh()
    {
        var refreshes = 0;
        using var queue = new RealtimeRefreshQueue(
            () => { Interlocked.Increment(ref refreshes); return Task.CompletedTask; },
            TimeSpan.FromMilliseconds(10));

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => queue.RequestAsync()));

        Assert.Equal(1, refreshes);
    }

    [Fact]
    public async Task Event_received_during_refresh_runs_one_follow_up_refresh()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshes = 0;
        using var queue = new RealtimeRefreshQueue(async () =>
        {
            if (Interlocked.Increment(ref refreshes) != 1) return;
            firstStarted.TrySetResult();
            await releaseFirst.Task;
        }, TimeSpan.Zero);

        var first = queue.RefreshNowAsync();
        await firstStarted.Task;
        var pending = Task.WhenAll(queue.RequestAsync(), queue.RequestAsync(), queue.RequestAsync());
        releaseFirst.TrySetResult();
        await Task.WhenAll(first, pending);

        Assert.Equal(2, refreshes);
    }

    [Fact]
    public async Task Completed_realtime_refresh_notifies_the_component_to_render()
    {
        var refreshed = false;
        var rendered = false;
        using var queue = new RealtimeRefreshQueue(
            () => { refreshed = true; return Task.CompletedTask; },
            TimeSpan.Zero,
            () => { rendered = true; return Task.CompletedTask; });

        await queue.RefreshNowAsync();

        Assert.True(refreshed);
        Assert.True(rendered);
    }

    [Fact]
    public async Task Reconciliation_pulse_starts_once_and_stops_when_disposed()
    {
        var firstPulse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePulse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pulses = 0;
        var pulse = new RealtimeReconciliationPulse(async () =>
        {
            Interlocked.Increment(ref pulses);
            firstPulse.TrySetResult();
            await releasePulse.Task;
        }, TimeSpan.FromMilliseconds(10));

        pulse.Start();
        pulse.Start();
        await firstPulse.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(30);
        Assert.Equal(1, pulses);

        var disposing = pulse.DisposeAsync().AsTask();
        releasePulse.TrySetResult();
        await disposing;
        var countAfterDispose = pulses;
        await Task.Delay(30);

        Assert.Equal(countAfterDispose, pulses);
    }
}
