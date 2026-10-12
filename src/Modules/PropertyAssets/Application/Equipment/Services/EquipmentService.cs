using PropFlow.Modules.PropertyAssets.Application;
using PropFlow.Modules.PropertyAssets.Application.Equipment.Dtos;
using PropFlow.Modules.PropertyAssets.Domain.Equipment;
using EquipmentEntity = PropFlow.Modules.PropertyAssets.Domain.Equipment.Equipment;
using PropFlow.Shared;

namespace PropFlow.Modules.PropertyAssets.Application.Equipment.Services;

public class EquipmentService : IEquipmentService
{
    private readonly IPropertyAssetsStore _store;

    public EquipmentService(IPropertyAssetsStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<PagedResult<EquipmentDto>> GetEquipmentsAsync(EquipmentFilterQuery query, CancellationToken cancellationToken = default)
    {
        var page = await _store.EquipmentAsync(query, cancellationToken);
        return new PagedResult<EquipmentDto>(page.Items.Select(MapToDto).ToList(), page.TotalCount, page.PageIndex, page.PageSize);
    }

    public async Task<EquipmentDto?> GetEquipmentByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var equipment = await _store.EquipmentAsync(id, false, cancellationToken);

        return equipment is null ? null : MapToDto(equipment);
    }

    public async Task<EquipmentDetailDto?> GetEquipmentDetailByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var equipment = await _store.EquipmentAsync(id, false, cancellationToken);

        if (equipment is null) return null;

        return new EquipmentDetailDto(
            equipment.Id,
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
        // Validate Facility exists if FacilityId is provided
        if (command.FacilityId.HasValue)
        {
            var facility = await _store.FacilityAsync(command.FacilityId.Value, false, false, cancellationToken);
            if (facility is null)
            {
                throw new ArgumentException($"Cơ sở vật chất với ID = {command.FacilityId.Value} không tồn tại.");
            }
        }

        var name = IdentifierCodeNormalizer.NormalizeRequired(command.Name, "Tên thiết bị");
        var prefix = $"TB-{name}-";
        if (prefix.Length + 3 > 512) throw new ArgumentException("Mã thiết bị sau chuẩn hóa vượt quá 512 ký tự.");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            EquipmentEntity? equipment = null;
            try
            {
                return await _store.WithAdvisoryLockAsync($"equipment:{name}", async token =>
                {
                    var number = NextSequence(await _store.EquipmentCodesWithPrefixAsync(prefix, token), prefix);
                    var code = $"{prefix}{number:000}";
                    equipment = new EquipmentEntity(code, command.Name, DateTimeOffset.UtcNow, command.FacilityId,
                        command.EquipmentType, command.Manufacturer, command.Model, command.SerialNumber,
                        command.InstallationDate, command.WarrantyExpiryDate, command.LocationDescription,
                        command.Description, command.CreatedBy);
                    _store.Add(equipment);
                    await _store.SaveGeneratedCodeAsync(token);
                    return await MapToDtoWithIncludesAsync(equipment.Id, token);
                }, cancellationToken);
            }
            catch (IdentifierCodeConflictException) when (equipment is not null && attempt < 2)
            {
                _store.Detach(equipment);
            }
        }
        throw new InvalidOperationException("Không thể cấp mã thiết bị duy nhất. Vui lòng thử lại.");
    }

    public async Task<EquipmentDto> UpdateEquipmentAsync(Guid id, UpdateEquipmentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var equipment = await _store.EquipmentAsync(id, true, cancellationToken);
        if (equipment is null)
        {
            throw new KeyNotFoundException($"Không tìm thấy thiết bị với mã định danh ID = {id}.");
        }

        // Validate Facility exists if FacilityId is provided
        if (command.FacilityId.HasValue)
        {
            var facility = await _store.FacilityAsync(command.FacilityId.Value, false, false, cancellationToken);
            if (facility is null)
            {
                throw new ArgumentException($"Cơ sở vật chất với ID = {command.FacilityId.Value} không tồn tại.");
            }
        }

        var now = DateTimeOffset.UtcNow;
        equipment.Update(
            command.Name,
            command.FacilityId,
            command.EquipmentType,
            null, // Preserve existing status - FE-04.6 owns status management
            command.Manufacturer,
            command.Model,
            command.SerialNumber,
            command.InstallationDate,
            command.WarrantyExpiryDate,
            command.LocationDescription,
            command.Description,
            command.UpdatedBy,
            now);

        await _store.SaveAsync(cancellationToken);

        return await MapToDtoWithIncludesAsync(equipment.Id, cancellationToken);
    }

    public async Task<EquipmentDto> SetEquipmentStatusAsync(Guid id, SetEquipmentStatusCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var equipment = await _store.EquipmentAsync(id, true, cancellationToken);

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

        await _store.SaveAsync(cancellationToken);

        return MapToDto(equipment);
    }

    private static EquipmentDto MapToDto(EquipmentEntity equipment)
    {
        return new EquipmentDto(
            equipment.Id,
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
        var equipment = await _store.EquipmentAsync(id, false, cancellationToken);

        return MapToDto(equipment ?? throw new InvalidOperationException("Equipment not found"));
    }

    private static int NextSequence(IReadOnlyList<string> codes, string prefix)
    {
        var max = 0;
        foreach (var code in codes)
            if (int.TryParse(code[prefix.Length..], out var sequence)) max = Math.Max(max, sequence);
        return max + 1;
    }
}
