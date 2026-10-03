namespace PropFlow.Modules.Apartments.Domain.ApartmentUnits;

/// <summary>Immutable-in-history ownership period. Owner resident is a cross-module scalar ID.</summary>
public class ApartmentOwnership
{
    private ApartmentOwnership() { }

    public ApartmentOwnership(Guid apartmentUnitId, Guid ownerResidentId, DateOnly startDate, DateTimeOffset now, Guid? actorId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(apartmentUnitId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(ownerResidentId, Guid.Empty);
        Id = Guid.NewGuid(); ApartmentUnitId = apartmentUnitId; OwnerResidentId = ownerResidentId; StartDate = startDate;
        CreatedAt = now; UpdatedAt = now; CreatedBy = actorId; UpdatedBy = actorId;
    }

    public Guid Id { get; private set; }
    public Guid ApartmentUnitId { get; private set; }
    public Guid OwnerResidentId { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsCurrentAt(DateOnly date) => StartDate <= date && EndDate is null;
    public void End(DateOnly endDate, DateTimeOffset now, Guid? actorId)
    {
        if (endDate < StartDate) throw new ArgumentException("Ownership end date cannot precede its start date.", nameof(endDate));
        EndDate = endDate; UpdatedAt = now; UpdatedBy = actorId;
    }
}
