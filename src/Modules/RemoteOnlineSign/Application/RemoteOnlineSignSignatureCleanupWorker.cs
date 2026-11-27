using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PropFlow.Modules.RemoteOnlineSign.Application;

// Periodic cleanup for signature payloads that can no longer be used:
// finalized sessions and sessions whose OTP window has closed. Runs only when
// RemoteOnlineSign:SignaturePayloadCleanup:Enabled is true so legacy payloads
// are not touched before the operator approves a database cleanup.
public sealed class RemoteOnlineSignSignatureCleanupWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<RemoteOnlineSignSignatureCleanupWorker> logger) : BackgroundService
{
    private const int BatchSize = 500;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!configuration.GetValue<bool>("RemoteOnlineSign:SignaturePayloadCleanup:Enabled"))
        {
            logger.LogInformation("RemoteOnlineSign signature payload cleanup is disabled.");
            return;
        }
        using var timer = new PeriodicTimer(Interval);
        while (true)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var cleaned = await scope.ServiceProvider
                    .GetRequiredService<RemoteContractService>()
                    .CleanupExpiredSignaturePayloadsAsync(BatchSize, ct);
                if (cleaned > 0) logger.LogInformation("Cleared signature payloads from {Count} unusable signing sessions.", cleaned);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning("Signature payload cleanup deferred ({Type}).", ex.GetType().Name); }
            try { if (!await timer.WaitForNextTickAsync(ct)) return; }
            catch (OperationCanceledException) { return; }
        }
    }
}
