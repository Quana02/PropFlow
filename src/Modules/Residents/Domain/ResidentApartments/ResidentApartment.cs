using PropFlow.Modules.Residents.Domain.Residents;

namespace PropFlow.Modules.Residents.Domain.ResidentApartments;

public class ResidentApartment
{
    private ResidentApartment()
    {
        // Parameterless constructor for EF Core
    }

    public ResidentApartment(
        Guid residentId,
        Guid apartmentUnitId,
        string relationshipTypeCode,
        DateOnly startDate,
        DateTimeOffset now,
        bool isPrimary = false,
        DateOnly? endDate = null,
        string? note = null,
        Guid? createdBy = null)
    {
        if (residentId == Guid.Empty)
        {
            throw new ArgumentException("ResidentId cannot be empty.", nameof(residentId));
        }

        if (apartmentUnitId == Guid.Empty)
        {
            throw new ArgumentException("ApartmentUnitId cannot be empty.", nameof(apartmentUnitId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(relationshipTypeCode);

        if (endDate.HasValue && endDate.Value < startDate)
        {
            throw new ArgumentException("EndDate cannot be before StartDate.", nameof(endDate));
        }

        Id = Guid.NewGuid();
        ResidentId = residentId;
        ApartmentUnitId = apartmentUnitId;
        RelationshipTypeCode = relationshipTypeCode.Trim();
        IsPrimary = isPrimary;
        StartDate = startDate;
        EndDate = endDate;
        Status = ResidencyStatus.ACTIVE;
        Note = note?.Trim();
        CreatedBy = createdBy;
        UpdatedBy = createdBy;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ResidentId { get; private set; }

    // Cross-module scalar ID to apartments.apartment_units.id
    public Guid ApartmentUnitId { get; private set; }

    public string RelationshipTypeCode { get; private set; } = null!;
    public bool IsPrimary { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public ResidencyStatus Status { get; private set; } = ResidencyStatus.ACTIVE;
    public string? Note { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Within-module navigation
    public Resident? Resident { get; private set; }

    public bool IsActiveAt(DateOnly date) => Status == ResidencyStatus.ACTIVE && StartDate <= date && (!EndDate.HasValue || EndDate.Value >= date);

    public void EndResidency(DateOnly endDate, Guid? updatedBy, DateTimeOffset now)
    {
        if (endDate < StartDate)
        {
            throw new ArgumentException("EndDate cannot be before StartDate.", nameof(endDate));
        }

        EndDate = endDate;
        Status = ResidencyStatus.ENDED;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void SetPrimary(bool isPrimary, Guid? updatedBy, DateTimeOffset now)
    {
        IsPrimary = isPrimary;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void UpdateRelationship(string relationshipTypeCode, string? note, Guid? updatedBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relationshipTypeCode);

        RelationshipTypeCode = relationshipTypeCode.Trim();
        Note = note?.Trim();
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }
}
