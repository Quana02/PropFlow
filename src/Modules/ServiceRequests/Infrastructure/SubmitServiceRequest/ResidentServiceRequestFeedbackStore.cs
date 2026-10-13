using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.ServiceRequests.Application.RateResidentServiceRequest;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestFeedbacks;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.Modules.ServiceRequests.Infrastructure.SubmitServiceRequest;

public sealed class ResidentServiceRequestFeedbackStore(ServiceRequestsDbContext db) : IResidentServiceRequestFeedbackStore
{
    public async Task<(SaveResidentServiceRequestFeedbackOutcome Outcome, ResidentServiceRequestFeedbackResponse? Response)> SaveAsync(
        Guid residentId, Guid serviceRequestId, int rating, string comment, DateTimeOffset now, CancellationToken ct)
    {
        var request = await db.ServiceRequests.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == serviceRequestId && item.ResidentId == residentId, ct);
        if (request is null) return (SaveResidentServiceRequestFeedbackOutcome.RequestNotFound, null);
        if (request.Status is not (ServiceRequestStatus.RESOLVED or ServiceRequestStatus.CLOSED))
            return (SaveResidentServiceRequestFeedbackOutcome.RequestNotCompleted, null);

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
        return (SaveResidentServiceRequestFeedbackOutcome.Saved,
            new(feedback.ServiceRequestId, feedback.Rating, feedback.Comment, feedback.SubmittedAt));
    }
}
