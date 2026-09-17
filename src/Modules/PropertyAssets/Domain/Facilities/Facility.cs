using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using EquipmentEntity = PropFlow.Modules.PropertyAssets.Domain.Equipment.Equipment;

namespace PropFlow.Modules.PropertyAssets.Domain.Facilities;

public class Facility
{
    private readonly List<EquipmentEntity> _equipment = [];

    private Facility()
    {
        // Parameterless constructor for EF Core
    }

    public Facility(
        Guid buildingId,
        string code,
        string name,
        DateTimeOffset now,
        string? facilityType = null,
        string? locationDescription = null,
        string? description = null,
        Guid? createdBy = null,
        MasterDataStatus initialStatus = MasterDataStatus.ACTIVE)
    {
        if (buildingId == Guid.Empty)
        {
            throw new ArgumentException("BuildingId cannot be empty.", nameof(buildingId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = Guid.NewGuid();
        BuildingId = buildingId;
        Code = code.Trim();
        Name = name.Trim();
        FacilityType = facilityType?.Trim();
        LocationDescription = locationDescription?.Trim();
        Description = description?.Trim();
        Status = initialStatus;
        CreatedBy = createdBy;
        UpdatedBy = createdBy;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid BuildingId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? FacilityType { get; private set; }
    public string? LocationDescription { get; private set; }
    public string? Description { get; private set; }
    public MasterDataStatus Status { get; private set; } = MasterDataStatus.ACTIVE;
    public Guid? CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Within-module navigation
    public Building? Building { get; private set; }
    public IReadOnlyCollection<EquipmentEntity> Equipment => _equipment.AsReadOnly();

    public void Update(
        Guid buildingId,
        string name,
        string? facilityType,
        string? locationDescription,
        string? description,
        MasterDataStatus? status,
        Guid? updatedBy,
        DateTimeOffset now)
    {
        if (buildingId == Guid.Empty)
        {
            throw new ArgumentException("BuildingId cannot be empty.", nameof(buildingId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        BuildingId = buildingId;
        Name = name.Trim();
        FacilityType = facilityType?.Trim();
        LocationDescription = locationDescription?.Trim();
        Description = description?.Trim();
        if (status.HasValue)
        {
            Status = status.Value;
        }
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void SetStatus(MasterDataStatus status, Guid? updatedBy, DateTimeOffset now)
    {
        Status = status;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }
}
