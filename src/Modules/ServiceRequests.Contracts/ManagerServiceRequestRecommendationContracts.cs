namespace PropFlow.Modules.ServiceRequests.Contracts;

public static class ManagerRecommendationPriorities
{
    public static IReadOnlyList<string> Codes { get; } = Array.AsReadOnly(new[] { "NORMAL", "HIGH", "URGENT" });
}

public sealed record ServiceRequestHandlingPlan(string PriorityCode, bool? MaintenanceRequired, string? Action, string? Resources);

public sealed record ManagerRecommendationSuggestion(Guid Id, string Status, ServiceRequestHandlingPlan? Proposed,
    string? Reason, string? Failure, string? ReviewDecision, ServiceRequestHandlingPlan? Approved, string? ReviewNote);

public sealed record ManagerServiceRequestRecommendationView(bool CanAnalyze, bool CanReview, bool CanSetPriority,
    string? UnavailableReason, string? CurrentPriority, ManagerRecommendationSuggestion? Suggestion);

public sealed record ReviewManagerServiceRequestRecommendation(Guid? RecommendationId, string PriorityCode,
    bool? MaintenanceRequired = null, string? Action = null, string? Resources = null, string? ReviewNote = null);

public interface IAssignedServiceRequestRecommendationSource
{
    Task<ServiceRequestHandlingPlan?> GetApprovedAsync(Guid serviceRequestId, string? currentPriority, CancellationToken ct);
}
