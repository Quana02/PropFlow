using PropFlow.Modules.Administration.Contracts;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.Administration.Services;

public interface ISupportContactApi
{
    Task<ApiResult<SupportContactsState>> ReadAsync(CancellationToken ct = default);
    Task<ApiResult<SupportContactsState>> UpdateAsync(UpdateSupportContacts input, CancellationToken ct = default);
    Task<ApiResult<PublicSupportContacts>> PublicAsync(CancellationToken ct = default);
}
public sealed class SupportContactApi(AuthenticatedApiClient authenticated, ApiClient anonymous) : ISupportContactApi
{
    private const string Route = "api/v1/administration/system-configuration/support-contacts";
    public Task<ApiResult<SupportContactsState>> ReadAsync(CancellationToken ct = default) => authenticated.SendAsync<SupportContactsState>(HttpMethod.Get, Route, ct: ct);
    public Task<ApiResult<SupportContactsState>> UpdateAsync(UpdateSupportContacts input, CancellationToken ct = default) => authenticated.SendAsync<SupportContactsState>(HttpMethod.Put, Route, input, csrf: true, ct: ct);
    public Task<ApiResult<PublicSupportContacts>> PublicAsync(CancellationToken ct = default) => anonymous.SendAsync<PublicSupportContacts>(HttpMethod.Get, "api/v1/public/support-contacts", ct: ct);
}
