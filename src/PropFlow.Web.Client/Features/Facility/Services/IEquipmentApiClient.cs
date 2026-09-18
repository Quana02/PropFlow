using PropFlow.Web.Client.Features.Facility.Models;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.Facility.Services;

public interface IEquipmentApiClient
{
    Task<ApiResult<PagedResult<EquipmentModel>>> GetEquipmentsAsync(EquipmentFilterModel filter, CancellationToken cancellationToken = default);
    Task<ApiResult<EquipmentDetailModel>> GetEquipmentByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ApiResult<EquipmentModel>> CreateEquipmentAsync(CreateEquipmentModel model, CancellationToken cancellationToken = default);
    Task<ApiResult<EquipmentModel>> UpdateEquipmentAsync(Guid id, UpdateEquipmentModel model, CancellationToken cancellationToken = default);
    Task<ApiResult<EquipmentModel>> SetEquipmentStatusAsync(Guid id, EquipmentStatus status, CancellationToken cancellationToken = default);
}
