using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PropFlow.Modules.RemoteOnlineSign.Application;

public sealed class RemoteOnlineSignExpirationWorker(
    IServiceScopeFactory scopes,
    ILogger<RemoteOnlineSignExpirationWorker> logger) : BackgroundService
{
    private const int BatchSize = 100;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval);
        while (true)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var expired = await scope.ServiceProvider.GetRequiredService<RemoteContractService>()
                    .ExpireDueContractsAsync(BatchSize, ct);
                if (expired > 0)
                    logger.LogInformation("Expired {Count} overdue remote contracts.", expired);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Remote contract expiration will be retried.");
            }

            try { if (!await timer.WaitForNextTickAsync(ct)) return; }
            catch (OperationCanceledException) { return; }
        }
    }
}
