namespace PropFlow.Modules.Residents.Contracts;

/// <summary>Public read contract used by Apartments. It never exposes Residents persistence types.</summary>
public interface IResidentApartmentReadSource
{
    Task<bool> HasActiveResidenciesAsync(Guid apartmentUnitId, DateOnly date, CancellationToken cancellationToken = default);
    Task<bool> HasActiveOwnerOccupiedResidencyAsync(Guid residentId, Guid apartmentUnitId, DateOnly date, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ResidentLookupItem>> SearchResidentsAsync(string? search, int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, ResidentLookupItem>> GetResidentsAsync(IReadOnlyCollection<Guid> residentIds, CancellationToken cancellationToken = default);
    Task<bool> ResidentExistsAsync(Guid residentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<ActiveResidentAssociation>>> GetActiveAssociationsAsync(IReadOnlyCollection<Guid> apartmentUnitIds, DateOnly date, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<ResidentAssociationItem>>> GetAssociationsAsync(IReadOnlyCollection<Guid> apartmentUnitIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> GetActiveApartmentIdsAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> GetActiveApartmentIdsForUserAsync(Guid userId, DateOnly date, CancellationToken cancellationToken = default);
}

public sealed record ResidentLookupItem(Guid ResidentId, string ResidentCode, string FullName);
public sealed record ActiveResidentAssociation(Guid ResidentId, string ResidentCode, string FullName, string HouseholdRole, string ResidencyType, string? RelationshipToHead, DateOnly StartDate, string ResidencyStatus);
public sealed record ResidentAssociationItem(Guid ResidencyId, Guid ResidentId, string ResidentCode, string FullName,
    string HouseholdRole, string ResidencyType, Guid? HouseholdHeadResidencyId, string? HouseholdHeadName,
    string? HouseholdHeadResidentCode, string? RelationshipToHead, DateOnly StartDate, DateOnly? EndDate, string ResidencyStatus);
