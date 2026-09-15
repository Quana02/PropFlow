using System.Net.Http.Json;
using PropFlow.Web.Client.Features.Facility.Models;

namespace PropFlow.Web.Client.Features.Facility.Services;

public class BuildingApiClient : IBuildingApiClient
{
    private readonly HttpClient _httpClient;

    public BuildingApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<PagedResult<BuildingModel>> GetBuildingsAsync(BuildingFilterModel filter, CancellationToken cancellationToken = default)
    {
        var queryParams = new List<string>();

        if (!string.IsNullOrWhiteSpace(filter.SearchKeyword))
        {
            queryParams.Add($"searchKeyword={Uri.EscapeDataString(filter.SearchKeyword)}");
        }

        if (filter.Status.HasValue)
        {
            queryParams.Add($"status={filter.Status.Value}");
        }

        queryParams.Add($"pageIndex={filter.PageIndex}");
        queryParams.Add($"pageSize={filter.PageSize}");

        var queryString = string.Join("&", queryParams);
        var url = $"/api/v1/buildings?{queryString}";

        var result = await _httpClient.GetFromJsonAsync<PagedResult<BuildingModel>>(url, cancellationToken);
        return result ?? new PagedResult<BuildingModel>();
    }

    public async Task<BuildingModel?> GetBuildingByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/v1/buildings/{id}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BuildingModel>(cancellationToken: cancellationToken);
    }

    public async Task<BuildingDetailModel?> GetBuildingDetailByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/v1/buildings/{id}/detail", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BuildingDetailModel>(cancellationToken: cancellationToken);
    }

    public async Task<BuildingModel> CreateBuildingAsync(CreateBuildingModel model, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/v1/buildings", model, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorJson = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Lỗi tạo tòa nhà: {response.StatusCode} - {errorJson}");
        }

        var result = await response.Content.ReadFromJsonAsync<BuildingModel>(cancellationToken: cancellationToken);
        return result ?? throw new InvalidOperationException("Không nhận được dữ liệu phản hồi từ server.");
    }

    public async Task<BuildingModel> UpdateBuildingAsync(Guid id, UpdateBuildingModel model, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync($"/api/v1/buildings/{id}", model, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorJson = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Lỗi cập nhật tòa nhà: {response.StatusCode} - {errorJson}");
        }

        var result = await response.Content.ReadFromJsonAsync<BuildingModel>(cancellationToken: cancellationToken);
        return result ?? throw new InvalidOperationException("Không nhận được dữ liệu phản hồi từ server.");
    }

    public async Task<BuildingModel> SetBuildingStatusAsync(Guid id, MasterDataStatus status, CancellationToken cancellationToken = default)
    {
        var body = new SetBuildingStatusModel { Status = status };
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/buildings/{id}/status")
        {
            Content = JsonContent.Create(body)
        };

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorJson = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Lỗi chuyển trạng thái tòa nhà: {response.StatusCode} - {errorJson}");
        }

        var result = await response.Content.ReadFromJsonAsync<BuildingModel>(cancellationToken: cancellationToken);
        return result ?? throw new InvalidOperationException("Không nhận được dữ liệu phản hồi từ server.");
    }
}
