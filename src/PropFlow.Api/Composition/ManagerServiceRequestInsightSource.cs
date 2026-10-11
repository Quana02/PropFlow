using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.AiClassification.Infrastructure.Persistence;
using PropFlow.Modules.AiRecommendation.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Contracts;

namespace PropFlow.Api.Composition;

public sealed class ManagerServiceRequestInsightSource(
    AiClassificationDbContext classifications,
    AiRecommendationDbContext recommendations) : IManagerServiceRequestInsightSource
{
    public async Task<ManagerServiceRequestAiInsight?> GetAsync(Guid serviceRequestId, CancellationToken ct)
    {
        var classification = await classifications.AiRequestClassifications.AsNoTracking()
            .Where(x => x.ServiceRequestId == serviceRequestId)
            .OrderByDescending(x => x.AttemptNo)
            .Select(x => new { Status = x.RunStatus.ToString(), x.PredictedCategoryId, x.ConfidenceScore, x.ReasoningSummary })
            .FirstOrDefaultAsync(ct);
        var recommendation = await recommendations.AiRequestRecommendations.AsNoTracking()
            .Where(x => x.ServiceRequestId == serviceRequestId)
            .OrderByDescending(x => x.AttemptNo)
            .Select(x => new { Status = x.RunStatus.ToString(), x.SuggestedPriorityCode, x.MaintenanceRecommended, x.RecommendedAction, x.RecommendedResourceSummary, x.ReasoningSummary })
            .FirstOrDefaultAsync(ct);

        if (classification is null && recommendation is null) return null;
        return new(
            classification?.Status, classification?.PredictedCategoryId, classification?.ConfidenceScore, classification?.ReasoningSummary,
            recommendation?.Status, recommendation?.SuggestedPriorityCode, recommendation?.MaintenanceRecommended,
            recommendation?.RecommendedAction, recommendation?.RecommendedResourceSummary, recommendation?.ReasoningSummary);
    }
}
