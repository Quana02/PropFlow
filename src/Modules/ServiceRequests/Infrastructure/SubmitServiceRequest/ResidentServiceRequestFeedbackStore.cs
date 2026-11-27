using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.ServiceRequests.Application.RateResidentServiceRequest;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestFeedbacks;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;
using PropFlow.Modules.Billing.Contracts;

namespace PropFlow.Modules.ServiceRequests.Infrastructure.SubmitServiceRequest;

public sealed class ResidentServiceRequestFeedbackStore(ServiceRequestsDbContext db, IServiceRequestBilling? billing = null) : IResidentServiceRequestFeedbackStore
{
    public async Task<(SaveResidentServiceRequestFeedbackOutcome Outcome, ResidentServiceRequestFeedbackResponse? Response)> SaveAsync(
        Guid residentId, Guid serviceRequestId, int rating, string comment, DateTimeOffset now, CancellationToken ct, Guid? actorId = null)
    {
        await using var transaction = await db.BeginWorkflowMutationAsync(serviceRequestId, ct);
        var request = await db.ServiceRequests
            .SingleOrDefaultAsync(item => item.Id == serviceRequestId && item.ResidentId == residentId, ct);
        if (request is null) return (SaveResidentServiceRequestFeedbackOutcome.RequestNotFound, null);
        if (request.Status != ServiceRequestStatus.CLOSED)
            return (SaveResidentServiceRequestFeedbackOutcome.RequestNotCompleted, null);
        if (request.BaseServiceCost + request.ApprovedAdditionalCost > 0)
        {
            var settlement = billing is null ? null : await billing.FindForResidentAsync(serviceRequestId, residentId, ct);
            if (settlement is null || settlement.OutstandingAmount > 0)
                return (SaveResidentServiceRequestFeedbackOutcome.PaymentRequired, null);
        }

        var feedback = await db.ServiceRequestFeedbacks.SingleOrDefaultAsync(item => item.ServiceRequestId == serviceRequestId, ct);
        if (feedback is null)
        {
            feedback = new ServiceRequestFeedback(serviceRequestId, residentId, rating, comment, now);
            db.ServiceRequestFeedbacks.Add(feedback);
        }
        else
        {
            feedback.Update(rating, comment, now);
        }

        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return (SaveResidentServiceRequestFeedbackOutcome.Saved,
            new(feedback.ServiceRequestId, feedback.Rating, feedback.Comment, feedback.SubmittedAt));
    }
}
