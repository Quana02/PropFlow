namespace PropFlow.Modules.AiRecommendation.Domain.Recommendations;

public class AiRecommendationReview
{
    private AiRecommendationReview()
    {
        FinalPriorityCode = string.Empty;
    }

    internal AiRecommendationReview(
        Guid recommendationId,
        Guid reviewedBy,
        AiReviewDecision decision,
        DateTimeOffset now,
        string? finalPriorityCode,
        bool? finalMaintenanceRequired = null,
        string? finalAction = null,
        string? finalResourcePlan = null,
        string? reviewNote = null)
    {
        if (recommendationId == Guid.Empty)
        {
            throw new ArgumentException("Recommendation id is required.", nameof(recommendationId));
        }

        if (reviewedBy == Guid.Empty)
        {
            throw new ArgumentException("Reviewed by is required.", nameof(reviewedBy));
        }

        if (decision is AiReviewDecision.CONFIRMED or AiReviewDecision.CORRECTED or AiReviewDecision.OVERRIDDEN &&
            string.IsNullOrWhiteSpace(finalPriorityCode))
        {
            throw new ArgumentException("Final priority code is required for a final recommendation decision.", nameof(finalPriorityCode));
        }

        Id = Guid.NewGuid();
        RecommendationId = recommendationId;
        ReviewedBy = reviewedBy;
        Decision = decision;
        FinalPriorityCode = string.IsNullOrWhiteSpace(finalPriorityCode) ? null : finalPriorityCode.Trim().ToUpperInvariant();
        FinalMaintenanceRequired = finalMaintenanceRequired;
        FinalAction = string.IsNullOrWhiteSpace(finalAction) ? null : finalAction.Trim();
        FinalResourcePlan = string.IsNullOrWhiteSpace(finalResourcePlan) ? null : finalResourcePlan.Trim();
        ReviewNote = string.IsNullOrWhiteSpace(reviewNote) ? null : reviewNote.Trim();
        ReviewedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid RecommendationId { get; private set; }
    public Guid ReviewedBy { get; private set; }
    public AiReviewDecision Decision { get; private set; }
    public string? FinalPriorityCode { get; private set; }
    public bool? FinalMaintenanceRequired { get; private set; }
    public string? FinalAction { get; private set; }
    public string? FinalResourcePlan { get; private set; }
    public string? ReviewNote { get; private set; }
    public DateTimeOffset ReviewedAt { get; private set; }

    public AiRequestRecommendation? Recommendation { get; private set; }
}
