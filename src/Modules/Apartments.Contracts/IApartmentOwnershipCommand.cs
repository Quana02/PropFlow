namespace PropFlow.Modules.Apartments.Contracts;

/// <summary>Business capability for adding an owner. It never transfers or removes other owners.</summary>
public interface IApartmentOwnershipCommand
{
    Task AddOwnerAsync(Guid apartmentUnitId, Guid residentId, DateOnly effectiveDate, Guid? actorId, CancellationToken cancellationToken);
    Task EndOwnershipAsync(Guid apartmentUnitId, Guid ownershipId, DateOnly endDate, Guid? actorId, CancellationToken cancellationToken);
}
