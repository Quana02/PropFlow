using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PropFlow.Modules.Complaints.Application;
using PropFlow.Modules.Complaints.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Application;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Infrastructure.Persistence;

namespace PropFlow.Api.Composition;

public sealed class ComplaintCommitter(ComplaintsDbContext complaints, CommunicationDbContext communication,
    ResidentsDbContext residents, IComplaintClosureNotice notices) : IComplaintCommitter
{
    public async Task SaveAsync(ComplaintClosed? closed, CancellationToken ct)
    {
        if (closed is null) { await complaints.SaveChangesAsync(ct); return; }
        // Both contexts use the same scoped Npgsql connection. The event's notification
        // is committed synchronously with its source, so no post-commit delivery can be lost.
        await using var transaction = await complaints.Database.BeginTransactionAsync(ct);
        await communication.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct);
        try
        {
            var recipient = await residents.Residents.AsNoTracking().Where(r => r.Id == closed.ResidentId)
                .Select(r => r.UserId).SingleOrDefaultAsync(ct);
            if (recipient.HasValue)
                await notices.AddAsync(closed.EventId, closed.ComplaintId, recipient.Value, closed.OccurredAt, ct);
            await complaints.SaveChangesAsync(ct);
            await communication.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        finally { await communication.Database.UseTransactionAsync(null, ct); }
    }
}
