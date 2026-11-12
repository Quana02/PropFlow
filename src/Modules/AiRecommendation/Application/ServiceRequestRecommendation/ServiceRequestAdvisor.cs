namespace PropFlow.Modules.AiRecommendation.Application.ServiceRequestRecommendation;

public sealed record RecommendationInput(string Title, string Description, string? Category, string? Facility, string? Equipment);

public sealed record RecommendationPrediction(string? PriorityCode, bool? MaintenanceRecommended, string? Action,
    string? Resources, string? Reason, int? InputTokens, int? OutputTokens, int? TotalTokens);

public interface IServiceRequestAdvisor
{
    bool Ready { get; }
    string Model { get; }
    Task<RecommendationPrediction?> RecommendAsync(RecommendationInput input, IReadOnlyList<string> priorities, CancellationToken ct);
}
