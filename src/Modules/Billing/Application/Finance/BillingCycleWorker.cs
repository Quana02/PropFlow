using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace PropFlow.Modules.Billing.Application.Finance;
public sealed class BillingCycleWorker(IServiceScopeFactory scopes, ILogger<BillingCycleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Delay until the host is running; do not perform finance work during a failed startup.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try { while (await timer.WaitForNextTickAsync(ct)) {
            try { using var scope = scopes.CreateScope(); await scope.ServiceProvider.GetRequiredService<IBillingCycle>().RunAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning("Billing cycle deferred ({Type}).", ex.GetType().Name); }
        } } catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
