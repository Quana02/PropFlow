using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PropFlow.Modules.Billing.Contracts;

namespace PropFlow.Modules.AiRecommendation.Infrastructure.UtilityConsumption;

public sealed class UtilityNoticeWorker(IServiceScopeFactory scopes, ILogger<UtilityNoticeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<UtilityInsights>().DispatchAsync(scope.ServiceProvider.GetRequiredService<IFinanceNoticePublisher>(), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Utility notification delivery deferred ({ErrorType})", ex.GetType().Name); }
            try { if (!await timer.WaitForNextTickAsync(ct)) break; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
    }
}
