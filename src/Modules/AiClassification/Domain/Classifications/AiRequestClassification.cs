namespace PropFlow.Modules.AiClassification.Domain.Classifications;

public class AiRequestClassification
{
    private AiRequestClassification()
    {
        Provider = string.Empty;
        ModelName = string.Empty;
    }

    private AiRequestClassification(
        Guid serviceRequestId,
        int attemptNo,
        string provider,
        string modelName,
        string? promptVersion,
        string? inputHash,
        int? inputTokens,
        int? outputTokens,
        int? totalTokens,
        int? latencyMs,
        DateTimeOffset now)
    {
        if (serviceRequestId == Guid.Empty)
        {
            throw new ArgumentException("Service request id is required.", nameof(serviceRequestId));
        }

        if (attemptNo < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNo), "Attempt number must be at least 1.");
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("AI provider code is required.", nameof(provider));
        }

        if (string.IsNullOrWhiteSpace(modelName))
        {
            throw new ArgumentException("AI model name is required.", nameof(modelName));
        }

        ValidateNonNegative(inputTokens, nameof(inputTokens));
        ValidateNonNegative(outputTokens, nameof(outputTokens));
        ValidateNonNegative(totalTokens, nameof(totalTokens));
        ValidateNonNegative(latencyMs, nameof(latencyMs));

        Id = Guid.NewGuid();
        ServiceRequestId = serviceRequestId;
        AttemptNo = attemptNo;
        Provider = provider.Trim();
        ModelName = modelName.Trim();
        PromptVersion = NormalizeOptionalCode(promptVersion);
        InputHash = NormalizeOptionalText(inputHash);
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        TotalTokens = totalTokens;
        LatencyMs = latencyMs;
        CreatedAt = now;
    }

    private AiRequestClassification(
        Guid serviceRequestId,
        int attemptNo,
        string provider,
        string modelName,
        Guid predictedCategoryId,
        DateTimeOffset now,
        decimal? confidenceScore = null,
        string? reasoningSummary = null,
        string? promptVersion = null,
        string? inputHash = null,
        int? inputTokens = null,
        int? outputTokens = null,
        int? totalTokens = null,
        int? latencyMs = null)
        : this(serviceRequestId, attemptNo, provider, modelName, promptVersion, inputHash, inputTokens, outputTokens, totalTokens, latencyMs, now)
    {
        if (predictedCategoryId == Guid.Empty)
        {
            throw new ArgumentException("Predicted category id is required for successful classification.", nameof(predictedCategoryId));
        }

        ValidateConfidence(confidenceScore);

        PredictedCategoryId = predictedCategoryId;
        ConfidenceScore = confidenceScore;
        ReasoningSummary = NormalizeOptionalText(reasoningSummary);
        RunStatus = AiRunStatus.SUCCESS;
    }

    public static AiRequestClassification CreateSuccess(
        Guid serviceRequestId,
        int attemptNo,
        string provider,
        string modelName,
        Guid predictedCategoryId,
        DateTimeOffset now,
        decimal? confidenceScore = null,
        string? reasoningSummary = null,
        string? promptVersion = null,
        string? inputHash = null,
        int? inputTokens = null,
        int? outputTokens = null,
        int? totalTokens = null,
        int? latencyMs = null)
    {
        return new AiRequestClassification(
            serviceRequestId,
            attemptNo,
            provider,
            modelName,
            predictedCategoryId,
            now,
            confidenceScore,
            reasoningSummary,
            promptVersion,
            inputHash,
            inputTokens,
            outputTokens,
            totalTokens,
            latencyMs);
    }

    public static AiRequestClassification CreateFailure(
        Guid serviceRequestId,
        int attemptNo,
        string provider,
        string modelName,
        string errorMessage,
        DateTimeOffset now,
        string? promptVersion = null,
        string? inputHash = null,
        int? inputTokens = null,
        int? outputTokens = null,
        int? totalTokens = null,
        int? latencyMs = null)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException("Failure reason is required.", nameof(errorMessage));
        }

        var classification = new AiRequestClassification(
            serviceRequestId,
            attemptNo,
            provider,
            modelName,
            promptVersion,
            inputHash,
            inputTokens,
            outputTokens,
            totalTokens,
            latencyMs,
            now);

        classification.RunStatus = AiRunStatus.FAILED;
        classification.ErrorMessage = errorMessage.Trim();

        return classification;
    }

    public Guid Id { get; private set; }
    public Guid ServiceRequestId { get; private set; }
    public int AttemptNo { get; private set; }
    public string Provider { get; private set; }
    public string ModelName { get; private set; }
    public string? PromptVersion { get; private set; }
    public Guid? PredictedCategoryId { get; private set; }
    public decimal? ConfidenceScore { get; private set; }
    public string? ReasoningSummary { get; private set; }
    public string? InputHash { get; private set; }
    public int? InputTokens { get; private set; }
    public int? OutputTokens { get; private set; }
    public int? TotalTokens { get; private set; }
    public AiRunStatus RunStatus { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int? LatencyMs { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public AiClassificationReview? Review { get; private set; }

    public AiClassificationReview ConfirmSuggestion(Guid reviewedBy, DateTimeOffset now, string? reviewNote = null)
    {
        EnsureSuccessfulAndUnreviewed();

        var review = new AiClassificationReview(
            Id,
            reviewedBy,
            AiReviewDecision.CONFIRMED,
            now,
            PredictedCategoryId,
            reviewNote);

        Review = review;
        return review;
    }

    public AiClassificationReview CorrectClassification(Guid finalCategoryId, Guid reviewedBy, DateTimeOffset now, string? reviewNote = null)
    {
        EnsureSuccessfulAndUnreviewed();

        if (finalCategoryId == Guid.Empty)
        {
            throw new ArgumentException("Final category id is required.", nameof(finalCategoryId));
        }

        if (finalCategoryId == PredictedCategoryId)
        {
            throw new InvalidOperationException("Use confirmation when the final category matches the AI suggestion.");
        }

        var review = new AiClassificationReview(
            Id,
            reviewedBy,
            AiReviewDecision.CORRECTED,
            now,
            finalCategoryId,
            reviewNote);

        Review = review;
        return review;
    }

    private void EnsureSuccessfulAndUnreviewed()
    {
        if (RunStatus != AiRunStatus.SUCCESS)
        {
            throw new InvalidOperationException("Only successful AI classifications can be reviewed.");
        }

        if (Review is not null)
        {
            throw new InvalidOperationException("AI classification has already been reviewed.");
        }
    }

    private static void ValidateConfidence(decimal? confidenceScore)
    {
        if (confidenceScore is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(confidenceScore), "Confidence score must be between 0 and 1.");
        }
    }

    private static void ValidateNonNegative(int? value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value must not be negative.");
        }
    }

    private static string? NormalizeOptionalCode(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
