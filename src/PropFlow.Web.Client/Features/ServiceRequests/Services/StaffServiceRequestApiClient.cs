using Microsoft.AspNetCore.Components.Forms;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.ServiceRequests.Services;

public interface IStaffServiceRequestApiClient
{
    Task<ApiResult<StaffServiceRequestListResponse>> ListAsync(StaffServiceRequestListQuery query, CancellationToken ct = default);
    Task<ApiResult<StaffServiceRequestDetailResponse>> GetAsync(string requestNumber, CancellationToken ct = default);
    Task<ApiResult<StaffServiceRequestDetailResponse>> AcceptAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<StaffServiceRequestDetailResponse>> ProgressAsync(Guid id, UpdateStaffServiceRequestProgressRequest request, CancellationToken ct = default);
    Task<ApiResult<StaffServiceRequestDetailResponse>> CompleteAsync(Guid id, CompleteStaffServiceRequestWorkRequest request, CancellationToken ct = default);
    Task<ApiResult<StaffServiceRequestDetailResponse>> UploadEvidenceAsync(Guid id, IReadOnlyList<IBrowserFile> files, CancellationToken ct = default);
    Task<ApiResult<ApiFileContent>> DownloadEvidenceAsync(string downloadUrl, CancellationToken ct = default);
}

public sealed class StaffServiceRequestApiClient(AuthenticatedApiClient api) : IStaffServiceRequestApiClient
{
    public Task<ApiResult<StaffServiceRequestListResponse>> ListAsync(StaffServiceRequestListQuery query, CancellationToken ct = default)
    {
        var values = new List<string> { $"page={query.Page}", $"pageSize={query.PageSize}" };
        Add(values, "search", query.Search);
        Add(values, "status", query.Status);
        return api.SendAsync<StaffServiceRequestListResponse>(HttpMethod.Get,
            $"api/v1/staff/service-requests?{string.Join("&", values)}", ct: ct);
    }

    public Task<ApiResult<StaffServiceRequestDetailResponse>> GetAsync(string requestNumber, CancellationToken ct = default) =>
        api.SendAsync<StaffServiceRequestDetailResponse>(HttpMethod.Get,
            $"api/v1/staff/service-requests/{Uri.EscapeDataString(requestNumber)}", ct: ct);

    public Task<ApiResult<StaffServiceRequestDetailResponse>> AcceptAsync(Guid id, CancellationToken ct = default) =>
        api.SendAsync<StaffServiceRequestDetailResponse>(HttpMethod.Post, $"api/v1/staff/service-requests/{id}/accept", new { }, ct: ct);

    public Task<ApiResult<StaffServiceRequestDetailResponse>> ProgressAsync(Guid id, UpdateStaffServiceRequestProgressRequest request, CancellationToken ct = default) =>
        api.SendAsync<StaffServiceRequestDetailResponse>(HttpMethod.Post, $"api/v1/staff/service-requests/{id}/progress", request, ct: ct);

    public Task<ApiResult<StaffServiceRequestDetailResponse>> CompleteAsync(Guid id, CompleteStaffServiceRequestWorkRequest request, CancellationToken ct = default) =>
        api.SendAsync<StaffServiceRequestDetailResponse>(HttpMethod.Post, $"api/v1/staff/service-requests/{id}/complete", request, ct: ct);

    public async Task<ApiResult<StaffServiceRequestDetailResponse>> UploadEvidenceAsync(Guid id, IReadOnlyList<IBrowserFile> files, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        foreach (var file in files)
        {
            var fileContent = new StreamContent(file.OpenReadStream(10 * 1024 * 1024, ct));
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
            content.Add(fileContent, "files", file.Name);
        }
        return await api.SendMultipartAsync<StaffServiceRequestDetailResponse>(
            $"api/v1/staff/service-requests/{id}/evidence", content, ct);
    }

    public Task<ApiResult<ApiFileContent>> DownloadEvidenceAsync(string downloadUrl, CancellationToken ct = default) =>
        api.GetFileAsync(downloadUrl.TrimStart('/'), ct);

    private static void Add(ICollection<string> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) values.Add($"{key}={Uri.EscapeDataString(value.Trim())}");
    }
}
