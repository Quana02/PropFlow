using System.Net.Http.Json;
using PropFlow.Web.Client.Features.Facility.Models;

namespace PropFlow.Web.Client.Features.Facility.Services;

public class EquipmentApiClient : IEquipmentApiClient
{
    private readonly HttpClient _httpClient;

    public EquipmentApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<PagedResult<EquipmentModel>> GetEquipmentsAsync(EquipmentFilterModel filter, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (filter.BuildingId.HasValue) query.Add($"buildingId={filter.BuildingId}");
        if (filter.FacilityId.HasValue) query.Add($"facilityId={filter.FacilityId}");
        if (!string.IsNullOrEmpty(filter.SearchKeyword)) query.Add($"searchKeyword={Uri.EscapeDataString(filter.SearchKeyword)}");
        if (filter.Status.HasValue) query.Add($"status={filter.Status}");
        query.Add($"pageIndex={filter.PageIndex}");
        query.Add($"pageSize={filter.PageSize}");
        var url = $"api/v1/equipments?{string.Join("&", query)}";
        return await _httpClient.GetFromJsonAsync<PagedResult<EquipmentModel>>(url, cancellationToken) ?? new PagedResult<EquipmentModel>();
    }

    public async Task<EquipmentDetailModel?> GetEquipmentByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<EquipmentDetailModel>($"api/v1/equipments/{id}", cancellationToken);
    }

    public async Task<EquipmentModel> CreateEquipmentAsync(CreateEquipmentModel model, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/v1/equipments", model, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EquipmentModel>(cancellationToken) ?? new EquipmentModel();
    }

    public async Task<EquipmentModel> UpdateEquipmentAsync(Guid id, UpdateEquipmentModel model, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/v1/equipments/{id}", model, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EquipmentModel>(cancellationToken) ?? new EquipmentModel();
    }

    public async Task<EquipmentModel> SetEquipmentStatusAsync(Guid id, EquipmentStatus status, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PatchAsJsonAsync($"api/v1/equipments/{id}/status", new SetEquipmentStatusModel { Status = status }, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EquipmentModel>(cancellationToken) ?? new EquipmentModel();
    }
}
