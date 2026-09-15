using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;

namespace PropFlow.Modules.PropertyAssets.Domain.Equipment;

public class Equipment
{
    private Equipment()
    {
        // Parameterless constructor for EF Core
    }

    public Equipment(
        Guid buildingId,
        string code,
        string name,
        DateTimeOffset now,
        Guid? facilityId = null,
        string? equipmentType = null,
        string? manufacturer = null,
        string? model = null,
        string? serialNumber = null,
        DateOnly? installationDate = null,
        DateOnly? warrantyExpiryDate = null,
        string? locationDescription = null,
        string? description = null,
        Guid? createdBy = null)
    {
        if (buildingId == Guid.Empty)
        {
            throw new ArgumentException("BuildingId cannot be empty.", nameof(buildingId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = Guid.NewGuid();
        BuildingId = buildingId;
        FacilityId = facilityId;
        Code = code.Trim();
        Name = name.Trim();
        EquipmentType = equipmentType?.Trim();
        Manufacturer = manufacturer?.Trim();
        Model = model?.Trim();
        SerialNumber = serialNumber?.Trim();
        InstallationDate = installationDate;
        WarrantyExpiryDate = warrantyExpiryDate;
        LocationDescription = locationDescription?.Trim();
        Status = EquipmentStatus.ACTIVE;
        Description = description?.Trim();
        CreatedBy = createdBy;
        UpdatedBy = createdBy;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid BuildingId { get; private set; }
    public Guid? FacilityId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? EquipmentType { get; private set; }
    public string? Manufacturer { get; private set; }
    public string? Model { get; private set; }
    public string? SerialNumber { get; private set; }
    public DateOnly? InstallationDate { get; private set; }
    public DateOnly? WarrantyExpiryDate { get; private set; }
    public string? LocationDescription { get; private set; }
    public EquipmentStatus Status { get; private set; } = EquipmentStatus.ACTIVE;
    public string? Description { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Within-module navigation
    public Building? Building { get; private set; }
    public Facility? Facility { get; private set; }

    public void Update(
        string name,
        Guid? facilityId,
        string? equipmentType,
        string? manufacturer,
        string? model,
        string? serialNumber,
        DateOnly? installationDate,
        DateOnly? warrantyExpiryDate,
        string? locationDescription,
        string? description,
        Guid? updatedBy,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        FacilityId = facilityId;
        EquipmentType = equipmentType?.Trim();
        Manufacturer = manufacturer?.Trim();
        Model = model?.Trim();
        SerialNumber = serialNumber?.Trim();
        InstallationDate = installationDate;
        WarrantyExpiryDate = warrantyExpiryDate;
        LocationDescription = locationDescription?.Trim();
        Description = description?.Trim();
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void MarkUnderMaintenance(Guid? updatedBy, DateTimeOffset now)
    {
        if (Status == EquipmentStatus.OUT_OF_SERVICE)
        {
            throw new InvalidOperationException("Cannot place out of service equipment under maintenance directly.");
        }

        Status = EquipmentStatus.UNDER_MAINTENANCE;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void MarkActive(Guid? updatedBy, DateTimeOffset now)
    {
        Status = EquipmentStatus.ACTIVE;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void MarkOutOfService(Guid? updatedBy, DateTimeOffset now)
    {
        Status = EquipmentStatus.OUT_OF_SERVICE;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void Deactivate(Guid? updatedBy, DateTimeOffset now)
    {
        Status = EquipmentStatus.INACTIVE;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }
}
