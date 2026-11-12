namespace PropFlow.Modules.AiClassification.Application.ServiceRequestClassification;

public sealed record ClassificationCategory(string Code, string Name, string? Description);

public sealed record ClassificationPrediction(string? CategoryCode, string? Reason, int? InputTokens, int? OutputTokens, int? TotalTokens);

public interface IServiceRequestClassifier
{
    bool Ready { get; }
    string Model { get; }
    Task<ClassificationPrediction?> ClassifyAsync(string title, string description,
        IReadOnlyList<ClassificationCategory> categories, CancellationToken ct);
}
