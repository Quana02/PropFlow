namespace PropFlow.Modules.ServiceRequests.Contracts;

public sealed record CheckResidentServiceRequestDraft(string Description);

public sealed record ResidentDraftMissingInformation(string Code, string Message);

public sealed record ResidentServiceRequestDraftReview(
    bool Available,
    IReadOnlyList<ResidentDraftMissingInformation> MissingInformation,
    DateOnly? PreferredDate = null,
    string? PreferredTimeCode = null,
    string? Notice = null);
