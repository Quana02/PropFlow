namespace PropFlow.Modules.Apartments.Domain.ApartmentUnits;

public class ApartmentUnit
{
    private ApartmentUnit()
    {
        // Parameterless constructor for EF Core
    }

    public ApartmentUnit(
        
        string unitNumber,
        int floorNumber,
        DateTimeOffset now,
        Guid apartmentUnitTypeId,
        decimal usableAreaM2 = 1,
        int? bedroomCount = null,
        int? bathroomCount = null,
        DateOnly? handoverDate = null,
        string? description = null,
        Guid? createdBy = null)
    {
        

        ArgumentException.ThrowIfNullOrWhiteSpace(unitNumber);

        Id = Guid.NewGuid();
        
        UnitNumber = unitNumber.Trim();
        FloorNumber = floorNumber;
        if (apartmentUnitTypeId == Guid.Empty) throw new ArgumentException("Apartment unit type is required.", nameof(apartmentUnitTypeId));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(usableAreaM2, 0);
        ArgumentOutOfRangeException.ThrowIfNegative(floorNumber);
        if (bedroomCount is < 0) throw new ArgumentOutOfRangeException(nameof(bedroomCount));
        if (bathroomCount is < 0) throw new ArgumentOutOfRangeException(nameof(bathroomCount));

        ApartmentUnitTypeId = apartmentUnitTypeId;
        UsableAreaM2 = usableAreaM2;
        BedroomCount = bedroomCount;
        BathroomCount = bathroomCount;
        HandoverDate = handoverDate;
        Description = description?.Trim();
        Status = MasterDataStatus.ACTIVE;
        CreatedBy = createdBy;
        UpdatedBy = createdBy;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    

    public string UnitNumber { get; private set; } = null!;
    public int FloorNumber { get; private set; }
    public Guid ApartmentUnitTypeId { get; private set; }
    public ApartmentUnitType ApartmentUnitType { get; private set; } = null!;
    public decimal UsableAreaM2 { get; private set; }
    public int? BedroomCount { get; private set; }
    public int? BathroomCount { get; private set; }
    public DateOnly? HandoverDate { get; private set; }
    public string? Description { get; private set; }
    public MasterDataStatus Status { get; private set; } = MasterDataStatus.ACTIVE;
    public Guid? CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(
        string unitNumber,
        int floorNumber,
        Guid apartmentUnitTypeId,
        decimal usableAreaM2,
        int? bedroomCount,
        int? bathroomCount,
        DateOnly? handoverDate,
        string? description,
        Guid? updatedBy,
        DateTimeOffset now)
    {
        FloorNumber = floorNumber;
        ArgumentException.ThrowIfNullOrWhiteSpace(unitNumber);
        if (apartmentUnitTypeId == Guid.Empty) throw new ArgumentException("Apartment unit type is required.", nameof(apartmentUnitTypeId));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(usableAreaM2, 0);
        ArgumentOutOfRangeException.ThrowIfNegative(floorNumber);
        if (bedroomCount is < 0) throw new ArgumentOutOfRangeException(nameof(bedroomCount));
        if (bathroomCount is < 0) throw new ArgumentOutOfRangeException(nameof(bathroomCount));

        UnitNumber = unitNumber.Trim();
        ApartmentUnitTypeId = apartmentUnitTypeId;
        UsableAreaM2 = usableAreaM2;
        BedroomCount = bedroomCount;
        BathroomCount = bathroomCount;
        HandoverDate = handoverDate;
        Description = description?.Trim();
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void Deactivate(Guid? updatedBy, DateTimeOffset now)
    {
        Status = MasterDataStatus.INACTIVE;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void Activate(Guid? updatedBy, DateTimeOffset now)
    {
        Status = MasterDataStatus.ACTIVE;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }
}
