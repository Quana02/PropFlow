using PropFlow.Web.Client.Features.Facility.Models;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.Facility.Services;

public sealed class BuildingApiClient(AuthenticatedApiClient api) : IBuildingApiClient
{
    public Task<ApiResult<PagedResult<BuildingModel>>> GetBuildingsAsync(BuildingFilterModel filter, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(filter.SearchKeyword)) query.Add($"searchKeyword={Uri.EscapeDataString(filter.SearchKeyword)}");
        if (filter.Status.HasValue) query.Add($"status={filter.Status.Value}");
        query.Add($"pageIndex={filter.PageIndex}");
        query.Add($"pageSize={filter.PageSize}");
        return api.SendAsync<PagedResult<BuildingModel>>(HttpMethod.Get, $"api/v1/buildings?{string.Join("&", query)}", ct: cancellationToken);
    }

    public Task<ApiResult<BuildingModel>> GetBuildingByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        api.SendAsync<BuildingModel>(HttpMethod.Get, $"api/v1/buildings/{id}", ct: cancellationToken);

    public Task<ApiResult<BuildingDetailModel>> GetBuildingDetailByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        api.SendAsync<BuildingDetailModel>(HttpMethod.Get, $"api/v1/buildings/{id}/detail", ct: cancellationToken);

    public Task<ApiResult<BuildingModel>> CreateBuildingAsync(CreateBuildingModel model, CancellationToken cancellationToken = default) =>
        api.SendAsync<BuildingModel>(HttpMethod.Post, "api/v1/buildings", model, ct: cancellationToken);

    public Task<ApiResult<BuildingModel>> UpdateBuildingAsync(Guid id, UpdateBuildingModel model, CancellationToken cancellationToken = default) =>
        api.SendAsync<BuildingModel>(HttpMethod.Put, $"api/v1/buildings/{id}", model, ct: cancellationToken);

    public Task<ApiResult<BuildingModel>> SetBuildingStatusAsync(Guid id, MasterDataStatus status, CancellationToken cancellationToken = default) =>
        api.SendAsync<BuildingModel>(HttpMethod.Patch, $"api/v1/buildings/{id}/status", new SetBuildingStatusModel { Status = status }, ct: cancellationToken);
}
