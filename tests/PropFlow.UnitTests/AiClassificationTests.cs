using PropFlow.Modules.AiClassification.Domain.Classifications;

namespace PropFlow.UnitTests;

public class AiClassificationTests
{
    private readonly DateTimeOffset _now = new(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AiRequestClassification_CreateSuccess_RecordsSuccessfulClassification()
    {
        var predictedCategoryId = Guid.NewGuid();
        var classification = NewSuccessfulClassification(predictedCategoryId: predictedCategoryId, confidenceScore: 0.8750m);

        Assert.NotEqual(Guid.Empty, classification.Id);
        Assert.Equal(AiRunStatus.SUCCESS, classification.RunStatus);
        Assert.Equal(predictedCategoryId, classification.PredictedCategoryId);
        Assert.Equal(0.8750m, classification.ConfidenceScore);
        Assert.Equal("PROMPT-V1", classification.PromptVersion);
        Assert.Null(classification.ErrorMessage);
        Assert.Equal(_now, classification.CreatedAt);
    }

    [Fact]
    public void AiRequestClassification_CreateSuccess_ValidatesRequiredAndTelemetryValues()
    {
        Assert.Throws<ArgumentException>(() => AiRequestClassification.CreateSuccess(Guid.Empty, 1, "Provider", "Model", Guid.NewGuid(), _now));
        Assert.Throws<ArgumentOutOfRangeException>(() => AiRequestClassification.CreateSuccess(Guid.NewGuid(), 0, "Provider", "Model", Guid.NewGuid(), _now));
        Assert.Throws<ArgumentException>(() => AiRequestClassification.CreateSuccess(Guid.NewGuid(), 1, "", "Model", Guid.NewGuid(), _now));
        Assert.Throws<ArgumentException>(() => AiRequestClassification.CreateSuccess(Guid.NewGuid(), 1, "Provider", "", Guid.NewGuid(), _now));
        Assert.Throws<ArgumentException>(() => AiRequestClassification.CreateSuccess(Guid.NewGuid(), 1, "Provider", "Model", Guid.Empty, _now));
        Assert.Throws<ArgumentOutOfRangeException>(() => AiRequestClassification.CreateSuccess(Guid.NewGuid(), 1, "Provider", "Model", Guid.NewGuid(), _now, confidenceScore: -0.1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => AiRequestClassification.CreateSuccess(Guid.NewGuid(), 1, "Provider", "Model", Guid.NewGuid(), _now, confidenceScore: 1.1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => AiRequestClassification.CreateSuccess(Guid.NewGuid(), 1, "Provider", "Model", Guid.NewGuid(), _now, inputTokens: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AiRequestClassification.CreateSuccess(Guid.NewGuid(), 1, "Provider", "Model", Guid.NewGuid(), _now, latencyMs: -1));
    }

    [Fact]
    public void AiRequestClassification_CreateFailure_CapturesFailureWithoutBlockingRequest()
    {
        var classification = AiRequestClassification.CreateFailure(
            Guid.NewGuid(),
            1,
            "Provider",
            "Model",
            "Provider timeout",
            _now,
            inputHash: "abc123",
            latencyMs: 1200);

        Assert.Equal(AiRunStatus.FAILED, classification.RunStatus);
        Assert.Equal("Provider timeout", classification.ErrorMessage);
        Assert.Null(classification.PredictedCategoryId);
        Assert.Null(classification.ConfidenceScore);
        Assert.Throws<ArgumentException>(() => AiRequestClassification.CreateFailure(Guid.NewGuid(), 1, "Provider", "Model", "", _now));
    }

    [Fact]
    public void AiRequestClassification_ConfirmSuggestion_CreatesFinalReviewFromSuggestion()
    {
        var predictedCategoryId = Guid.NewGuid();
        var classification = NewSuccessfulClassification(predictedCategoryId);
        var managerId = Guid.NewGuid();
        var reviewedAt = _now.AddMinutes(5);

        var review = classification.ConfirmSuggestion(managerId, reviewedAt, "Looks right");

        Assert.Equal(AiReviewDecision.CONFIRMED, review.Decision);
        Assert.Equal(predictedCategoryId, review.FinalCategoryId);
        Assert.Equal(managerId, review.ReviewedBy);
        Assert.Equal(reviewedAt, review.ReviewedAt);
        Assert.Same(review, classification.Review);
    }

    [Fact]
    public void AiRequestClassification_CorrectClassification_PreservesAiSuggestion()
    {
        var predictedCategoryId = Guid.NewGuid();
        var finalCategoryId = Guid.NewGuid();
        var classification = NewSuccessfulClassification(predictedCategoryId);

        var review = classification.CorrectClassification(finalCategoryId, Guid.NewGuid(), _now.AddMinutes(5), "Better operational category");

        Assert.Equal(AiReviewDecision.CORRECTED, review.Decision);
        Assert.Equal(finalCategoryId, review.FinalCategoryId);
        Assert.Equal(predictedCategoryId, classification.PredictedCategoryId);
    }

    [Fact]
    public void AiRequestClassification_ReviewRules_RejectFailedOrAlreadyReviewedResults()
    {
        var failed = AiRequestClassification.CreateFailure(Guid.NewGuid(), 1, "Provider", "Model", "Failed", _now);
        Assert.Throws<InvalidOperationException>(() => failed.ConfirmSuggestion(Guid.NewGuid(), _now));

        var classification = NewSuccessfulClassification();
        classification.ConfirmSuggestion(Guid.NewGuid(), _now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => classification.ConfirmSuggestion(Guid.NewGuid(), _now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => classification.CorrectClassification(Guid.NewGuid(), Guid.NewGuid(), _now.AddMinutes(2)));
    }

    [Fact]
    public void AiRequestClassification_CorrectionRules_ValidateFinalCategoryAndActor()
    {
        var predictedCategoryId = Guid.NewGuid();
        var classification = NewSuccessfulClassification(predictedCategoryId);

        Assert.Throws<ArgumentException>(() => classification.CorrectClassification(Guid.Empty, Guid.NewGuid(), _now));
        Assert.Throws<ArgumentException>(() => classification.CorrectClassification(Guid.NewGuid(), Guid.Empty, _now));
        Assert.Throws<InvalidOperationException>(() => classification.CorrectClassification(predictedCategoryId, Guid.NewGuid(), _now));
    }

    [Fact]
    public void AiRequestClassification_PublicApi_DoesNotExposeStatusTransitionsOrGenericReview()
    {
        var publicConstructors = typeof(AiRequestClassification).GetConstructors();
        Assert.Empty(publicConstructors);

        var publicMethods = typeof(AiRequestClassification)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly);

        Assert.Contains(publicMethods, method => method.Name == nameof(AiRequestClassification.CreateSuccess) && method.IsStatic);
        Assert.Contains(publicMethods, method => method.Name == nameof(AiRequestClassification.CreateFailure) && method.IsStatic);
        Assert.DoesNotContain(publicMethods, method => method.Name is "SetStatus" or "ChangeStatus" or "UpdateStatus" or "RecordFailure" or "OverrideClassification");
        Assert.DoesNotContain(publicMethods, method => method.GetParameters().Any(parameter =>
            parameter.ParameterType == typeof(AiRunStatus) || parameter.ParameterType == typeof(AiReviewDecision)));
    }

    [Fact]
    public void AiRequestClassification_ReviewDecisions_ExposeOnlySpecSupportedPaths()
    {
        var classification = NewSuccessfulClassification();

        var confirmReview = classification.ConfirmSuggestion(Guid.NewGuid(), _now.AddMinutes(1));
        Assert.Equal(AiReviewDecision.CONFIRMED, confirmReview.Decision);

        var correctedClassification = NewSuccessfulClassification();
        var correctedReview = correctedClassification.CorrectClassification(Guid.NewGuid(), Guid.NewGuid(), _now.AddMinutes(1));
        Assert.Equal(AiReviewDecision.CORRECTED, correctedReview.Decision);

        var publicMethodNames = typeof(AiRequestClassification)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .ToList();

        Assert.DoesNotContain("RejectClassification", publicMethodNames);
        Assert.DoesNotContain("OverrideClassification", publicMethodNames);
    }

    private AiRequestClassification NewSuccessfulClassification(Guid? predictedCategoryId = null, decimal? confidenceScore = null)
    {
        return AiRequestClassification.CreateSuccess(
            Guid.NewGuid(),
            1,
            "OpenAI-compatible",
            "classification-model",
            predictedCategoryId ?? Guid.NewGuid(),
            _now,
            confidenceScore,
            reasoningSummary: "Suggested based on request description",
            promptVersion: "prompt-v1",
            inputHash: "hash",
            inputTokens: 10,
            outputTokens: 5,
            totalTokens: 15,
            latencyMs: 250);
    }
}
