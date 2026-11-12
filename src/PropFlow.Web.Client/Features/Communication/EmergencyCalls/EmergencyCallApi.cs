using PropFlow.Modules.Communication.Contracts;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.Communication.EmergencyCalls;

public sealed class EmergencyCallApi(AuthenticatedApiClient api)
{
    private const string Root = "api/v1/emergency-calls";
    public Task<ApiResult<EmergencyCallView>> Start(StartEmergencyCall request, CancellationToken ct) => api.SendAsync<EmergencyCallView>(HttpMethod.Post, Root, request, ct: ct);
    public Task<ApiResult<EmergencyActiveCall>> Active(string connection, CancellationToken ct) => api.SendAsync<EmergencyActiveCall>(HttpMethod.Get, $"{Root}/active?connectionId={Uri.EscapeDataString(connection)}", ct: ct);
    public Task<ApiResult<EmergencyCallView>> End(Guid id, CancellationToken ct) => api.SendAsync<EmergencyCallView>(HttpMethod.Post, $"{Root}/{id}/end", ct: ct);
    public Task<ApiResult<EmergencyCallView>> Retry(Guid id, CancellationToken ct) => api.SendAsync<EmergencyCallView>(HttpMethod.Post, $"{Root}/{id}/retry", ct: ct);
    public Task<ApiResult<EmergencyRtcConfiguration>> Configuration(CancellationToken ct) => api.SendAsync<EmergencyRtcConfiguration>(HttpMethod.Get, $"{Root}/rtc", ct: ct);
}
