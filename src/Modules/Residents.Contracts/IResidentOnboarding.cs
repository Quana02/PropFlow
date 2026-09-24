namespace PropFlow.Modules.Residents.Contracts;

public sealed record EligibleResident(Guid Id, Guid? ApartmentUnitId);
public interface IResidentOnboarding
{
    Task<EligibleResident?> FindEligibleAsync(string normalizedEmail, DateOnly today, CancellationToken ct);
    Task<bool> LinkEligibleAsync(Guid residentId, Guid userId, string normalizedEmail, DateOnly today, DateTimeOffset now, CancellationToken ct);
}
