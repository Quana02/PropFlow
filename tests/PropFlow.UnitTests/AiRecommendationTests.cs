using PropFlow.Modules.AiRecommendation.Domain.Recommendations;

namespace PropFlow.UnitTests;

public class AiRecommendationTests
{
    private readonly DateTimeOffset _now = new(2026, 9, 15, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AiRequestRecommendation_CreateSuccess_RecordsImmutableSuccessfulResult()
    {
        var recommendation = NewSuccessfulRecommendation(" urgent ");

        Assert.NotEqual(Guid.Empty, recommendation.Id);
        Assert.Equal(AiRunStatus.SUCCESS, recommendation.RunStatus);
        Assert.Equal("URGENT", recommendation.SuggestedPriorityCode);
        Assert.True(recommendation.MaintenanceRecommended);
        Assert.Equal("Inspect water leakage", recommendation.RecommendedAction);
        Assert.Equal("Plumbing team capability", recommendation.RecommendedResourceSummary);
        Assert.Equal("{\"capability\":\"PLUMBING\"}", recommendation.RecommendedResourcesJson);
        Assert.Equal("PROMPT-V2", recommendation.PromptVersion);
        Assert.Null(recommendation.ErrorMessage);
        Assert.Equal(_now, recommendation.CreatedAt);
    }

    [Fact]
    public void AiRequestRecommendation_CreateSuccess_ValidatesRequiredFieldsAndTelemetry()
    {
        Assert.Throws<ArgumentException>(() => AiRequestRecommendation.CreateSuccess(Guid.Empty, 1, "Provider", "Model", "URGENT", _now));
        Assert.Throws<ArgumentOutOfRangeException>(() => AiRequestRecommendation.CreateSuccess(Guid.NewGuid(), 0, "Provider", "Model", "URGENT", _now));
        Assert.Throws<ArgumentException>(() => AiRequestRecommendation.CreateSuccess(Guid.NewGuid(), 1, "", "Model", "URGENT", _now));
        Assert.Throws<ArgumentException>(() => AiRequestRecommendation.CreateSuccess(Guid.NewGuid(), 1, "Provider", "", "URGENT", _now));
        Assert.Throws<ArgumentException>(() => AiRequestRecommendation.CreateSuccess(Guid.NewGuid(), 1, "Provider", "Model", "", _now));
        Assert.Throws<ArgumentOutOfRangeException>(() => AiRequestRecommendation.CreateSuccess(Guid.NewGuid(), 1, "Provider", "Model", "URGENT", _now, inputTokens: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AiRequestRecommendation.CreateSuccess(Guid.NewGuid(), 1, "Provider", "Model", "URGENT", _now, latencyMs: -1));
    }

    [Fact]
    public void AiRequestRecommendation_CreateFailure_CapturesFailureWithoutFakeOutputs()
    {
        var recommendation = AiRequestRecommendation.CreateFailure(
            Guid.NewGuid(),
            1,
            "Provider",
            "Model",
            "Provider timeout",
            _now,
            inputHash: "abc123",
            latencyMs: 1200);

        Assert.Equal(AiRunStatus.FAILED, recommendation.RunStatus);
        Assert.Equal("Provider timeout", recommendation.ErrorMessage);
        Assert.Null(recommendation.SuggestedPriorityCode);
        Assert.Null(recommendation.MaintenanceRecommended);
        Assert.Null(recommendation.RecommendedAction);
        Assert.Null(recommendation.RecommendedResourceSummary);
        Assert.Null(recommendation.RecommendedResourcesJson);
        Assert.Null(recommendation.ReasoningSummary);
        Assert.Throws<ArgumentException>(() => AiRequestRecommendation.CreateFailure(Guid.NewGuid(), 1, "Provider", "Model", "", _now));
    }

    [Fact]
    public void AiRequestRecommendation_ConfirmSuggestion_CreatesFinalDecisionFromSuggestion()
    {
        var recommendation = NewSuccessfulRecommendation();
        var managerId = Guid.NewGuid();
        var reviewedAt = _now.AddMinutes(5);

        var review = recommendation.ConfirmSuggestion(managerId, reviewedAt, "Looks right");

        Assert.Equal(AiReviewDecision.CONFIRMED, review.Decision);
        Assert.Equal("URGENT", review.FinalPriorityCode);
        Assert.True(review.FinalMaintenanceRequired);
        Assert.Equal("Inspect water leakage", review.FinalAction);
        Assert.Equal("Plumbing team capability", review.FinalResourcePlan);
        Assert.Equal(managerId, review.ReviewedBy);
        Assert.Equal(reviewedAt, review.ReviewedAt);
        Assert.Same(review, recommendation.Review);
    }

    [Fact]
    public void AiRequestRecommendation_CorrectRecommendation_PreservesAiSuggestionAndStoresFinalValues()
    {
        var recommendation = NewSuccessfulRecommendation();

        var review = recommendation.CorrectRecommendation(
            "normal",
            Guid.NewGuid(),
            _now.AddMinutes(5),
            "Less urgent after manager review",
            finalMaintenanceRequired: false,
            finalAction: "Handle during normal operations",
            finalResourcePlan: "General operations team");

        Assert.Equal(AiReviewDecision.CORRECTED, review.Decision);
        Assert.Equal("NORMAL", review.FinalPriorityCode);
        Assert.False(review.FinalMaintenanceRequired);
        Assert.Equal("Handle during normal operations", review.FinalAction);
        Assert.Equal("General operations team", review.FinalResourcePlan);
        Assert.Equal("Less urgent after manager review", review.ReviewNote);
        Assert.Equal("URGENT", recommendation.SuggestedPriorityCode);
        Assert.True(recommendation.MaintenanceRecommended);
        Assert.Equal("Inspect water leakage", recommendation.RecommendedAction);
    }

    [Fact]
    public void AiRequestRecommendation_ReviewRules_RejectFailedAlreadyReviewedOrInvalidReview()
    {
        var failed = AiRequestRecommendation.CreateFailure(Guid.NewGuid(), 1, "Provider", "Model", "Failed", _now);
        Assert.Throws<InvalidOperationException>(() => failed.ConfirmSuggestion(Guid.NewGuid(), _now));

        var recommendation = NewSuccessfulRecommendation();
        Assert.Throws<ArgumentOutOfRangeException>(() => recommendation.ConfirmSuggestion(Guid.NewGuid(), _now.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() => recommendation.ConfirmSuggestion(Guid.Empty, _now));
        Assert.Throws<ArgumentException>(() => recommendation.CorrectRecommendation("", Guid.NewGuid(), _now, "Reason"));
        Assert.Throws<ArgumentException>(() => recommendation.CorrectRecommendation("NORMAL", Guid.Empty, _now, "Reason"));
        Assert.Throws<ArgumentException>(() => recommendation.CorrectRecommendation("NORMAL", Guid.NewGuid(), _now, ""));
        Assert.Throws<InvalidOperationException>(() => recommendation.CorrectRecommendation("URGENT", Guid.NewGuid(), _now, "Same result", true, "Inspect water leakage", "Plumbing team capability"));

        recommendation.ConfirmSuggestion(Guid.NewGuid(), _now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => recommendation.ConfirmSuggestion(Guid.NewGuid(), _now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => recommendation.CorrectRecommendation("NORMAL", Guid.NewGuid(), _now.AddMinutes(2), "Second review"));
    }

    [Fact]
    public void AiRequestRecommendation_PublicApi_DoesNotExposeStatusTransitionsOrGenericReview()
    {
        Assert.Empty(typeof(AiRequestRecommendation).GetConstructors());

        var publicMethods = typeof(AiRequestRecommendation)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly);

        Assert.Contains(publicMethods, method => method.Name == nameof(AiRequestRecommendation.CreateSuccess) && method.IsStatic);
        Assert.Contains(publicMethods, method => method.Name == nameof(AiRequestRecommendation.CreateFailure) && method.IsStatic);
        Assert.DoesNotContain(publicMethods, method => method.Name is "SetStatus" or "ChangeStatus" or "UpdateStatus" or "RecordFailure" or "OverrideRecommendation" or "RejectRecommendation");
        Assert.DoesNotContain(publicMethods, method => method.GetParameters().Any(parameter =>
            parameter.ParameterType == typeof(AiRunStatus) || parameter.ParameterType == typeof(AiReviewDecision)));
    }

    [Fact]
    public void AiRequestRecommendation_ReviewDecisions_ExposeOnlySpecSupportedPaths()
    {
        var confirmation = NewSuccessfulRecommendation().ConfirmSuggestion(Guid.NewGuid(), _now.AddMinutes(1));
        Assert.Equal(AiReviewDecision.CONFIRMED, confirmation.Decision);

        var correction = NewSuccessfulRecommendation().CorrectRecommendation("NORMAL", Guid.NewGuid(), _now.AddMinutes(1), "Manager correction");
        Assert.Equal(AiReviewDecision.CORRECTED, correction.Decision);

        var publicMethodNames = typeof(AiRequestRecommendation)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .ToList();

        Assert.DoesNotContain("RejectRecommendation", publicMethodNames);
        Assert.DoesNotContain("OverrideRecommendation", publicMethodNames);
    }

    private AiRequestRecommendation NewSuccessfulRecommendation(string suggestedPriorityCode = "URGENT")
    {
        return AiRequestRecommendation.CreateSuccess(
            Guid.NewGuid(),
            1,
            "OpenAI-compatible",
            "recommendation-model",
            suggestedPriorityCode,
            _now,
            maintenanceRecommended: true,
            recommendedAction: "Inspect water leakage",
            recommendedResourceSummary: "Plumbing team capability",
            recommendedResourcesJson: "{\"capability\":\"PLUMBING\"}",
            reasoningSummary: "Water leak keywords indicate urgent operational handling",
            promptVersion: "prompt-v2",
            inputHash: "hash",
            inputTokens: 20,
            outputTokens: 8,
            totalTokens: 28,
            latencyMs: 350);
    }
}
