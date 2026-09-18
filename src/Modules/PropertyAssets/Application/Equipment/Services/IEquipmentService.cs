using PropFlow.Modules.PropertyAssets.Application.Equipment.Dtos;

namespace PropFlow.Modules.PropertyAssets.Application.Equipment.Services;

public interface IEquipmentService
{
    Task<PagedResult<EquipmentDto>> GetEquipmentsAsync(EquipmentFilterQuery query, CancellationToken cancellationToken = default);
    Task<EquipmentDto?> GetEquipmentByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<EquipmentDetailDto?> GetEquipmentDetailByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<EquipmentDto> CreateEquipmentAsync(CreateEquipmentCommand command, CancellationToken cancellationToken = default);
    Task<EquipmentDto> UpdateEquipmentAsync(Guid id, UpdateEquipmentCommand command, CancellationToken cancellationToken = default);
    Task<EquipmentDto> SetEquipmentStatusAsync(Guid id, SetEquipmentStatusCommand command, CancellationToken cancellationToken = default);
}
