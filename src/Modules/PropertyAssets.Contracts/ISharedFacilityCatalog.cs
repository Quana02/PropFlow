namespace PropFlow.Modules.PropertyAssets.Contracts;

// Task 2 scope extension: narrow, read-only catalogue for shared-facility recommendations.
// A facility still requires an explicit resident-facing usage profile before publication.
public sealed record SharedFacilityReference(Guid Id, string Name, string? Type, string? Location,
    string Status, DateTimeOffset UpdatedAt);

public interface ISharedFacilityCatalog
{
    Task<IReadOnlyList<SharedFacilityReference>> ListAsync(CancellationToken ct);
    Task<SharedFacilityReference?> FindAsync(Guid facilityId, CancellationToken ct);
}
