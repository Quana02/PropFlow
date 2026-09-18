using System.Net.Http.Json;
using PropFlow.Web.Client.Features.Facility.Models;

namespace PropFlow.Web.Client.Features.Facility.Services;

public class FacilityApiClient : IFacilityApiClient
{
    private readonly HttpClient _httpClient;

    public FacilityApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<PagedResult<FacilityModel>> GetFacilitiesAsync(FacilityFilterModel filter, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (filter.BuildingId.HasValue) query.Add($"buildingId={filter.BuildingId}");
        if (!string.IsNullOrEmpty(filter.SearchKeyword)) query.Add($"searchKeyword={Uri.EscapeDataString(filter.SearchKeyword)}");
        if (filter.Status.HasValue) query.Add($"status={filter.Status}");
        query.Add($"pageIndex={filter.PageIndex}");
        query.Add($"pageSize={filter.PageSize}");
        var url = $"api/v1/facilities?{string.Join("&", query)}";
        return await _httpClient.GetFromJsonAsync<PagedResult<FacilityModel>>(url, cancellationToken) ?? new PagedResult<FacilityModel>();
    }

    public async Task<FacilityDetailModel?> GetFacilityByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<FacilityDetailModel>($"api/v1/facilities/{id}", cancellationToken);
    }

    public async Task<FacilityModel> CreateFacilityAsync(CreateFacilityModel model, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/v1/facilities", model, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<FacilityModel>(cancellationToken) ?? new FacilityModel();
    }

    public async Task<FacilityModel> UpdateFacilityAsync(Guid id, UpdateFacilityModel model, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/v1/facilities/{id}", model, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<FacilityModel>(cancellationToken) ?? new FacilityModel();
    }

    public async Task<FacilityModel> SetFacilityStatusAsync(Guid id, MasterDataStatus status, CancellationToken cancellationToken = default)
    {
        var body = new SetFacilityStatusModel { Status = status };
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/facilities/{id}/status")
        {
            Content = JsonContent.Create(body)
        };
        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorJson = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Lỗi chuyển trạng thái: {response.StatusCode} - {errorJson}");
        }
        var result = await response.Content.ReadFromJsonAsync<FacilityModel>(cancellationToken: cancellationToken);
        return result ?? throw new InvalidOperationException("Không nhận được dữ liệu từ server.");
    }
}
