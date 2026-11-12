namespace PropFlow.Modules.Residents.Contracts;

public sealed record ActiveResidentResidence(
    Guid ResidentId,
    Guid ResidentApartmentId,
    Guid ApartmentUnitId);

public interface IResidentResidenceSource
{
    Task<ActiveResidentResidence?> FindActiveAsync(
        Guid userId,
        DateOnly date,
        CancellationToken ct);
}
