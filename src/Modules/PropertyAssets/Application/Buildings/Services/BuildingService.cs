using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;

namespace PropFlow.Modules.PropertyAssets.Application.Buildings.Services;

public class BuildingService : IBuildingService
{
    private readonly PropertyAssetsDbContext _dbContext;

    public BuildingService(PropertyAssetsDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<PagedResult<BuildingDto>> GetBuildingsAsync(BuildingFilterQuery query, CancellationToken cancellationToken = default)
    {
        var dbQuery = _dbContext.Buildings.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.SearchKeyword))
        {
            var keyword = query.SearchKeyword.Trim().ToLower();
            dbQuery = dbQuery.Where(b =>
                b.Code.ToLower().Contains(keyword) ||
                b.Name.ToLower().Contains(keyword) ||
                b.Address.ToLower().Contains(keyword));
        }

        if (query.Status.HasValue)
        {
            dbQuery = dbQuery.Where(b => b.Status == query.Status.Value);
        }

        var totalCount = await dbQuery.CountAsync(cancellationToken);

        var pageIndex = query.PageIndex < 1 ? 1 : query.PageIndex;
        var pageSize = query.PageSize < 1 ? 10 : query.PageSize;

        var items = await dbQuery
            .OrderByDescending(b => b.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(b => MapToDto(b))
            .ToListAsync(cancellationToken);

        return new PagedResult<BuildingDto>(items, totalCount, pageIndex, pageSize);
    }

    public async Task<BuildingDto?> GetBuildingByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var building = await _dbContext.Buildings
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        return building is null ? null : MapToDto(building);
    }

    public async Task<BuildingDetailDto?> GetBuildingDetailByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var building = await _dbContext.Buildings
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (building is null) return null;

        var facilityCount = await _dbContext.Facilities.CountAsync(f => f.BuildingId == id, cancellationToken);
        var activeFacilityCount = await _dbContext.Facilities.CountAsync(f => f.BuildingId == id && f.Status == MasterDataStatus.ACTIVE, cancellationToken);

        var equipmentCount = await _dbContext.Equipment.CountAsync(e => e.BuildingId == id, cancellationToken);
        var activeEquipmentCount = await _dbContext.Equipment.CountAsync(e => e.BuildingId == id && e.Status == Domain.Equipment.EquipmentStatus.ACTIVE, cancellationToken);

        return new BuildingDetailDto(
            building.Id,
            building.Code,
            building.Name,
            building.Address,
            building.TimeZoneId,
            building.NumberOfFloors,
            building.Description,
            building.Status,
            facilityCount,
            equipmentCount,
            activeFacilityCount,
            activeEquipmentCount,
            building.CreatedBy,
            building.UpdatedBy,
            building.CreatedAt,
            building.UpdatedAt);
    }

    public async Task<BuildingDto> CreateBuildingAsync(CreateBuildingCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var codeUpper = command.Code.Trim();
        var existingBuilding = await _dbContext.Buildings
            .AnyAsync(b => b.Code.ToLower() == codeUpper.ToLower(), cancellationToken);

        if (existingBuilding)
        {
            throw new InvalidOperationException($"Mã tòa nhà '{command.Code}' đã tồn tại trong hệ thống.");
        }

        var now = DateTimeOffset.UtcNow;
        var building = new Building(
            command.Code,
            command.Name,
            command.Address,
            now,
            string.IsNullOrWhiteSpace(command.TimeZoneId) ? "Asia/Ho_Chi_Minh" : command.TimeZoneId,
            command.NumberOfFloors,
            command.Description,
            command.CreatedBy);

        _dbContext.Buildings.Add(building);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(building);
    }

    public async Task<BuildingDto> UpdateBuildingAsync(Guid id, UpdateBuildingCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var building = await _dbContext.Buildings.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (building is null)
        {
            throw new KeyNotFoundException($"Không tìm thấy tòa nhà với mã định danh ID = {id}.");
        }

        var now = DateTimeOffset.UtcNow;
        building.Update(
            command.Name,
            command.Address,
            string.IsNullOrWhiteSpace(command.TimeZoneId) ? "Asia/Ho_Chi_Minh" : command.TimeZoneId,
            command.NumberOfFloors,
            command.Description,
            command.UpdatedBy,
            now);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(building);
    }

    public async Task<BuildingDto> SetBuildingStatusAsync(Guid id, SetBuildingStatusCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var building = await _dbContext.Buildings.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (building is null)
        {
            throw new KeyNotFoundException($"Không tìm thấy tòa nhà với mã định danh ID = {id}.");
        }

        var now = DateTimeOffset.UtcNow;
        if (command.Status == MasterDataStatus.INACTIVE)
        {
            building.Deactivate(command.UpdatedBy, now);
        }
        else
        {
            building.Activate(command.UpdatedBy, now);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(building);
    }

    private static BuildingDto MapToDto(Building building)
    {
        return new BuildingDto(
            building.Id,
            building.Code,
            building.Name,
            building.Address,
            building.TimeZoneId,
            building.NumberOfFloors,
            building.Description,
            building.Status,
            building.CreatedBy,
            building.UpdatedBy,
            building.CreatedAt,
            building.UpdatedAt);
    }
}
