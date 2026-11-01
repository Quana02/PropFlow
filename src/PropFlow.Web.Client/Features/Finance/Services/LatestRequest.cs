namespace PropFlow.Web.Client.Features.Finance.Services;

/// <summary>Debounces list requests and prevents a superseded response from replacing the current list.</summary>
public sealed class LatestRequest : IDisposable
{
    private CancellationTokenSource? pending;
    private bool disposed;

    public async Task<bool> RunAsync<T>(Func<CancellationToken, Task<T>> request, Action<T> apply, int delayMs = 0)
    {
        if (disposed) return false;
        pending?.Cancel();
        using var current = new CancellationTokenSource();
        pending = current;
        try
        {
            if (delayMs > 0) await Task.Delay(delayMs, current.Token);
            var result = await request(current.Token);
            current.Token.ThrowIfCancellationRequested();
            apply(result);
            return true;
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested) { return false; }
        catch (Exception) when (current.IsCancellationRequested) { return false; }
        finally { if (ReferenceEquals(pending, current)) pending = null; }
    }

    public void Dispose() { disposed = true; pending?.Cancel(); }
}
