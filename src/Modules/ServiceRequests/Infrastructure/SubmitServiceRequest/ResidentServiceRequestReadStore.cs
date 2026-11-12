using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.ServiceRequests.Application.ListResidentServiceRequests;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.Modules.ServiceRequests.Infrastructure.SubmitServiceRequest;

public sealed class ResidentServiceRequestReadStore(ServiceRequestsDbContext db)
    : IResidentServiceRequestReadStore
{
    public async Task<IReadOnlyList<ResidentServiceRequestListItem>> ListAsync(
        Guid residentId,
        ResidentServiceRequestFilter filter,
        CancellationToken ct)
    {
        var query = db.ServiceRequests
            .AsNoTracking()
            .Where(request => request.ResidentId == residentId);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.ToLower();
            query = query.Where(request =>
                request.RequestNumber.ToLower().Contains(search) ||
                request.Title.ToLower().Contains(search));
        }
        if (filter.Status is { } status)
            query = query.Where(request => request.Status == status);
        if (filter.SubmittedFromInclusive is { } from)
            query = query.Where(request => request.SubmittedAt >= from);
        if (filter.SubmittedToExclusive is { } to)
            query = query.Where(request => request.SubmittedAt < to);

        return await query
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
}
