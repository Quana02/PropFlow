using PropFlow.Modules.Communication.Application.EmergencyCalls;

namespace PropFlow.Api.Composition;

public sealed class EmergencyCallDispatchWorker(IServiceScopeFactory scopes, IHostEnvironment environment, ILogger<EmergencyCallDispatchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (environment.IsEnvironment("Testing")) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                try { await using var scope = scopes.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<IEmergencyCalls>().DispatchAsync(ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception e) { logger.LogWarning("Emergency assignment deferred ({Kind}).", e.GetType().Name); }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
