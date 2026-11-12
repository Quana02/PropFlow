namespace PropFlow.Modules.Authentication.Contracts;

public sealed record InternalAccountProfile(Guid Id, string Username, string DisplayName, string? Email,
    string? PhoneNumber, string Status, bool EmailVerified);

public sealed record InternalAccountProfileUpdate(InternalAccountProfile Account, IReadOnlyList<string> ChangedFields);

// Administration checks the administrative scope before accessing another account.
public interface IInternalAccountProfiles
{
    Task<InternalAccountProfile?> ReadAsync(Guid userId, CancellationToken ct);
    Task<InternalAccountProfileUpdate?> UpdateAsync(Guid userId, UpdateProfileRequest request, Guid actorId, CancellationToken ct);
}
