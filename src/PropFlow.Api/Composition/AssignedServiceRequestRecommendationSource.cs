using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.AiRecommendation.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.Api.Composition;

public sealed class AssignedServiceRequestRecommendationSource(AiRecommendationDbContext recommendations, ServiceRequestsDbContext requests)
    : IAssignedServiceRequestRecommendationSource
{
    public async Task<ServiceRequestHandlingPlan?> GetApprovedAsync(Guid serviceRequestId, string? currentPriority, CancellationToken ct)
    {
        if (currentPriority is null) return null;
        var approved = await recommendations.AiRequestRecommendations.AsNoTracking().Include(x => x.Review)
            .Where(x => x.ServiceRequestId == serviceRequestId && x.Review != null)
            .OrderByDescending(x => x.AttemptNo).FirstOrDefaultAsync(ct);
        if (approved?.Review is not { } review || review.FinalPriorityCode != currentPriority) return null;
        var request = await requests.ServiceRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == serviceRequestId, ct);
        return request is not null && request.FinalPriorityCode == currentPriority && approved.InputHash == ManagerServiceRequestRecommendation.InputHash(request)
            ? new(currentPriority, review.FinalMaintenanceRequired, review.FinalAction, review.FinalResourcePlan) : null;
    }
}
