using PropFlow.Web.Client.Features.Resident.Models;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.Resident.Services;
public interface IResidentApiClient
{
    Task<ApiResult<PagedResidentsResponse>> ListAsync(string? search, string? status, Guid? apartmentUnitId = null,
        string? relationshipKind = null, string? residencyType = null, string? householdRole = null,
        int page = 1, CancellationToken ct = default);
    Task<ApiResult<ResidentDetailResponse>> GetAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<ResidentDetailResponse>> CreateAsync(CreateResidentRequest request, CancellationToken ct = default);
    Task<ApiResult<EmptyResponse>> UpdateAsync(Guid id, UpdateResidentRequest request, CancellationToken ct = default);
    Task<ApiResult<EmptyResponse>> SetStatusAsync(Guid id, string status, CancellationToken ct = default);
    Task<ApiResult<ResidencyItem>> AddResidencyAsync(Guid id, CreateResidencyRequest request, CancellationToken ct = default);
    Task<ApiResult<ResidencyItem>> MoveResidencyAsync(Guid id, MoveResidencyRequest request, CancellationToken ct = default);
    Task<ApiResult<EmptyResponse>> EndResidencyAsync(Guid id, Guid residencyId, DateOnly endDate, CancellationToken ct = default);
    Task<ApiResult<ResidentDetailResponse>> MeAsync(CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<ActiveApartmentOption>>> ActiveApartmentsAsync(CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<ActiveApartmentOption>>> ResidencyCandidatesAsync(Guid residentId, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<EligibleHouseholdHead>>> EligibleHeadsAsync(Guid apartmentUnitId, CancellationToken ct = default);
}
public sealed class ResidentApiClient(AuthenticatedApiClient api) : IResidentApiClient
{
    public Task<ApiResult<PagedResidentsResponse>> ListAsync(string? search, string? status, Guid? apartmentUnitId = null,
        string? relationshipKind = null, string? residencyType = null, string? householdRole = null,
        int page = 1, CancellationToken ct = default) =>
        api.SendAsync<PagedResidentsResponse>(HttpMethod.Get, $"api/v1/residents?pageIndex={page}&pageSize=20" +
            (string.IsNullOrWhiteSpace(search) ? "" : $"&search={Uri.EscapeDataString(search)}") +
            (string.IsNullOrWhiteSpace(status) ? "" : $"&status={status}") +
            (apartmentUnitId.HasValue ? $"&apartmentUnitId={apartmentUnitId}" : "") +
            (string.IsNullOrWhiteSpace(relationshipKind) ? "" : $"&relationshipKind={relationshipKind}") +
            (string.IsNullOrWhiteSpace(residencyType) ? "" : $"&residencyType={residencyType}") +
            (string.IsNullOrWhiteSpace(householdRole) ? "" : $"&householdRole={householdRole}"), ct: ct);
    public Task<ApiResult<ResidentDetailResponse>> GetAsync(Guid id, CancellationToken ct = default) => api.SendAsync<ResidentDetailResponse>(HttpMethod.Get, $"api/v1/residents/{id}", ct: ct);
    public Task<ApiResult<ResidentDetailResponse>> CreateAsync(CreateResidentRequest request, CancellationToken ct = default) => api.SendAsync<ResidentDetailResponse>(HttpMethod.Post, "api/v1/residents", request, ct: ct);
    public Task<ApiResult<EmptyResponse>> UpdateAsync(Guid id, UpdateResidentRequest request, CancellationToken ct = default) => api.SendAsync<EmptyResponse>(HttpMethod.Put, $"api/v1/residents/{id}", request, ct: ct);
    public Task<ApiResult<EmptyResponse>> SetStatusAsync(Guid id, string status, CancellationToken ct = default) => api.SendAsync<EmptyResponse>(HttpMethod.Patch, $"api/v1/residents/{id}/status", new SetResidentStatusRequest { Status = status }, ct: ct);
    public Task<ApiResult<ResidencyItem>> AddResidencyAsync(Guid id, CreateResidencyRequest request, CancellationToken ct = default) => api.SendAsync<ResidencyItem>(HttpMethod.Post, $"api/v1/residents/{id}/residencies", request, ct: ct);
    public Task<ApiResult<ResidencyItem>> MoveResidencyAsync(Guid id, MoveResidencyRequest request, CancellationToken ct = default) => api.SendAsync<ResidencyItem>(HttpMethod.Post, $"api/v1/residents/{id}/residencies/move", request, ct: ct);
    public Task<ApiResult<EmptyResponse>> EndResidencyAsync(Guid id, Guid residencyId, DateOnly endDate, CancellationToken ct = default) => api.SendAsync<EmptyResponse>(HttpMethod.Patch, $"api/v1/residents/{id}/residencies/{residencyId}/end", new EndResidencyRequest { EndDate = endDate }, ct: ct);
    public Task<ApiResult<ResidentDetailResponse>> MeAsync(CancellationToken ct = default) => api.SendAsync<ResidentDetailResponse>(HttpMethod.Get, "api/v1/residents/me", ct: ct);
    public Task<ApiResult<IReadOnlyList<ActiveApartmentOption>>> ActiveApartmentsAsync(CancellationToken ct = default) => api.SendAsync<IReadOnlyList<ActiveApartmentOption>>(HttpMethod.Get, "api/v1/residents/apartments", ct: ct);
    public Task<ApiResult<IReadOnlyList<ActiveApartmentOption>>> ResidencyCandidatesAsync(Guid residentId, CancellationToken ct = default) => api.SendAsync<IReadOnlyList<ActiveApartmentOption>>(HttpMethod.Get, $"api/v1/residents/{residentId}/residency-candidates", ct: ct);
    public Task<ApiResult<IReadOnlyList<EligibleHouseholdHead>>> EligibleHeadsAsync(Guid apartmentUnitId, CancellationToken ct = default) => api.SendAsync<IReadOnlyList<EligibleHouseholdHead>>(HttpMethod.Get, $"api/v1/residents/apartments/{apartmentUnitId}/eligible-household-heads", ct: ct);
}
