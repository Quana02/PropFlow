namespace PropFlow.Modules.AiRecommendation.Domain.Recommendations;

public class AiRequestRecommendation
{
    private AiRequestRecommendation()
    {
        Provider = string.Empty;
        ModelName = string.Empty;
    }

    private AiRequestRecommendation(
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

    private AiRequestRecommendation(
        Guid serviceRequestId,
        int attemptNo,
        string provider,
        string modelName,
        string suggestedPriorityCode,
        DateTimeOffset now,
        bool? maintenanceRecommended = null,
        string? recommendedAction = null,
        string? recommendedResourceSummary = null,
        string? recommendedResourcesJson = null,
        string? reasoningSummary = null,
        string? promptVersion = null,
        string? inputHash = null,
        int? inputTokens = null,
        int? outputTokens = null,
        int? totalTokens = null,
        int? latencyMs = null)
        : this(serviceRequestId, attemptNo, provider, modelName, promptVersion, inputHash, inputTokens, outputTokens, totalTokens, latencyMs, now)
    {
        SuggestedPriorityCode = NormalizeRequiredCode(suggestedPriorityCode, nameof(suggestedPriorityCode));
        MaintenanceRecommended = maintenanceRecommended;
        RecommendedAction = NormalizeOptionalText(recommendedAction);
        RecommendedResourceSummary = NormalizeOptionalText(recommendedResourceSummary);
        RecommendedResourcesJson = NormalizeOptionalText(recommendedResourcesJson);
        ReasoningSummary = NormalizeOptionalText(reasoningSummary);
        RunStatus = AiRunStatus.SUCCESS;
    }

    public static AiRequestRecommendation CreateSuccess(
        Guid serviceRequestId,
        int attemptNo,
        string provider,
        string modelName,
        string suggestedPriorityCode,
        DateTimeOffset now,
        bool? maintenanceRecommended = null,
        string? recommendedAction = null,
        string? recommendedResourceSummary = null,
        string? recommendedResourcesJson = null,
        string? reasoningSummary = null,
        string? promptVersion = null,
        string? inputHash = null,
        int? inputTokens = null,
        int? outputTokens = null,
        int? totalTokens = null,
        int? latencyMs = null)
    {
        return new AiRequestRecommendation(
            serviceRequestId,
            attemptNo,
            provider,
            modelName,
            suggestedPriorityCode,
            now,
            maintenanceRecommended,
            recommendedAction,
            recommendedResourceSummary,
            recommendedResourcesJson,
            reasoningSummary,
            promptVersion,
            inputHash,
            inputTokens,
            outputTokens,
            totalTokens,
            latencyMs);
    }

    public static AiRequestRecommendation CreateFailure(
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

        var recommendation = new AiRequestRecommendation(
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

        recommendation.RunStatus = AiRunStatus.FAILED;
        recommendation.ErrorMessage = errorMessage.Trim();

        return recommendation;
    }

    public Guid Id { get; private set; }
    public Guid ServiceRequestId { get; private set; }
    public int AttemptNo { get; private set; }
    public string Provider { get; private set; }
    public string ModelName { get; private set; }
    public string? PromptVersion { get; private set; }
    public string? SuggestedPriorityCode { get; private set; }
    public bool? MaintenanceRecommended { get; private set; }
    public string? RecommendedAction { get; private set; }
    public string? RecommendedResourceSummary { get; private set; }
    public string? RecommendedResourcesJson { get; private set; }
    public string? ReasoningSummary { get; private set; }
    public string? InputHash { get; private set; }
    public int? InputTokens { get; private set; }
    public int? OutputTokens { get; private set; }
    public int? TotalTokens { get; private set; }
    public AiRunStatus RunStatus { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int? LatencyMs { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public AiRecommendationReview? Review { get; private set; }

    public AiRecommendationReview ConfirmSuggestion(Guid reviewedBy, DateTimeOffset now, string? reviewNote = null)
    {
        EnsureSuccessfulAndUnreviewed(now);

        var review = new AiRecommendationReview(
            Id,
            reviewedBy,
            AiReviewDecision.CONFIRMED,
            now,
            SuggestedPriorityCode,
            MaintenanceRecommended,
            RecommendedAction,
            RecommendedResourceSummary,
            reviewNote);

        Review = review;
        return review;
    }

    public AiRecommendationReview CorrectRecommendation(
        string finalPriorityCode,
        Guid reviewedBy,
        DateTimeOffset now,
        string reviewNote,
        bool? finalMaintenanceRequired = null,
        string? finalAction = null,
        string? finalResourcePlan = null)
    {
        EnsureSuccessfulAndUnreviewed(now);

        if (string.IsNullOrWhiteSpace(reviewNote))
        {
            throw new ArgumentException("Review note is required when correcting AI recommendations.", nameof(reviewNote));
        }

        var normalizedPriority = NormalizeRequiredCode(finalPriorityCode, nameof(finalPriorityCode));
        var normalizedAction = NormalizeOptionalText(finalAction);
        var normalizedResourcePlan = NormalizeOptionalText(finalResourcePlan);

        if (normalizedPriority == SuggestedPriorityCode &&
            finalMaintenanceRequired == MaintenanceRecommended &&
            string.Equals(normalizedAction, RecommendedAction, StringComparison.Ordinal) &&
            string.Equals(normalizedResourcePlan, RecommendedResourceSummary, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Use confirmation when the final recommendation matches the AI suggestion.");
        }

        var review = new AiRecommendationReview(
            Id,
            reviewedBy,
            AiReviewDecision.CORRECTED,
            now,
            normalizedPriority,
            finalMaintenanceRequired,
            normalizedAction,
            normalizedResourcePlan,
            reviewNote);

        Review = review;
        return review;
    }

    private void EnsureSuccessfulAndUnreviewed(DateTimeOffset now)
    {
        if (RunStatus != AiRunStatus.SUCCESS)
        {
            throw new InvalidOperationException("Only successful AI recommendations can be reviewed.");
        }

        if (Review is not null)
        {
            throw new InvalidOperationException("AI recommendation has already been reviewed.");
        }

        if (now < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(now), "Review time cannot be before the AI recommendation was created.");
        }
    }

    private static void ValidateNonNegative(int? value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value must not be negative.");
        }
    }

    private static string NormalizeRequiredCode(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Code is required.", parameterName);
        }

        return value.Trim().ToUpperInvariant();
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
