using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos;
using PropFlow.Modules.PropertyAssets.Application.Facilities.Dtos;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;

namespace PropFlow.Modules.PropertyAssets.Application.Facilities.Services;

public class FacilityService : IFacilityService
{
    private readonly PropertyAssetsDbContext _dbContext;

    public FacilityService(PropertyAssetsDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<PagedResult<FacilityDto>> GetFacilitiesAsync(FacilityFilterQuery query, CancellationToken cancellationToken = default)
    {
        var dbQuery = _dbContext.Facilities.Include(f => f.Building).AsNoTracking();

        if (query.BuildingId.HasValue && query.BuildingId != Guid.Empty)
        {
            dbQuery = dbQuery.Where(f => f.BuildingId == query.BuildingId);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchKeyword))
        {
            var keyword = query.SearchKeyword.Trim().ToLower();
            dbQuery = dbQuery.Where(f =>
                f.Code.ToLower().Contains(keyword) ||
                f.Name.ToLower().Contains(keyword) ||
                (f.FacilityType != null && f.FacilityType.ToLower().Contains(keyword)));
        }

        if (query.Status.HasValue)
        {
            dbQuery = dbQuery.Where(f => f.Status == query.Status.Value);
        }

        var totalCount = await dbQuery.CountAsync(cancellationToken);

        var items = await dbQuery
            .OrderByDescending(f => f.CreatedAt)
            .Skip((query.PageIndex - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(f => new FacilityDto(
                f.Id,
                f.BuildingId,
                f.Building != null ? f.Building.Name : string.Empty,
                f.Code,
                f.Name,
                f.FacilityType,
                f.LocationDescription,
                f.Description,
                f.Status,
                f.CreatedBy,
                f.UpdatedBy,
                f.CreatedAt,
                f.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<FacilityDto>(items, totalCount, query.PageIndex, query.PageSize);
    }

    public async Task<FacilityDetailDto?> GetFacilityByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var facility = await _dbContext.Facilities
            .Include(f => f.Building)
            .Include(f => f.Equipment)
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (facility == null)
            return null;

        var totalEq = facility.Equipment.Count;
        var activeEq = facility.Equipment.Count(e => e.Status == PropFlow.Modules.PropertyAssets.Domain.Equipment.EquipmentStatus.ACTIVE);

        return new FacilityDetailDto(
            facility.Id,
            facility.BuildingId,
            facility.Building != null ? facility.Building.Name : string.Empty,
            facility.Code,
            facility.Name,
            facility.FacilityType,
            facility.LocationDescription,
            facility.Description,
            facility.Status,
            totalEq,
            activeEq,
            facility.CreatedBy,
            facility.UpdatedBy,
            facility.CreatedAt,
            facility.UpdatedAt);
    }

    public async Task<FacilityDto> CreateFacilityAsync(CreateFacilityCommand command, CancellationToken cancellationToken = default)
    {
        var buildingExists = await _dbContext.Buildings.AnyAsync(b => b.Id == command.BuildingId, cancellationToken);
        if (!buildingExists)
        {
            throw new ArgumentException("Tòa nhà không tồn tại.");
        }

        var codeExists = await _dbContext.Facilities.AnyAsync(f => f.Code.ToLower() == command.Code.Trim().ToLower(), cancellationToken);
        if (codeExists)
        {
            throw new ArgumentException($"Mã tiện ích '{command.Code}' đã tồn tại.");
        }

        var facility = new Facility(
            command.BuildingId,
            command.Code,
            command.Name,
            DateTimeOffset.UtcNow,
            command.FacilityType,
            command.LocationDescription,
            command.Description,
            command.CreatedBy,
            command.InitialStatus);

        _dbContext.Facilities.Add(facility);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await MapToDtoAsync(facility, cancellationToken);
    }

    public async Task<FacilityDto> UpdateFacilityAsync(Guid id, UpdateFacilityCommand command, CancellationToken cancellationToken = default)
    {
        var facility = await _dbContext.Facilities
            .Include(f => f.Building)
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (facility == null)
        {
            throw new KeyNotFoundException("Không tìm thấy tiện ích.");
        }

        facility.Update(
            command.BuildingId,
            command.Name,
            command.FacilityType,
            command.LocationDescription,
            command.Description,
            command.Status,
            command.UpdatedBy,
            DateTimeOffset.UtcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await MapToDtoAsync(facility, cancellationToken);
    }

    public async Task<FacilityDto> SetFacilityStatusAsync(Guid id, SetFacilityStatusCommand command, CancellationToken cancellationToken = default)
    {
        var facility = await _dbContext.Facilities
            .Include(f => f.Building)
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (facility == null)
        {
            throw new KeyNotFoundException("Không tìm thấy tiện ích.");
        }

        facility.SetStatus(command.Status, command.UpdatedBy, DateTimeOffset.UtcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await MapToDtoAsync(facility, cancellationToken);
    }

    private async Task<FacilityDto> MapToDtoAsync(Facility facility, CancellationToken cancellationToken)
    {
        var buildingName = facility.Building?.Name;
        if (buildingName == null && facility.BuildingId != Guid.Empty)
        {
            var b = await _dbContext.Buildings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == facility.BuildingId, cancellationToken);
            buildingName = b?.Name ?? string.Empty;
        }

        return new FacilityDto(
            facility.Id,
            facility.BuildingId,
            buildingName ?? string.Empty,
            facility.Code,
            facility.Name,
            facility.FacilityType,
            facility.LocationDescription,
            facility.Description,
            facility.Status,
            facility.CreatedBy,
            facility.UpdatedBy,
            facility.CreatedAt,
            facility.UpdatedAt);
    }
}
