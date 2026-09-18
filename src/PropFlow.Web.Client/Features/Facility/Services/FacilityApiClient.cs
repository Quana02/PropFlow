using PropFlow.Web.Client.Features.Facility.Models;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.Facility.Services;

public sealed class FacilityApiClient(AuthenticatedApiClient api) : IFacilityApiClient
{
    public Task<ApiResult<PagedResult<FacilityModel>>> GetFacilitiesAsync(FacilityFilterModel filter, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (filter.BuildingId.HasValue) query.Add($"buildingId={filter.BuildingId}");
        if (!string.IsNullOrWhiteSpace(filter.SearchKeyword)) query.Add($"searchKeyword={Uri.EscapeDataString(filter.SearchKeyword)}");
        if (filter.Status.HasValue) query.Add($"status={filter.Status.Value}");
        query.Add($"pageIndex={filter.PageIndex}");
        query.Add($"pageSize={filter.PageSize}");
        return api.SendAsync<PagedResult<FacilityModel>>(HttpMethod.Get, $"api/v1/facilities?{string.Join("&", query)}", ct: cancellationToken);
    }

    public Task<ApiResult<FacilityDetailModel>> GetFacilityByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        api.SendAsync<FacilityDetailModel>(HttpMethod.Get, $"api/v1/facilities/{id}", ct: cancellationToken);

    public Task<ApiResult<FacilityModel>> CreateFacilityAsync(CreateFacilityModel model, CancellationToken cancellationToken = default) =>
        api.SendAsync<FacilityModel>(HttpMethod.Post, "api/v1/facilities", model, ct: cancellationToken);

    public Task<ApiResult<FacilityModel>> UpdateFacilityAsync(Guid id, UpdateFacilityModel model, CancellationToken cancellationToken = default) =>
        api.SendAsync<FacilityModel>(HttpMethod.Put, $"api/v1/facilities/{id}", model, ct: cancellationToken);

    public Task<ApiResult<FacilityModel>> SetFacilityStatusAsync(Guid id, MasterDataStatus status, CancellationToken cancellationToken = default) =>
        api.SendAsync<FacilityModel>(HttpMethod.Patch, $"api/v1/facilities/{id}/status", new SetFacilityStatusModel { Status = status }, ct: cancellationToken);
}
