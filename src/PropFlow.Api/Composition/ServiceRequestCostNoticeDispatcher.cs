using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Communication.Application;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Infrastructure;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.Api.Composition;

public sealed class ServiceRequestCostNoticeDispatcher(ServiceRequestsDbContext db, ResidentsDbContext residents,
    IFinanceNotificationStore notifications, TimeProvider clock, ILogger<ServiceRequestCostNoticeDispatcher> logger)
{
    public async Task DispatchAsync(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71241006)", ct);
        var now = clock.GetUtcNow();
        var events = await db.ResidentEvents.Where(x => x.Status == "Pending" && x.NextAttemptAt <= now)
            .OrderBy(x => x.CreatedAt).Take(10).ToArrayAsync(ct);
        foreach (var item in events)
        {
            try
            {
                if (item.Name != "ServiceRequestAdditionalCostProposed" || item.Version != 1)
                    throw new InvalidOperationException("Unsupported service request cost notice.");
                var notice = JsonSerializer.Deserialize<AdditionalCostNotice>(item.Payload)!;
                var current = await db.ServiceRequests.AsNoTracking().Where(x => x.Id == item.SourceId)
                    .Select(x => new { x.AdditionalCostProposalId, x.AdditionalCostConsentStatus }).SingleOrDefaultAsync(ct);
                if (current?.AdditionalCostProposalId != notice.ProposalId || current.AdditionalCostConsentStatus != "PENDING")
                { item.Delivered(clock.GetUtcNow()); continue; }
                var userId = await residents.Residents.AsNoTracking().Where(x => x.Id == notice.ResidentId)
                    .Select(x => x.UserId).SingleOrDefaultAsync(ct);
                if (userId is null) throw new InvalidOperationException("Resident account unavailable.");
                await notifications.PublishAsync(new(item.Id, userId, null, "Yêu cầu xác nhận chi phí phát sinh",
                    $"Yêu cầu {notice.RequestNumber} phát sinh {notice.Amount:N0} đ. Lý do: {notice.Note}. Vui lòng đồng ý hoặc từ chối; công việc đang chờ phản hồi của bạn.",
                    "", "SERVICE_REQUEST", item.SourceId, $"/resident/service-requests/{item.SourceId}"), ct);
                item.Delivered(clock.GetUtcNow());
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                item.Failed(error.GetType().Name, clock.GetUtcNow());
                logger.LogWarning("Service request cost notice {EventId} deferred ({ErrorType}).", item.Id, error.GetType().Name);
            }
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
