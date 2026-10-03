using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;

namespace PropFlow.Modules.PropertyAssets.Infrastructure;

public sealed class MaintenanceAssetSource(PropertyAssetsDbContext db) : IMaintenanceAssetSource
{
    public async Task<IReadOnlyList<MaintenanceAssetReference>> GetAvailableAsync(CancellationToken ct)
    {
        var facilities = await db.Facilities.AsNoTracking().Select(x => new MaintenanceAssetReference(x.Id, x.Code, x.Name, "FACILITY", x.Status.ToString(), x.LocationDescription, null)).ToListAsync(ct);
        var equipment = await db.Equipment.AsNoTracking().Select(x => new MaintenanceAssetReference(x.Id, x.Code, x.Name, "EQUIPMENT", x.Status.ToString(), x.LocationDescription, x.FacilityId)).ToListAsync(ct);
        return facilities.Concat(equipment).OrderBy(x => x.AssetType).ThenBy(x => x.Code).ToArray();
    }

    public Task<MaintenanceAssetReference?> GetFacilityAsync(Guid facilityId, CancellationToken ct) =>
        db.Facilities.AsNoTracking()
            .Where(x => x.Id == facilityId)
            .Select(x => new MaintenanceAssetReference(x.Id, x.Code, x.Name, "FACILITY", x.Status.ToString(), x.LocationDescription, null))
            .SingleOrDefaultAsync(ct);

    public Task<MaintenanceAssetReference?> GetEquipmentAsync(Guid equipmentId, CancellationToken ct) =>
        db.Equipment.AsNoTracking()
            .Where(x => x.Id == equipmentId)
            .Select(x => new MaintenanceAssetReference(x.Id, x.Code, x.Name, "EQUIPMENT", x.Status.ToString(), x.LocationDescription, x.FacilityId))
            .SingleOrDefaultAsync(ct);

    public async Task<bool> ExistsAsync(Guid? facilityId, Guid? equipmentId, CancellationToken ct)
    {
        if (facilityId is { } facility && !await db.Facilities.AsNoTracking().AnyAsync(x => x.Id == facility, ct)) return false;
        if (equipmentId is { } equipment && !await db.Equipment.AsNoTracking().AnyAsync(x => x.Id == equipment, ct)) return false;
        return true;
    }

    public async Task MarkUnderMaintenanceAsync(Guid facilityId, Guid? equipmentId, Guid actor, CancellationToken ct)
    {
        var facility = await db.Facilities.SingleOrDefaultAsync(item => item.Id == facilityId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy cơ sở vật chất.");
        var equipment = equipmentId is { } id
            ? await db.Equipment.SingleOrDefaultAsync(item => item.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy thiết bị.")
            : null;
        if (equipment?.FacilityId is not null && equipment.FacilityId != facilityId)
            throw new ArgumentException("Thiết bị không thuộc cơ sở vật chất đã chọn.");

        var now = DateTimeOffset.UtcNow;
        facility.SetStatus(MasterDataStatus.UNDER_MAINTENANCE, actor, now);
        equipment?.MarkUnderMaintenance(actor, now);
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkActiveAsync(Guid facilityId, Guid? equipmentId, Guid actor, CancellationToken ct)
    {
        var facility = await db.Facilities.SingleOrDefaultAsync(item => item.Id == facilityId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy cơ sở vật chất.");
        var equipment = equipmentId is { } id
            ? await db.Equipment.SingleOrDefaultAsync(item => item.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy thiết bị.")
            : null;
        if (equipment?.FacilityId is not null && equipment.FacilityId != facilityId)
            throw new ArgumentException("Thiết bị không thuộc cơ sở vật chất đã chọn.");

        var now = DateTimeOffset.UtcNow;
        facility.SetStatus(MasterDataStatus.ACTIVE, actor, now);
        equipment?.MarkActive(actor, now);
        await db.SaveChangesAsync(ct);
    }
}
