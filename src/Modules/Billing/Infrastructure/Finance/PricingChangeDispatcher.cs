using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Billing.Infrastructure.Persistence;

namespace PropFlow.Modules.Billing.Infrastructure.Finance;
public sealed class PricingChangeDispatcher(BillingDbContext db, IPricingChangePublisher publisher,
    ILogger<PricingChangeDispatcher> logger, TimeProvider? clock = null)
{
    private DateTimeOffset Now => (clock ?? TimeProvider.System).GetUtcNow();
    public async Task DispatchAsync(CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71241003)", ct);
        var now = Now;
        var pending = await db.PricingChanges.Where(x => x.Status == "Pending" && x.NextAttemptAt <= now)
            .OrderBy(x => x.CreatedAt).Take(10).ToArrayAsync(ct);
        foreach (var item in pending)
        {
            try {
                await publisher.PublishAsync(new(item.Id, item.Title, item.Message, item.CreatedAt), ct);
                item.Delivered(Now);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) {
                item.Failed(ex.GetType().Name, Now);
                logger.LogWarning("Pricing change {EventId} delivery attempt {Attempt}: {Status} ({ErrorType}).",
                    item.Id, item.Attempts, item.Status, ex.GetType().Name);
            }
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
