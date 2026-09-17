using PropFlow.Web.Client.Features.Facility.Models;

namespace PropFlow.Web.Client.Features.Facility.Services;

public interface IEquipmentApiClient
{
    Task<PagedResult<EquipmentModel>> GetEquipmentsAsync(EquipmentFilterModel filter, CancellationToken cancellationToken = default);
    Task<EquipmentDetailModel?> GetEquipmentByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<EquipmentModel> CreateEquipmentAsync(CreateEquipmentModel model, CancellationToken cancellationToken = default);
    Task<EquipmentModel> UpdateEquipmentAsync(Guid id, UpdateEquipmentModel model, CancellationToken cancellationToken = default);
    Task<EquipmentModel> SetEquipmentStatusAsync(Guid id, EquipmentStatus status, CancellationToken cancellationToken = default);
}
