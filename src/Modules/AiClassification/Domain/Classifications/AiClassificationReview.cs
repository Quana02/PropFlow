namespace PropFlow.Modules.AiClassification.Domain.Classifications;

public class AiClassificationReview
{
    private AiClassificationReview()
    {
    }

    internal AiClassificationReview(
        Guid classificationId,
        Guid reviewedBy,
        AiReviewDecision decision,
        DateTimeOffset now,
        Guid? finalCategoryId = null,
        string? reviewNote = null)
    {
        if (classificationId == Guid.Empty)
        {
            throw new ArgumentException("Classification id is required.", nameof(classificationId));
        }

        if (reviewedBy == Guid.Empty)
        {
            throw new ArgumentException("Reviewed by is required.", nameof(reviewedBy));
        }

        if (decision is AiReviewDecision.CONFIRMED or AiReviewDecision.CORRECTED or AiReviewDecision.OVERRIDDEN)
        {
            if (finalCategoryId is null || finalCategoryId == Guid.Empty)
            {
                throw new ArgumentException("Final category id is required for a final classification decision.", nameof(finalCategoryId));
            }
        }

        Id = Guid.NewGuid();
        ClassificationId = classificationId;
        ReviewedBy = reviewedBy;
        Decision = decision;
        FinalCategoryId = finalCategoryId;
        ReviewNote = string.IsNullOrWhiteSpace(reviewNote) ? null : reviewNote.Trim();
        ReviewedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ClassificationId { get; private set; }
    public Guid ReviewedBy { get; private set; }
    public AiReviewDecision Decision { get; private set; }
    public Guid? FinalCategoryId { get; private set; }
    public string? ReviewNote { get; private set; }
    public DateTimeOffset ReviewedAt { get; private set; }

    public AiRequestClassification? Classification { get; private set; }
}
