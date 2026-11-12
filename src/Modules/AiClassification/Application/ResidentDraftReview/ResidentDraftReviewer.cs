namespace PropFlow.Modules.AiClassification.Application.ResidentDraftReview;

public sealed record ResidentDraftPrediction(IReadOnlyList<string> MissingFields, DateOnly? PreferredDate, string? PreferredTimeCode);

public interface IResidentDraftReviewer
{
    bool Ready { get; }
    Task<ResidentDraftPrediction?> CheckAsync(string description, DateOnly today, CancellationToken ct);
}
