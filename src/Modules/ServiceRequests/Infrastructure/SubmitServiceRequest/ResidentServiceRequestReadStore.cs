using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.ServiceRequests.Application.ListResidentServiceRequests;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.Modules.ServiceRequests.Infrastructure.SubmitServiceRequest;

public sealed class ResidentServiceRequestReadStore(ServiceRequestsDbContext db)
    : IResidentServiceRequestReadStore
{
    public async Task<IReadOnlyList<ResidentServiceRequestListItem>> ListAsync(
        Guid residentId,
        CancellationToken ct) =>
        await db.ServiceRequests
            .AsNoTracking()
            .Where(request => request.ResidentId == residentId)
            .OrderByDescending(request => request.SubmittedAt)
            .Select(request => new ResidentServiceRequestListItem(
                request.Id,
                request.RequestNumber,
                request.Title,
                request.Category == null ? null : request.Category.Code,
                request.Status.ToString(),
                request.FinalPriorityCode,
                request.ServiceAreaCode,
                request.PreferredDate,
                request.PreferredTimeCode,
                request.SubmittedAt,
                request.UpdatedAt,
                db.ServiceRequestFeedbacks.Where(feedback => feedback.ServiceRequestId == request.Id).Select(feedback => (int?)feedback.Rating).SingleOrDefault(),
                db.ServiceRequestFeedbacks.Where(feedback => feedback.ServiceRequestId == request.Id).Select(feedback => feedback.Comment).SingleOrDefault(),
                db.ServiceRequestFeedbacks.Where(feedback => feedback.ServiceRequestId == request.Id).Select(feedback => (DateTimeOffset?)feedback.SubmittedAt).SingleOrDefault()))
            .ToArrayAsync(ct);
}
