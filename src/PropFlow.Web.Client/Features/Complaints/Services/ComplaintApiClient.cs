using PropFlow.Web.Client.Features.Complaints.Models;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.Complaints.Services;

public sealed class ComplaintApiClient(AuthenticatedApiClient api)
{
    public Task<ApiResult<ComplaintPage>> ListAsync(string search, string status, int page, CancellationToken ct) =>
        api.SendAsync<ComplaintPage>(HttpMethod.Get, $"api/v1/complaints?search={Uri.EscapeDataString(search)}&status={Uri.EscapeDataString(status)}&page={page}", ct: ct);
    public Task<ApiResult<ComplaintDetail>> GetAsync(Guid id, CancellationToken ct) => api.SendAsync<ComplaintDetail>(HttpMethod.Get, $"api/v1/complaints/{id}", ct: ct);
    public Task<ApiResult<ComplaintDetail>> SubmitAsync(string subject, string description, CancellationToken ct) =>
        api.SendAsync<ComplaintDetail>(HttpMethod.Post, "api/v1/complaints", new { subject, description }, ct: ct);
    public Task<ApiResult<ComplaintDetail>> ActionAsync(Guid id, string action, object? body, CancellationToken ct) =>
        api.SendAsync<ComplaintDetail>(action == "context" || action.StartsWith("followups/") ? HttpMethod.Put : HttpMethod.Post,
            $"api/v1/complaints/{id}/{action}", body, ct: ct);
    public Task<ApiResult<IReadOnlyList<StaffOption>>> StaffAsync(CancellationToken ct) =>
        api.SendAsync<IReadOnlyList<StaffOption>>(HttpMethod.Get, "api/v1/complaints/staff-options", ct: ct);
    public Task<ApiResult<ComplaintContextOptions>> ContextAsync(string search, CancellationToken ct) =>
        api.SendAsync<ComplaintContextOptions>(HttpMethod.Get, $"api/v1/complaints/context-options?search={Uri.EscapeDataString(search)}", ct: ct);
}
