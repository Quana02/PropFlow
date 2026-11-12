using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PropFlow.Modules.Complaints.Application;
using PropFlow.Modules.Complaints.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Application;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Infrastructure.Persistence;

namespace PropFlow.Api.Composition;

public sealed class ComplaintCommitter(ComplaintsDbContext complaints, CommunicationDbContext communication,
    ResidentsDbContext residents, IComplaintClosureNotice notices, IFinanceNotificationRealtime realtime,
    ILogger<ComplaintCommitter> logger) : IComplaintCommitter
{
    public async Task SaveAsync(ComplaintClosed? closed, CancellationToken ct)
    {
        if (closed is null) { await complaints.SaveChangesAsync(ct); return; }
        // The complaint and its notification are committed on the same scoped connection.
        Guid? recipient;
        FinanceNotificationUpdate? update = null;
        await using var transaction = await complaints.Database.BeginTransactionAsync(ct);
        await communication.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct);
        try
        {
            recipient = await residents.Residents.AsNoTracking().Where(r => r.Id == closed.ResidentId)
                .Select(r => r.UserId).SingleOrDefaultAsync(ct);
            if (recipient.HasValue)
                update = await notices.AddAsync(closed.EventId, closed.ComplaintId, recipient.Value, closed.OccurredAt, ct);
            await complaints.SaveChangesAsync(ct);
            await communication.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        finally { await communication.Database.UseTransactionAsync(null, CancellationToken.None); }

        if (recipient is not Guid userId || update is null) return;
        try { await realtime.CreatedAsync(userId, update, CancellationToken.None); }
        catch (Exception exception)
        {
            logger.LogWarning("Realtime complaint notification deferred for {NoticeId} ({ErrorType}).", update.Id, exception.GetType().Name);
        }
    }
}
