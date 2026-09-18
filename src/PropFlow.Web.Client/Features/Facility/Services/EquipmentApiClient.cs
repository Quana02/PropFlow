using PropFlow.Web.Client.Features.Facility.Models;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.Facility.Services;

public sealed class EquipmentApiClient(AuthenticatedApiClient api) : IEquipmentApiClient
{
    public Task<ApiResult<PagedResult<EquipmentModel>>> GetEquipmentsAsync(EquipmentFilterModel filter, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (filter.BuildingId.HasValue) query.Add($"buildingId={filter.BuildingId}");
        if (filter.FacilityId.HasValue) query.Add($"facilityId={filter.FacilityId}");
        if (!string.IsNullOrWhiteSpace(filter.SearchKeyword)) query.Add($"searchKeyword={Uri.EscapeDataString(filter.SearchKeyword)}");
        if (filter.Status.HasValue) query.Add($"status={filter.Status.Value}");
        query.Add($"pageIndex={filter.PageIndex}");
        query.Add($"pageSize={filter.PageSize}");
        return api.SendAsync<PagedResult<EquipmentModel>>(HttpMethod.Get, $"api/v1/equipments?{string.Join("&", query)}", ct: cancellationToken);
    }

    public Task<ApiResult<EquipmentDetailModel>> GetEquipmentByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        api.SendAsync<EquipmentDetailModel>(HttpMethod.Get, $"api/v1/equipments/{id}", ct: cancellationToken);

    public Task<ApiResult<EquipmentModel>> CreateEquipmentAsync(CreateEquipmentModel model, CancellationToken cancellationToken = default) =>
        api.SendAsync<EquipmentModel>(HttpMethod.Post, "api/v1/equipments", model, ct: cancellationToken);

    public Task<ApiResult<EquipmentModel>> UpdateEquipmentAsync(Guid id, UpdateEquipmentModel model, CancellationToken cancellationToken = default) =>
        api.SendAsync<EquipmentModel>(HttpMethod.Put, $"api/v1/equipments/{id}", model, ct: cancellationToken);

    public Task<ApiResult<EquipmentModel>> SetEquipmentStatusAsync(Guid id, EquipmentStatus status, CancellationToken cancellationToken = default) =>
        api.SendAsync<EquipmentModel>(HttpMethod.Patch, $"api/v1/equipments/{id}/status", new SetEquipmentStatusModel { Status = status }, ct: cancellationToken);
}
