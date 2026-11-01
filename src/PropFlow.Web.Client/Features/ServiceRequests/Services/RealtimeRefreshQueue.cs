namespace PropFlow.Web.Client.Features.ServiceRequests.Services;

public sealed class RealtimeRefreshQueue(
    Func<Task> refresh,
    TimeSpan? debounce = null,
    Func<Task>? afterRefresh = null) : IDisposable
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TimeSpan _debounce = debounce ?? TimeSpan.FromMilliseconds(250);
    private Task _runner = Task.CompletedTask;
    private bool _running;
    private bool _pending;
    private bool _immediate;
    private bool _disposed;

    public Task RequestAsync() => Enqueue(immediate: false);
    public Task RefreshNowAsync() => Enqueue(immediate: true);

    private Task Enqueue(bool immediate)
    {
        lock (_sync)
        {
            if (_disposed) return Task.CompletedTask;
            _pending = true;
            _immediate |= immediate;
            if (!_running)
            {
                _running = true;
                _runner = RunAsync();
            }

            return _runner;
        }
    }

    private async Task RunAsync()
    {
        try
        {
            while (true)
            {
                bool immediate;
                lock (_sync)
                {
                    if (_disposed) return;
                    immediate = _immediate;
                    _immediate = false;
                    _pending = false;
                }

                if (!immediate && _debounce > TimeSpan.Zero)
                    await Task.Delay(_debounce, _lifetime.Token);

                lock (_sync)
                {
                    if (_disposed) return;
                    // Every request received before this point is covered by the refresh below.
                    _pending = false;
                    _immediate = false;
                }

                await refresh();
                if (afterRefresh is not null)
                    await afterRefresh();

                lock (_sync)
                {
                    if (_disposed || !_pending)
                    {
                        _running = false;
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            lock (_sync) _running = false;
        }
        catch
        {
            lock (_sync) _running = false;
            throw;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _pending = false;
        }

        _lifetime.Cancel();
    }
}
