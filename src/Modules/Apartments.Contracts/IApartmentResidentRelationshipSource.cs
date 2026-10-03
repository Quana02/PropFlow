namespace PropFlow.Modules.Apartments.Contracts;

/// <summary>Public read contract used by other modules to describe apartment relationships.</summary>
public interface IApartmentResidentRelationshipSource
{
    Task<IReadOnlyList<ApartmentReference>> GetApartmentsAsync(
        IReadOnlyCollection<Guid> apartmentUnitIds,
        CancellationToken ct);

    Task<IReadOnlyList<ResidentApartmentOwnership>> GetOwnershipsAsync(
        IReadOnlyCollection<Guid> residentIds,
        CancellationToken ct);

    Task<IReadOnlyList<Guid>> GetCurrentOwnerResidentIdsAsync(
        Guid apartmentUnitId,
        DateOnly date,
        CancellationToken ct);

    Task<IReadOnlyList<Guid>> GetCurrentOwnerResidentIdsAsync(
        DateOnly date,
        CancellationToken ct);
}

public sealed record ApartmentReference(Guid Id, string UnitNumber, int FloorNumber);

public sealed record ResidentApartmentOwnership(
    Guid Id,
    Guid ResidentId,
    Guid ApartmentUnitId,
    string UnitNumber,
    int FloorNumber,
    DateOnly StartDate,
    DateOnly? EndDate);
