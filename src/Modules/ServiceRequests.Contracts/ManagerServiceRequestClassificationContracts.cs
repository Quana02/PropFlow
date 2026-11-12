namespace PropFlow.Modules.ServiceRequests.Contracts;

public sealed record ManagerClassificationCategory(Guid Id, string Name);

public sealed record ManagerClassificationSuggestion(Guid Id, string Status, Guid? SuggestedCategoryId,
    string? Reason, string? Failure, string? ReviewDecision, Guid? FinalCategoryId);

public sealed record ManagerServiceRequestClassificationView(bool CanAnalyze, bool CanReview, string? UnavailableReason,
    ManagerClassificationSuggestion? Suggestion, IReadOnlyList<ManagerClassificationCategory> Categories);

public sealed record ReviewManagerServiceRequestClassification(Guid SuggestionId, Guid CategoryId);
