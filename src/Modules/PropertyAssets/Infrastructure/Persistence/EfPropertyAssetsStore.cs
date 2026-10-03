using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.PropertyAssets.Application;
using PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos;
using PropFlow.Modules.PropertyAssets.Application.Facilities.Dtos;
using PropFlow.Modules.PropertyAssets.Application.Equipment.Dtos;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;
using PropFlow.Modules.PropertyAssets.Domain.Equipment;
using EquipmentEntity = PropFlow.Modules.PropertyAssets.Domain.Equipment.Equipment;
using FacilityPage = PropFlow.Modules.PropertyAssets.Application.Facilities.Dtos.PagedResult<PropFlow.Modules.PropertyAssets.Domain.Facilities.Facility>;
using EquipmentPage = PropFlow.Modules.PropertyAssets.Application.Equipment.Dtos.PagedResult<PropFlow.Modules.PropertyAssets.Domain.Equipment.Equipment>;

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;

public sealed class EfPropertyAssetsStore(PropertyAssetsDbContext db, IAssetReadAccess access) : IPropertyAssetsStore
{
    public Task<Building?> BuildingAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var source = tracking ? db.Buildings.AsQueryable() : db.Buildings.AsNoTracking();
        return source.FirstOrDefaultAsync(b => b.Id == id, ct);
    }

    public async Task<CurrentBuildingPropertyOverviewDto?> CurrentBuildingOverviewAsync(CancellationToken ct)
    {
        var scope = await access.GetScopeAsync(ct);
        if (!scope.Unrestricted && !scope.HasAssignedWork) return null;
        var facilities = VisibleFacilities(scope);
        var equipment = VisibleEquipment(scope);
        var building = await db.Buildings.AsNoTracking()
            .OrderByDescending(b => b.Status == MasterDataStatus.ACTIVE)
            .ThenByDescending(b => b.UpdatedAt)
            .ThenByDescending(b => b.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (building is null)
            return null;

        // Facility statistics by status
        var facilityStatsRaw = await facilities
            .GroupBy(f => f.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var facilityStats = facilityStatsRaw.ToDictionary(g => g.Status, g => g.Count);

        var facilitySummary = new FacilitySummaryDto(
            Total: await facilities.CountAsync(ct),
            Active: facilityStats.GetValueOrDefault(MasterDataStatus.ACTIVE, 0),
            UnderMaintenance: facilityStats.GetValueOrDefault(MasterDataStatus.UNDER_MAINTENANCE, 0),
            Inactive: facilityStats.GetValueOrDefault(MasterDataStatus.INACTIVE, 0),
            Unavailable: facilityStats.GetValueOrDefault(MasterDataStatus.UNAVAILABLE, 0),
            OutOfService: facilityStats.GetValueOrDefault(MasterDataStatus.OUT_OF_SERVICE, 0)
        );

        // Equipment statistics by status
        var equipmentStatsRaw = await equipment
            .GroupBy(e => e.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var equipmentStats = equipmentStatsRaw.ToDictionary(g => g.Status, g => g.Count);

        var equipmentSummary = new EquipmentSummaryDto(
            Total: await equipment.CountAsync(ct),
            Active: equipmentStats.GetValueOrDefault(EquipmentStatus.ACTIVE, 0),
            UnderMaintenance: equipmentStats.GetValueOrDefault(EquipmentStatus.UNDER_MAINTENANCE, 0),
            Inactive: equipmentStats.GetValueOrDefault(EquipmentStatus.INACTIVE, 0),
            OutOfService: equipmentStats.GetValueOrDefault(EquipmentStatus.OUT_OF_SERVICE, 0)
        );

        return new CurrentBuildingPropertyOverviewDto(
            building.Id,
            building.Code,
            building.Name,
            building.Address,
            building.TimeZoneId,
            building.NumberOfFloors,
            building.Description,
            building.Status,
            facilitySummary,
            equipmentSummary,
            building.CreatedAt,
            building.UpdatedAt
        );
    }

    public void Add(Building building) => db.Buildings.Add(building);

    public async Task<FacilityPage> FacilitiesAsync(FacilityFilterQuery query, CancellationToken ct)
    {
        var pageIndex = Math.Max(1, query.PageIndex);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var source = VisibleFacilities(await access.GetScopeAsync(ct));
        if (!string.IsNullOrWhiteSpace(query.SearchKeyword))
        {
            var keyword = query.SearchKeyword.Trim().ToLower();
            source = source.Where(f => f.Code.ToLower().Contains(keyword) || f.Name.ToLower().Contains(keyword) ||
                (f.FacilityType != null && f.FacilityType.ToLower().Contains(keyword)));
        }
        if (query.Status.HasValue) source = source.Where(f => f.Status == query.Status.Value);
        if (!string.IsNullOrWhiteSpace(query.FacilityType)) source = source.Where(f => f.FacilityType == query.FacilityType);
        var count = await source.CountAsync(ct);
        var items = await source.OrderByDescending(f => f.CreatedAt).ThenBy(f => f.Code)
            .Skip((pageIndex - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new FacilityPage(items, count, pageIndex, pageSize);
    }

    public async Task<Facility?> FacilityAsync(Guid id, bool tracking, bool includeEquipment, CancellationToken ct)
    {
        var scope = await access.GetScopeAsync(ct);
        IQueryable<Facility> source = VisibleFacilities(scope);
        if (tracking) source = source.AsTracking();
        if (includeEquipment)
            source = scope.Unrestricted ? source.Include(f => f.Equipment)
                : source.Include(f => f.Equipment.Where(e => scope.EquipmentIds.Contains(e.Id)));
        if (!tracking) source = source.AsNoTracking();
        return await source.FirstOrDefaultAsync(f => f.Id == id, ct);
    }

    public Task<bool> FacilityCodeExistsAsync(string code, CancellationToken ct) =>
        db.Facilities.AnyAsync(f => f.Code.ToLower() == code.ToLower(), ct);

    public void Add(Facility facility) => db.Facilities.Add(facility);

    public async Task<EquipmentPage> EquipmentAsync(EquipmentFilterQuery query, CancellationToken ct)
    {
        IQueryable<EquipmentEntity> source = VisibleEquipment(await access.GetScopeAsync(ct)).Include(e => e.Facility);
        if (!string.IsNullOrWhiteSpace(query.SearchKeyword))
        {
            var keyword = query.SearchKeyword.Trim().ToLower();
            source = source.Where(e => e.Code.ToLower().Contains(keyword) || e.Name.ToLower().Contains(keyword) ||
                (e.Manufacturer != null && e.Manufacturer.ToLower().Contains(keyword)) ||
                (e.Model != null && e.Model.ToLower().Contains(keyword)));
        }
        if (query.BuildingLevelOnly) source = source.Where(e => e.FacilityId == null);
        if (query.FacilityId.HasValue) source = source.Where(e => e.FacilityId == query.FacilityId.Value);
        if (query.Status.HasValue) source = source.Where(e => e.Status == query.Status.Value);
        if (!string.IsNullOrWhiteSpace(query.EquipmentType)) source = source.Where(e => e.EquipmentType == query.EquipmentType);
        var count = await source.CountAsync(ct);
        var pageIndex = Math.Max(1, query.PageIndex);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var items = await source.OrderByDescending(e => e.CreatedAt).ThenBy(e => e.Code).Skip((pageIndex - 1) * pageSize)
            .Take(pageSize).ToListAsync(ct);
        return new EquipmentPage(items, count, pageIndex, pageSize);
    }

    public async Task<EquipmentEntity?> EquipmentAsync(Guid id, bool tracking, CancellationToken ct)
    {
        IQueryable<EquipmentEntity> source = VisibleEquipment(await access.GetScopeAsync(ct)).Include(e => e.Facility);
        if (tracking) source = source.AsTracking();
        if (!tracking) source = source.AsNoTracking();
        return await source.FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public Task<bool> EquipmentCodeExistsAsync(string code, CancellationToken ct) =>
        db.Equipment.AnyAsync(e => e.Code.ToLower() == code.ToLower(), ct);

    public void Add(EquipmentEntity equipment) => db.Equipment.Add(equipment);

    private IQueryable<Facility> VisibleFacilities(AssetReadScope scope)
    {
        var source = db.Facilities.AsNoTracking();
        return scope.Unrestricted ? source : source.Where(f => scope.FacilityIds.Contains(f.Id) ||
            db.Equipment.Any(e => e.FacilityId == f.Id && scope.EquipmentIds.Contains(e.Id)));
    }

    private IQueryable<EquipmentEntity> VisibleEquipment(AssetReadScope scope)
    {
        var source = db.Equipment.AsNoTracking();
        // A facility assignment does not implicitly grant access to every child asset.
        return scope.Unrestricted ? source : source.Where(e => scope.EquipmentIds.Contains(e.Id));
    }

    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
