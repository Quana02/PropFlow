namespace PropFlow.Modules.Residents.Contracts;

public sealed record RegistrationResidentCandidate(
    Guid Id,
    Guid? ApartmentUnitId,
    string NormalizedEmail,
    string NormalizedPhone,
    string IdentityType,
    string NormalizedIdentityNumber);

public static class ResidentRegistrationNormalization
{
    public static string Email(string value) => value.Trim().ToLowerInvariant();
    public static string Phone(string value) => value.Trim();
    public static string IdentityType(string value) => value.Trim().ToUpperInvariant();
    public static string IdentityNumber(string value) => new(value.Where(character => character is >= '0' and <= '9').ToArray());
}

public interface IResidentOnboarding
{
    Task<RegistrationResidentCandidate?> FindRegistrationCandidateAsync(
        string normalizedEmail,
        string normalizedPhone,
        string identityType,
        string normalizedIdentityNumber,
        DateOnly today,
        CancellationToken ct);
    Task<RegistrationResidentCandidate?> RevalidateRegistrationCandidateAsync(Guid residentId, DateOnly today, CancellationToken ct);
    Task<bool> LinkEligibleAsync(RegistrationResidentCandidate candidate, Guid userId, DateOnly today, DateTimeOffset now, CancellationToken ct);
}
