using PropFlow.Modules.Billing.Infrastructure.Finance;

namespace PropFlow.Api.Composition;
public sealed class PricingChangeWorker(IServiceScopeFactory scopes, ILogger<PricingChangeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try {
            while (await timer.WaitForNextTickAsync(stoppingToken)) {
                try {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<PricingChangeDispatcher>().DispatchAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception ex) { logger.LogWarning("Pricing change delivery deferred ({ErrorType}).", ex.GetType().Name); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
