using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.PropertyAssets.Application.Equipment.Dtos;
using PropFlow.Modules.PropertyAssets.Domain.Equipment;
using EquipmentEntity = PropFlow.Modules.PropertyAssets.Domain.Equipment.Equipment;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;

namespace PropFlow.Modules.PropertyAssets.Application.Equipment.Services;

public class EquipmentService : IEquipmentService
{
    private readonly PropertyAssetsDbContext _dbContext;

    public EquipmentService(PropertyAssetsDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<PagedResult<EquipmentDto>> GetEquipmentsAsync(EquipmentFilterQuery query, CancellationToken cancellationToken = default)
    {
        var dbQuery = _dbContext.Equipment.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.SearchKeyword))
        {
            var keyword = query.SearchKeyword.Trim().ToLower();
            dbQuery = dbQuery.Where(e =>
                e.Code.ToLower().Contains(keyword) ||
                e.Name.ToLower().Contains(keyword) ||
                (e.Manufacturer != null && e.Manufacturer.ToLower().Contains(keyword)) ||
                (e.Model != null && e.Model.ToLower().Contains(keyword)));
        }

        if (query.BuildingId.HasValue)
        {
            dbQuery = dbQuery.Where(e => e.BuildingId == query.BuildingId.Value);
        }

        if (query.FacilityId.HasValue)
        {
            dbQuery = dbQuery.Where(e => e.FacilityId == query.FacilityId.Value);
        }

        if (query.Status.HasValue)
        {
            dbQuery = dbQuery.Where(e => e.Status == query.Status.Value);
        }

        var totalCount = await dbQuery.CountAsync(cancellationToken);

        var pageIndex = query.PageIndex < 1 ? 1 : query.PageIndex;
        var pageSize = query.PageSize < 1 ? 10 : query.PageSize;

        var items = await dbQuery
            .Include(e => e.Building)
            .Include(e => e.Facility)
            .OrderByDescending(e => e.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(e => MapToDto(e))
            .ToListAsync(cancellationToken);

        return new PagedResult<EquipmentDto>(items, totalCount, pageIndex, pageSize);
    }

    public async Task<EquipmentDto?> GetEquipmentByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var equipment = await _dbContext.Equipment
            .AsNoTracking()
            .Include(e => e.Building)
            .Include(e => e.Facility)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        return equipment is null ? null : MapToDto(equipment);
    }

    public async Task<EquipmentDetailDto?> GetEquipmentDetailByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var equipment = await _dbContext.Equipment
            .AsNoTracking()
            .Include(e => e.Building)
            .Include(e => e.Facility)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (equipment is null) return null;

        return new EquipmentDetailDto(
            equipment.Id,
            equipment.BuildingId,
            equipment.Building?.Name ?? "",
            equipment.FacilityId,
            equipment.Facility?.Name,
            equipment.Code,
            equipment.Name,
            equipment.EquipmentType,
            equipment.Manufacturer,
            equipment.Model,
            equipment.SerialNumber,
            equipment.InstallationDate,
            equipment.WarrantyExpiryDate,
            equipment.LocationDescription,
            equipment.Status,
            equipment.Description,
            equipment.CreatedBy,
            equipment.UpdatedBy,
            equipment.CreatedAt,
            equipment.UpdatedAt);
    }

    public async Task<EquipmentDto> CreateEquipmentAsync(CreateEquipmentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var codeUpper = command.Code.Trim();
        var existingEquipment = await _dbContext.Equipment
            .AnyAsync(e => e.BuildingId == command.BuildingId && e.Code.ToLower() == codeUpper.ToLower(), cancellationToken);

        if (existingEquipment)
        {
            throw new InvalidOperationException($"Mã thiết bị '{command.Code}' đã tồn tại trong tòa nhà này.");
        }

        var now = DateTimeOffset.UtcNow;
        var equipment = new EquipmentEntity(
            command.BuildingId,
            command.Code,
            command.Name,
            now,
            command.FacilityId,
            command.EquipmentType,
            command.Manufacturer,
            command.Model,
            command.SerialNumber,
            command.InstallationDate,
            command.WarrantyExpiryDate,
            command.LocationDescription,
            command.Description,
            command.CreatedBy);

        _dbContext.Equipment.Add(equipment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await MapToDtoWithIncludesAsync(equipment.Id, cancellationToken);
    }

    public async Task<EquipmentDto> UpdateEquipmentAsync(Guid id, UpdateEquipmentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var equipment = await _dbContext.Equipment.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (equipment is null)
        {
            throw new KeyNotFoundException($"Không tìm thấy thiết bị với mã định danh ID = {id}.");
        }

        var now = DateTimeOffset.UtcNow;
        equipment.Update(
            command.Name,
            command.FacilityId,
            command.EquipmentType,
            command.Status,
            command.Manufacturer,
            command.Model,
            command.SerialNumber,
            command.InstallationDate,
            command.WarrantyExpiryDate,
            command.LocationDescription,
            command.Description,
            command.UpdatedBy,
            now);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await MapToDtoWithIncludesAsync(equipment.Id, cancellationToken);
    }

    public async Task<EquipmentDto> SetEquipmentStatusAsync(Guid id, SetEquipmentStatusCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var equipment = await _dbContext.Equipment
            .Include(e => e.Building)
            .Include(e => e.Facility)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (equipment is null)
        {
            throw new KeyNotFoundException($"Không tìm thấy thiết bị với mã định danh ID = {id}.");
        }

        switch (command.Status)
        {
            case EquipmentStatus.ACTIVE:
                equipment.MarkActive(command.UpdatedBy, DateTimeOffset.UtcNow);
                break;
            case EquipmentStatus.INACTIVE:
                equipment.Deactivate(command.UpdatedBy, DateTimeOffset.UtcNow);
                break;
            case EquipmentStatus.UNDER_MAINTENANCE:
                equipment.MarkUnderMaintenance(command.UpdatedBy, DateTimeOffset.UtcNow);
                break;
            case EquipmentStatus.OUT_OF_SERVICE:
                equipment.MarkOutOfService(command.UpdatedBy, DateTimeOffset.UtcNow);
                break;
            default:
                throw new ArgumentException($"Trạng thái không hợp lệ: {command.Status}");
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(equipment);
    }

    private static EquipmentDto MapToDto(EquipmentEntity equipment)
    {
        return new EquipmentDto(
            equipment.Id,
            equipment.BuildingId,
            equipment.Building?.Name ?? "",
            equipment.FacilityId,
            equipment.Facility?.Name,
            equipment.Code,
            equipment.Name,
            equipment.EquipmentType,
            equipment.Manufacturer,
            equipment.Model,
            equipment.SerialNumber,
            equipment.InstallationDate,
            equipment.WarrantyExpiryDate,
            equipment.LocationDescription,
            equipment.Status,
            equipment.Description,
            equipment.CreatedBy,
            equipment.UpdatedBy,
            equipment.CreatedAt,
            equipment.UpdatedAt);
    }

    private async Task<EquipmentDto> MapToDtoWithIncludesAsync(Guid id, CancellationToken cancellationToken)
    {
        var equipment = await _dbContext.Equipment
            .AsNoTracking()
            .Include(e => e.Building)
            .Include(e => e.Facility)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        return MapToDto(equipment ?? throw new InvalidOperationException("Equipment not found"));
    }
}
