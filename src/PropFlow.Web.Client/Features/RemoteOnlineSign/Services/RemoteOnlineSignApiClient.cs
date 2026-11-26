using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components.Forms;
using PropFlow.Web.Client.Features.RemoteOnlineSign.Models;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.RemoteOnlineSign.Services;

public sealed class RemoteOnlineSignApiClient(AuthenticatedApiClient api)
{
    private const string Base = "api/v1/remote-online-sign";
    public Task<ApiResult<RemoteContractSummary[]>> ListAsync(CancellationToken ct = default) => api.SendAsync<RemoteContractSummary[]>(HttpMethod.Get, Base, ct: ct);
    public Task<ApiResult<RemoteResidentCandidate[]>> SearchResidentsAsync(string? search, CancellationToken ct = default) => api.SendAsync<RemoteResidentCandidate[]>(HttpMethod.Get, $"{Base}/resident-candidates?search={Uri.EscapeDataString(search ?? string.Empty)}", ct: ct);
    public Task<ApiResult<RemoteContractSummary>> GetAsync(Guid id, CancellationToken ct = default) => api.SendAsync<RemoteContractSummary>(HttpMethod.Get, $"{Base}/{id}", ct: ct);
    public Task<ApiResult<RemoteContractSummary>> CreateAsync(CreateRemoteContractRequest request, CancellationToken ct = default) => api.SendAsync<RemoteContractSummary>(HttpMethod.Post, Base, request, ct: ct);
    public Task<ApiResult<RemoteContractSummary>> SelectSigningModeAsync(Guid id, SelectRemoteSigningModeRequest request, CancellationToken ct = default) => api.SendAsync<RemoteContractSummary>(HttpMethod.Put, $"{Base}/{id}/signing-mode", request, ct: ct);
    public Task<ApiResult<RemoteContractSummary>> UpdateMetadataAsync(Guid id, UpdateRemoteContractMetadataRequest request, CancellationToken ct = default) => api.SendAsync<RemoteContractSummary>(HttpMethod.Put, $"{Base}/{id}/metadata", request, ct: ct);
    public Task<ApiResult<RemoteContractSummary>> RenewDeadlineAsync(Guid id, RenewDeadlineRequest request, CancellationToken ct = default) => api.SendAsync<RemoteContractSummary>(HttpMethod.Put, $"{Base}/{id}/deadline", request, ct: ct);
    public Task<ApiResult<RemoteContractSummary>> AssignResidentAsync(Guid id, AssignResidentRequest request, CancellationToken ct = default) => api.SendAsync<RemoteContractSummary>(HttpMethod.Put, $"{Base}/{id}/resident", request, ct: ct);
    public async Task<ApiResult> SetFieldsAsync(Guid id, IReadOnlyList<SignatureFieldInput> request, CancellationToken ct = default) => Empty(await api.SendAsync<EmptyResponse>(HttpMethod.Put, $"{Base}/{id}/signature-fields", request, ct: ct));
    public async Task<ApiResult> SendAsync(Guid id, SendRemoteContractRequest request, CancellationToken ct = default) => Empty(await api.SendAsync<EmptyResponse>(HttpMethod.Post, $"{Base}/{id}/send", request, ct: ct));
    public Task<ApiResult<SigningChallenge>> BeginSigningAsync(Guid id, BeginSignatureRequest request, CancellationToken ct = default) => api.SendAsync<SigningChallenge>(HttpMethod.Post, $"{Base}/{id}/signatures/request", request, ct: ct);
    public Task<ApiResult<RemoteContractSummary>> VerifyAsync(Guid id, VerifySignatureRequest request, CancellationToken ct = default) => api.SendAsync<RemoteContractSummary>(HttpMethod.Post, $"{Base}/{id}/signatures/verify", request, ct: ct);
    public async Task<ApiResult> RejectAsync(Guid id, string reason, CancellationToken ct = default) => Empty(await api.SendAsync<EmptyResponse>(HttpMethod.Post, $"{Base}/{id}/reject", new RejectContractRequest(reason), ct: ct));
    public Task<ApiResult<RemoteContractSummary>> ReassignResidentAsync(Guid id, ReassignResidentRequest request, CancellationToken ct = default) => api.SendAsync<RemoteContractSummary>(HttpMethod.Post, $"{Base}/{id}/reassign-resident", request, ct: ct);
    public Task<ApiResult<RemoteAuditEvent[]>> HistoryAsync(Guid id, CancellationToken ct = default) => api.SendAsync<RemoteAuditEvent[]>(HttpMethod.Get, $"{Base}/{id}/history", ct: ct);
    public Task<ApiResult<RemoteSignatureField[]>> FieldsAsync(Guid id, CancellationToken ct = default) => api.SendAsync<RemoteSignatureField[]>(HttpMethod.Get, $"{Base}/{id}/signature-fields", ct: ct);
    public Task<ApiResult<RemoteDocumentMetadata>> MetadataAsync(Guid id, CancellationToken ct = default) => api.SendAsync<RemoteDocumentMetadata>(HttpMethod.Get, $"{Base}/{id}/document/metadata", ct: ct);
    public Task<ApiResult<ApiFileContent>> DownloadAsync(Guid id, CancellationToken ct = default) => api.GetFileAsync($"{Base}/{id}/document", ct);
    public async Task<ApiResult<RemoteDocumentMetadata>> UploadAsync(Guid id, IBrowserFile file, CancellationToken ct = default)
    {
        using var body = new MultipartFormDataContent();
        await using var stream = file.OpenReadStream(20 * 1024 * 1024, ct);
        var content = new StreamContent(stream); content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType ?? "application/pdf");
        body.Add(content, "file", file.Name); return await api.SendMultipartAsync<RemoteDocumentMetadata>(HttpMethod.Post, $"{Base}/{id}/document", body, ct);
    }
    public async Task<ApiResult<RemoteDocumentMetadata>> UploadAsync(Guid id, string fileName, string contentType, byte[] bytes, CancellationToken ct = default)
    {
        using var body = new MultipartFormDataContent();
        using var content = new ByteArrayContent(bytes); content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        body.Add(content, "file", fileName); return await api.SendMultipartAsync<RemoteDocumentMetadata>(HttpMethod.Post, $"{Base}/{id}/document", body, ct);
    }
    private static ApiResult Empty(ApiResult<EmptyResponse> result) => new(result.IsSuccess, result.StatusCode, result.Code, result.Message, result.TraceId, result.ValidationErrors);
}
