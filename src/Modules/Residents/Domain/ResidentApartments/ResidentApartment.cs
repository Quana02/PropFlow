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
        HouseholdRole householdRole,
        ResidencyType residencyType,
        DateOnly startDate,
        DateTimeOffset now,
        Guid? householdHeadResidencyId = null,
        HouseholdRelationship? relationshipToHead = null,
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

        if (endDate.HasValue && endDate.Value < startDate)
        {
            throw new ArgumentException("EndDate cannot be before StartDate.", nameof(endDate));
        }
        ValidateHousehold(householdRole, householdHeadResidencyId, relationshipToHead);

        Id = Guid.NewGuid();
        ResidentId = residentId;
        ApartmentUnitId = apartmentUnitId;
        HouseholdRole = householdRole;
        ResidencyType = residencyType;
        HouseholdHeadResidencyId = householdHeadResidencyId;
        RelationshipToHead = relationshipToHead;
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

    public HouseholdRole HouseholdRole { get; private set; }
    public ResidencyType ResidencyType { get; private set; }
    // Self-reference inside Residents: the active head residency for a household member.
    public Guid? HouseholdHeadResidencyId { get; private set; }
    public HouseholdRelationship? RelationshipToHead { get; private set; }
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
    public ResidentApartment? HouseholdHeadResidency { get; private set; }

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

    public void UpdateHousehold(HouseholdRole householdRole, ResidencyType residencyType, Guid? householdHeadResidencyId,
        HouseholdRelationship? relationshipToHead, Guid? updatedBy, DateTimeOffset now)
    {
        ValidateHousehold(householdRole, householdHeadResidencyId, relationshipToHead);
        HouseholdRole = householdRole;
        ResidencyType = residencyType;
        HouseholdHeadResidencyId = householdHeadResidencyId;
        RelationshipToHead = relationshipToHead;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void UpdateNote(string? note, Guid? updatedBy, DateTimeOffset now)
    {
        Note = note?.Trim();
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    private static void ValidateHousehold(HouseholdRole role, Guid? headResidencyId, HouseholdRelationship? relationship)
    {
        if (role == HouseholdRole.HOUSEHOLD_MEMBER && (!headResidencyId.HasValue || !relationship.HasValue))
            throw new ArgumentException("Household members require both a household head and relationship.");
        if (role != HouseholdRole.HOUSEHOLD_MEMBER && (headResidencyId.HasValue || relationship.HasValue))
            throw new ArgumentException("Only household members can reference a household head.");
    }
}
