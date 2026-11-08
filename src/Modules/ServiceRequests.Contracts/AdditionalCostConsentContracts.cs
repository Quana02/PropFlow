namespace PropFlow.Modules.ServiceRequests.Contracts;

public sealed record AdditionalCostConsentView(Guid ProposalId, decimal Amount, string? Note,
    string Status, DateTimeOffset? ProposedAt, DateTimeOffset? RespondedAt);
public sealed record ProposeAdditionalCostRequest(decimal Amount, string Note, Guid? ExpectedProposalId = null);
public sealed record RespondAdditionalCostRequest(Guid ProposalId, bool Accepted);
