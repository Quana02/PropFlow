namespace PropFlow.Api.Composition;
public sealed class OperationalNoticeWorker(IServiceScopeFactory scopes, ILogger<OperationalNoticeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try {
            while (await timer.WaitForNextTickAsync(ct)) {
                foreach (var assetSource in new[] { true, false }) {
                    try {
                        using var scope = scopes.CreateScope();
                        var dispatcher = scope.ServiceProvider.GetRequiredService<OperationalNoticeDispatcher>();
                        if (assetSource) await dispatcher.DispatchAssetsAsync(ct); else await dispatcher.DispatchMaintenanceAsync(ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                    catch (Exception ex) { logger.LogWarning("Operational broadcast deferred ({ErrorType}).", ex.GetType().Name); }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
