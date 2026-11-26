using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.ServiceRequests.Application.AcceptResidentServiceRequest;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.Modules.ServiceRequests.Infrastructure.SubmitServiceRequest;

public sealed class ResidentCompletionStore(ServiceRequestsDbContext db, IServiceRequestBilling billing) : IResidentCompletionStore
{
    public async Task<ResidentCompletionOutcome> DecideAsync(Guid residentId, Guid userId, Guid requestId,
        ResidentCompletionDecision input, DateTimeOffset now, CancellationToken ct)
    {
        if (!input.Accepted && (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length is < 3 or > 1000))
            return ResidentCompletionOutcome.InvalidInput;
        await using var tx = await db.BeginWorkflowMutationAsync(requestId, ct);
        var request = await db.ServiceRequests.SingleOrDefaultAsync(x => x.Id == requestId && x.ResidentId == residentId, ct);
        if (request is null) return ResidentCompletionOutcome.NotFound;
        if (request.ResolvedAt != input.ExpectedResolvedAt) return ResidentCompletionOutcome.Conflict;
        if (input.Accepted && request.Status == ServiceRequestStatus.CLOSED && request.ClosedBy == userId) return ResidentCompletionOutcome.Success;
        if (request.Status != ServiceRequestStatus.RESOLVED) return ResidentCompletionOutcome.Conflict;
        var settlement = await billing.FindForResidentAsync(requestId, residentId, ct);
        if (settlement is { OutstandingAmount: > 0 } ||
            request.BaseServiceCost + request.ApprovedAdditionalCost > 0 &&
            (settlement is null || request.ServiceInvoiceId != settlement.InvoiceId || settlement.Status != "PAID"))
            return ResidentCompletionOutcome.PaymentRequired;
        if (input.Accepted) request.Close(userId, now);
        else request.RequestResidentRework(input.Reason!, now);
        db.ServiceRequestActivities.Add(new(requestId, input.Accepted ? ServiceActivityType.CLOSED : ServiceActivityType.REWORK_REQUESTED, now,
            fromStatus: ServiceRequestStatus.RESOLVED, toStatus: input.Accepted ? ServiceRequestStatus.CLOSED : ServiceRequestStatus.SUBMITTED,
            title: input.Accepted ? "Cư dân đã nghiệm thu" : "Cư dân yêu cầu làm lại",
            detail: input.Accepted ? "Bạn đã xác nhận hoàn thành và đóng yêu cầu." : $"Yêu cầu làm lại {request.RequestNumber}: {input.Reason!.Trim()}",
            performedBy: userId));
        if (!input.Accepted)
            db.ResidentEvents.Add(new ServiceRequestWorkflowNotice(userId, null, request.RequestNumber)
                .ToEvent(ServiceRequestWorkflowNotice.Submitted, requestId, now));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return ResidentCompletionOutcome.Conflict; }
        if (tx is not null) await tx.CommitAsync(ct);
        return ResidentCompletionOutcome.Success;
    }
}
