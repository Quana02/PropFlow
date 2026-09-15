namespace PropFlow.Modules.Apartments.Domain.ApartmentUnits;

public class ApartmentUnit
{
    private ApartmentUnit()
    {
        // Parameterless constructor for EF Core
    }

    public ApartmentUnit(
        Guid buildingId,
        string unitNumber,
        int floorNumber,
        DateTimeOffset now,
        decimal? areaM2 = null,
        int? bedroomCount = null,
        string? description = null,
        Guid? createdBy = null)
    {
        if (buildingId == Guid.Empty)
        {
            throw new ArgumentException("BuildingId cannot be empty.", nameof(buildingId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(unitNumber);

        Id = Guid.NewGuid();
        BuildingId = buildingId;
        UnitNumber = unitNumber.Trim();
        FloorNumber = floorNumber;
        AreaM2 = areaM2;
        BedroomCount = bedroomCount;
        Description = description?.Trim();
        Status = MasterDataStatus.ACTIVE;
        CreatedBy = createdBy;
        UpdatedBy = createdBy;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    // Cross-module scalar ID to property_assets.buildings.id
    public Guid BuildingId { get; private set; }

    public string UnitNumber { get; private set; } = null!;
    public int FloorNumber { get; private set; }
    public decimal? AreaM2 { get; private set; }
    public int? BedroomCount { get; private set; }
    public string? Description { get; private set; }
    public MasterDataStatus Status { get; private set; } = MasterDataStatus.ACTIVE;
    public Guid? CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(
        int floorNumber,
        decimal? areaM2,
        int? bedroomCount,
        string? description,
        Guid? updatedBy,
        DateTimeOffset now)
    {
        FloorNumber = floorNumber;
        AreaM2 = areaM2;
        BedroomCount = bedroomCount;
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
