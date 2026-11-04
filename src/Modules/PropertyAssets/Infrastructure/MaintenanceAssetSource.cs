using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Domain.Equipment;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;

namespace PropFlow.Modules.PropertyAssets.Infrastructure;

public sealed class MaintenanceAssetSource(PropertyAssetsDbContext db) : IMaintenanceAssetSource
{
    public async Task<IReadOnlyList<MaintenanceAssetReference>> GetAvailableAsync(CancellationToken ct)
    {
        var facilities = await db.Facilities.AsNoTracking().Select(x => new MaintenanceAssetReference(x.Id, x.Code, x.Name, "FACILITY", x.Status.ToString(), x.LocationDescription, null, x.FacilityType == null ? null : x.FacilityType.Name)).ToListAsync(ct);
        var equipment = await db.Equipment.AsNoTracking().Select(x => new MaintenanceAssetReference(x.Id, x.Code, x.Name, "EQUIPMENT", x.Status.ToString(), x.LocationDescription, x.FacilityId, null)).ToListAsync(ct);
        return facilities.Concat(equipment).OrderBy(x => x.AssetType).ThenBy(x => x.Code).ToArray();
    }

    public Task<MaintenanceAssetReference?> GetFacilityAsync(Guid facilityId, CancellationToken ct) =>
        db.Facilities.AsNoTracking()
            .Where(x => x.Id == facilityId)
            .Select(x => new MaintenanceAssetReference(x.Id, x.Code, x.Name, "FACILITY", x.Status.ToString(), x.LocationDescription, null, x.FacilityType == null ? null : x.FacilityType.Name))
            .SingleOrDefaultAsync(ct);

    public Task<MaintenanceAssetReference?> GetEquipmentAsync(Guid equipmentId, CancellationToken ct) =>
        db.Equipment.AsNoTracking()
            .Where(x => x.Id == equipmentId)
            .Select(x => new MaintenanceAssetReference(x.Id, x.Code, x.Name, "EQUIPMENT", x.Status.ToString(), x.LocationDescription, x.FacilityId, null))
            .SingleOrDefaultAsync(ct);

    public async Task<bool> ExistsAsync(Guid? facilityId, Guid? equipmentId, CancellationToken ct)
    {
        if (facilityId is { } facility && !await db.Facilities.AsNoTracking().AnyAsync(x => x.Id == facility, ct)) return false;
        if (equipmentId is { } equipment && !await db.Equipment.AsNoTracking().AnyAsync(x => x.Id == equipment, ct)) return false;
        return true;
    }

    public async Task MarkUnderMaintenanceAsync(Guid? facilityId, Guid? equipmentId, Guid actor, CancellationToken ct)
    {
        if (facilityId is null && equipmentId is null)
            throw new ArgumentException("Phải chọn cơ sở vật chất hoặc thiết bị.");
        var facility = facilityId is { } selectedFacilityId
            ? await db.Facilities.SingleOrDefaultAsync(item => item.Id == selectedFacilityId, ct) ?? throw new KeyNotFoundException("Không tìm thấy cơ sở vật chất.")
            : null;
        var equipment = equipmentId is { } id
            ? await db.Equipment.SingleOrDefaultAsync(item => item.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy thiết bị.")
            : null;
        if (facility is not null && equipment?.FacilityId is not null && equipment.FacilityId != facilityId)
            throw new ArgumentException("Thiết bị không thuộc cơ sở vật chất đã chọn.");

        var now = DateTimeOffset.UtcNow;
        if (facility is not null && equipment is null)
        {
            // Facility-wide maintenance: affect this facility and its eligible equipment only.
            facility.SetStatus(MasterDataStatus.UNDER_MAINTENANCE, actor, now);
            var children = await db.Equipment.Where(item => item.FacilityId == facility.Id).ToListAsync(ct);
            foreach (var child in children.Where(item => item.Status != EquipmentStatus.OUT_OF_SERVICE))
                child.MarkUnderMaintenance(actor, now);
        }
        else
        {
            // A selected equipment scopes maintenance to that equipment; never alter its facility.
            equipment?.MarkUnderMaintenance(actor, now);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkActiveAsync(Guid? facilityId, Guid? equipmentId, Guid actor, CancellationToken ct)
    {
        if (facilityId is null && equipmentId is null)
            throw new ArgumentException("Phải chọn cơ sở vật chất hoặc thiết bị.");
        var facility = facilityId is { } selectedFacilityId
            ? await db.Facilities.SingleOrDefaultAsync(item => item.Id == selectedFacilityId, ct) ?? throw new KeyNotFoundException("Không tìm thấy cơ sở vật chất.")
            : null;
        var equipment = equipmentId is { } id
            ? await db.Equipment.SingleOrDefaultAsync(item => item.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy thiết bị.")
            : null;
        if (facility is not null && equipment?.FacilityId is not null && equipment.FacilityId != facilityId)
            throw new ArgumentException("Thiết bị không thuộc cơ sở vật chất đã chọn.");

        var now = DateTimeOffset.UtcNow;
        if (facility is not null && equipment is null)
        {
            // Restore only the facility-wide scope and only states FE-07 could have applied.
            if (facility.Status == MasterDataStatus.UNDER_MAINTENANCE)
                facility.SetStatus(MasterDataStatus.ACTIVE, actor, now);
            var children = await db.Equipment.Where(item => item.FacilityId == facility.Id).ToListAsync(ct);
            foreach (var child in children.Where(item => item.Status == EquipmentStatus.UNDER_MAINTENANCE))
                child.MarkActive(actor, now);
        }
        else if (equipment?.Status == EquipmentStatus.UNDER_MAINTENANCE)
        {
            // An equipment-scoped operation never changes the facility or sibling equipment.
            equipment.MarkActive(actor, now);
        }
        await db.SaveChangesAsync(ct);
    }
}
