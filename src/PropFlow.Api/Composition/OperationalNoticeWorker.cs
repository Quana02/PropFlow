namespace PropFlow.Api.Composition;
public sealed class OperationalNoticeWorker(IServiceScopeFactory scopes, ILogger<OperationalNoticeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try {
            while (await timer.WaitForNextTickAsync(ct)) {
                foreach (var source in new[] { 0, 1, 2 }) {
                    try {
                        using var scope = scopes.CreateScope();
                        if (source == 2) await scope.ServiceProvider.GetRequiredService<ServiceRequestCostNoticeDispatcher>().DispatchAsync(ct);
                        else {
                            var dispatcher = scope.ServiceProvider.GetRequiredService<OperationalNoticeDispatcher>();
                            if (source == 0) await dispatcher.DispatchAssetsAsync(ct); else await dispatcher.DispatchMaintenanceAsync(ct);
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                    catch (Exception ex) { logger.LogWarning("Operational broadcast deferred ({ErrorType}).", ex.GetType().Name); }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
