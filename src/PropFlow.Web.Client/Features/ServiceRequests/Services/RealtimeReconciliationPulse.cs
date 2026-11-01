namespace PropFlow.Web.Client.Features.ServiceRequests.Services;

/// <summary>
/// Provides a low-frequency reconciliation pulse in case a browser sleep, proxy or short network
/// interruption causes a SignalR event to be missed without immediately closing the connection.
/// SignalR remains the primary update path; this pulse only asks active subscribers to reconcile.
/// </summary>
public sealed class RealtimeReconciliationPulse(
    Func<Task> reconcile,
    TimeSpan? interval = null) : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TimeSpan _interval = Validate(interval ?? TimeSpan.FromMinutes(1));
    private Task _runner = Task.CompletedTask;
    private int _started;
    private bool _disposed;

    public void Start()
    {
        if (_disposed || Interlocked.CompareExchange(ref _started, 1, 0) != 0) return;
        _runner = RunAsync();
    }

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(_lifetime.Token))
                await reconcile();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _lifetime.CancelAsync();
        try
        {
            await _runner;
        }
        finally
        {
            _lifetime.Dispose();
        }
    }

    private static TimeSpan Validate(TimeSpan interval) => interval > TimeSpan.Zero
        ? interval
        : throw new ArgumentOutOfRangeException(nameof(interval), "Reconciliation interval must be positive.");
}
