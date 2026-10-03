namespace PropFlow.Modules.ServiceRequests.Contracts;

public sealed record CreateResidentServiceRequestRequest(
    string CategoryCode,
    string Title,
    string Description,
    string ServiceAreaCode,
    string PriorityCode,
    DateOnly? PreferredDate,
    string? PreferredTimeCode);

public sealed record ResidentServiceRequestCreatedResponse(
    Guid Id,
    string RequestNumber,
    string Status,
    DateTimeOffset SubmittedAt);
