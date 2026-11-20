using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Communication.Application;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Infrastructure;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.Api.Composition;

public sealed class ServiceRequestCostNoticeDispatcher(ServiceRequestsDbContext db, ResidentsDbContext residents,
    IFinanceNotificationStore notifications, TimeProvider clock, ILogger<ServiceRequestCostNoticeDispatcher> logger,
    WorkflowNotificationDelivery? workflow = null)
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
                if (item.Version != 1)
                    throw new InvalidOperationException("Unsupported service request cost notice.");
                if (item.Name is ServiceRequestWorkflowNotice.Submitted or ServiceRequestWorkflowNotice.Assigned)
                {
                    var delivery = workflow ?? throw new InvalidOperationException("Workflow notification delivery is unavailable.");
                    var change = JsonSerializer.Deserialize<ServiceRequestWorkflowNotice>(item.Payload)!;
                    if (item.Name == ServiceRequestWorkflowNotice.Submitted)
                    {
                        var residentId = await db.ServiceRequests.AsNoTracking().Where(x => x.Id == item.SourceId)
                            .Select(x => x.ResidentId).SingleAsync(ct);
                        var name = await residents.Residents.AsNoTracking().Where(x => x.Id == residentId)
                            .Select(x => x.FullName).SingleOrDefaultAsync(ct);
                        await delivery.PublishSubmissionAsync(item, change.SenderUserId, name, ct);
                    }
                    else
                    {
                        var recipient = change.RecipientUserId
                            ?? throw new InvalidOperationException("Assignment notice has no recipient.");
                        // Lock the active assignment so a concurrent reassignment cannot end it during delivery.
                        var activeAssignment = await db.ServiceRequestAssignments.FromSqlInterpolated($"""
                            SELECT * FROM service_requests.service_request_assignments
                            WHERE service_request_id = {item.SourceId} AND staff_user_id = {recipient}
                              AND status IN ('ASSIGNED', 'IN_PROGRESS')
                            FOR UPDATE
                            """).AsNoTracking().SingleOrDefaultAsync(ct);
                        if (activeAssignment is not null && (change.AssignmentId is { } id
                                ? activeAssignment.Id == id
                                : activeAssignment.AssignedAt == item.CreatedAt &&
                                  !await db.ServiceRequestAssignments.AnyAsync(assignment =>
                                      assignment.ServiceRequestId == item.SourceId &&
                                      assignment.StaffUserId == recipient &&
                                      assignment.AssignedAt == item.CreatedAt &&
                                      assignment.Id != activeAssignment.Id, ct)))
                            await delivery.PublishAssignmentAsync(item, change.SenderUserId, recipient,
                                "SERVICE_REQUEST", $"/staff/service-requests/{Uri.EscapeDataString(change.RequestNumber)}", ct);
                    }
                    item.Delivered(clock.GetUtcNow());
                    continue;
                }
                if (item.Name == "ServiceRequestInvoiceReady")
                {
                    var invoice = JsonSerializer.Deserialize<ServiceRequestInvoiceNotice>(item.Payload)!;
                    var linked = await db.ServiceRequests.AsNoTracking()
                        .Where(x => x.Id == item.SourceId && x.ServiceInvoiceId == invoice.InvoiceId)
                        .Select(x => new { x.SubmittedByUserId }).SingleOrDefaultAsync(ct);
                    if (linked is not null)
                    {
                        // This is the account passed to Billing when the issued recipient was snapshotted.
                        var recipient = linked.SubmittedByUserId;
                        if (recipient is null) throw new InvalidOperationException("Resident account unavailable.");
                        await notifications.PublishAsync(new(item.Id, recipient, null, "Hóa đơn dịch vụ đã sẵn sàng",
                            $"Yêu cầu {invoice.RequestNumber} đã được nghiệm thu. Vui lòng xem hóa đơn và thanh toán theo số tiền đã duyệt.",
                            "", "SERVICE_REQUEST", item.SourceId, $"/resident/service-requests/{item.SourceId}"), ct);
                    }
                    item.Delivered(clock.GetUtcNow());
                    continue;
                }
                if (item.Name == "ServiceRequestAdditionalCostReviewRequested")
                {
                    var review = JsonSerializer.Deserialize<AdditionalCostReviewNotice>(item.Payload)!;
                    var waiting = await db.ServiceRequests.AsNoTracking().AnyAsync(x => x.Id == item.SourceId &&
                        x.AdditionalCostProposalId == review.ProposalId && x.AdditionalCostConsentStatus == "AWAITING_MANAGER", ct);
                    if (waiting)
                        await (workflow ?? throw new InvalidOperationException("Workflow notification delivery is unavailable."))
                            .PublishCostReviewAsync(item, review.StaffUserId, review.RequestNumber, ct);
                    item.Delivered(clock.GetUtcNow());
                    continue;
                }
                if (item.Name != "ServiceRequestAdditionalCostProposed")
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
