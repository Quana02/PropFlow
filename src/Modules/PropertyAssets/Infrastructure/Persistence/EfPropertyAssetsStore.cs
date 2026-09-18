using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.PropertyAssets.Application;
using PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos;
using PropFlow.Modules.PropertyAssets.Application.Facilities.Dtos;
using PropFlow.Modules.PropertyAssets.Application.Equipment.Dtos;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;
using PropFlow.Modules.PropertyAssets.Domain.Equipment;
using EquipmentEntity = PropFlow.Modules.PropertyAssets.Domain.Equipment.Equipment;
using BuildingPage = PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos.PagedResult<PropFlow.Modules.PropertyAssets.Domain.Buildings.Building>;
using FacilityPage = PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos.PagedResult<PropFlow.Modules.PropertyAssets.Domain.Facilities.Facility>;
using EquipmentPage = PropFlow.Modules.PropertyAssets.Application.Equipment.Dtos.PagedResult<PropFlow.Modules.PropertyAssets.Domain.Equipment.Equipment>;

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;

public sealed class EfPropertyAssetsStore(PropertyAssetsDbContext db) : IPropertyAssetsStore
{
    public async Task<BuildingPage> BuildingsAsync(BuildingFilterQuery query, CancellationToken ct)
    {
        var source = db.Buildings.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.SearchKeyword))
        {
            var keyword = query.SearchKeyword.Trim().ToLower();
            source = source.Where(b => b.Code.ToLower().Contains(keyword) ||
                b.Name.ToLower().Contains(keyword) || b.Address.ToLower().Contains(keyword));
        }
        if (query.Status.HasValue) source = source.Where(b => b.Status == query.Status.Value);
        var count = await source.CountAsync(ct);
        var pageIndex = Math.Max(1, query.PageIndex);
        var pageSize = query.PageSize < 1 ? 10 : query.PageSize;
        var items = await source.OrderByDescending(b => b.CreatedAt).Skip((pageIndex - 1) * pageSize)
            .Take(pageSize).ToListAsync(ct);
        return new BuildingPage(items, count, pageIndex, pageSize);
    }

    public Task<Building?> BuildingAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var source = tracking ? db.Buildings.AsQueryable() : db.Buildings.AsNoTracking();
        return source.FirstOrDefaultAsync(b => b.Id == id, ct);
    }

    public async Task<BuildingAssetCounts> BuildingAssetCountsAsync(Guid id, CancellationToken ct) => new(
        await db.Facilities.CountAsync(f => f.BuildingId == id, ct),
        await db.Facilities.CountAsync(f => f.BuildingId == id && f.Status == MasterDataStatus.ACTIVE, ct),
        await db.Equipment.CountAsync(e => e.BuildingId == id, ct),
        await db.Equipment.CountAsync(e => e.BuildingId == id && e.Status == EquipmentStatus.ACTIVE, ct));

    public Task<bool> BuildingCodeExistsAsync(string code, CancellationToken ct) =>
        db.Buildings.AnyAsync(b => b.Code.ToLower() == code.ToLower(), ct);

    public Task<bool> BuildingExistsAsync(Guid id, CancellationToken ct) =>
        db.Buildings.AnyAsync(b => b.Id == id, ct);

    public Task<string?> BuildingNameAsync(Guid id, CancellationToken ct) =>
        db.Buildings.AsNoTracking().Where(b => b.Id == id).Select(b => b.Name).FirstOrDefaultAsync(ct);

    public void Add(Building building) => db.Buildings.Add(building);

    public async Task<FacilityPage> FacilitiesAsync(FacilityFilterQuery query, CancellationToken ct)
    {
        var source = db.Facilities.Include(f => f.Building).AsNoTracking();
        if (query.BuildingId.HasValue && query.BuildingId != Guid.Empty)
            source = source.Where(f => f.BuildingId == query.BuildingId);
        if (!string.IsNullOrWhiteSpace(query.SearchKeyword))
        {
            var keyword = query.SearchKeyword.Trim().ToLower();
            source = source.Where(f => f.Code.ToLower().Contains(keyword) || f.Name.ToLower().Contains(keyword) ||
                (f.FacilityType != null && f.FacilityType.ToLower().Contains(keyword)));
        }
        if (query.Status.HasValue) source = source.Where(f => f.Status == query.Status.Value);
        var count = await source.CountAsync(ct);
        var items = await source.OrderByDescending(f => f.CreatedAt)
            .Skip((query.PageIndex - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new FacilityPage(items, count, query.PageIndex, query.PageSize);
    }

    public Task<Facility?> FacilityAsync(Guid id, bool tracking, bool includeEquipment, CancellationToken ct)
    {
        IQueryable<Facility> source = db.Facilities.Include(f => f.Building);
        if (includeEquipment) source = source.Include(f => f.Equipment);
        if (!tracking) source = source.AsNoTracking();
        return source.FirstOrDefaultAsync(f => f.Id == id, ct);
    }

    public Task<bool> FacilityCodeExistsAsync(string code, CancellationToken ct) =>
        db.Facilities.AnyAsync(f => f.Code.ToLower() == code.ToLower(), ct);

    public void Add(Facility facility) => db.Facilities.Add(facility);

    public async Task<EquipmentPage> EquipmentAsync(EquipmentFilterQuery query, CancellationToken ct)
    {
        var source = db.Equipment.Include(e => e.Building).Include(e => e.Facility).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.SearchKeyword))
        {
            var keyword = query.SearchKeyword.Trim().ToLower();
            source = source.Where(e => e.Code.ToLower().Contains(keyword) || e.Name.ToLower().Contains(keyword) ||
                (e.Manufacturer != null && e.Manufacturer.ToLower().Contains(keyword)) ||
                (e.Model != null && e.Model.ToLower().Contains(keyword)));
        }
        if (query.BuildingId.HasValue) source = source.Where(e => e.BuildingId == query.BuildingId.Value);
        if (query.FacilityId.HasValue) source = source.Where(e => e.FacilityId == query.FacilityId.Value);
        if (query.Status.HasValue) source = source.Where(e => e.Status == query.Status.Value);
        var count = await source.CountAsync(ct);
        var pageIndex = Math.Max(1, query.PageIndex);
        var pageSize = query.PageSize < 1 ? 10 : query.PageSize;
        var items = await source.OrderByDescending(e => e.CreatedAt).Skip((pageIndex - 1) * pageSize)
            .Take(pageSize).ToListAsync(ct);
        return new EquipmentPage(items, count, pageIndex, pageSize);
    }

    public Task<EquipmentEntity?> EquipmentAsync(Guid id, bool tracking, CancellationToken ct)
    {
        IQueryable<EquipmentEntity> source = db.Equipment.Include(e => e.Building).Include(e => e.Facility);
        if (!tracking) source = source.AsNoTracking();
        return source.FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public Task<bool> EquipmentCodeExistsAsync(Guid buildingId, string code, CancellationToken ct) =>
        db.Equipment.AnyAsync(e => e.BuildingId == buildingId && e.Code.ToLower() == code.ToLower(), ct);

    public void Add(EquipmentEntity equipment) => db.Equipment.Add(equipment);

    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
