using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Services.Authentication;

public sealed class AuthSession(ApiClient api) : AuthenticationStateProvider
{
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private Task? initialization;
    private int epoch;
    public string? AccessToken { get; private set; }
    public AccountResponse? User { get; private set; }
    public string? Notice { get; private set; }
    public Task InitializeAsync() => initialization ??= RestoreAsync();
    private async Task RestoreAsync()
    {
        await RefreshAsync(null);
    }
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var claims = User == null ? new ClaimsIdentity() : new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, User.Id.ToString()), new Claim(ClaimTypes.Name, User.DisplayName), new Claim(ClaimTypes.Role, User.Role) }
                .Concat(User.Permissions.Select(x => new Claim("permission", x))), "PropFlow");
        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(claims)));
    }
    public void Accept(SessionResponse response)
    {
        epoch++;
        AccessToken = response.AccessToken;
        User = response.User;
        Notice = null;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
    public void UpdateUser(AccountResponse user) { User = user; NotifyAuthenticationStateChanged(GetAuthenticationStateAsync()); }
    public void Clear(string? notice = null)
    {
        epoch++;
        AccessToken = null; User = null; Notice = notice;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
    public async Task<bool> RefreshAsync(string? rejectedToken)
    {
        var requestedEpoch = epoch;
        await refreshLock.WaitAsync();
        try
        {
            if (epoch != requestedEpoch) return AccessToken != null;
            if (AccessToken != null && AccessToken != rejectedToken) return true;
            var result = await api.SendAsync<SessionResponse>(HttpMethod.Post, "api/v1/auth/refresh", csrf: true);
            if (epoch != requestedEpoch) return AccessToken != null;
            if (result.IsSuccess) { Accept(result.Data!); return true; }
            // An anonymous visit has no session to expire. A failed background restore
            // must not display an API outage as a login-form error before submission.
            Clear(rejectedToken == null ? null : result.Message ?? "Phiên đăng nhập đã hết hạn.");
            return false;
        }
        finally { refreshLock.Release(); }
    }
    public async Task<ApiResult<SessionResponse>> LoginAsync(LoginRequest request)
    {
        var result = await api.SendAsync<SessionResponse>(HttpMethod.Post, "api/v1/auth/login", request, csrf: true);
        if (result.IsSuccess) Accept(result.Data!);
        return result;
    }
    public async Task<ApiResult<EmptyResponse>> LogoutAsync()
    {
        // Increment the epoch before awaiting network so an in-flight refresh cannot restore local login.
        Clear();
        await refreshLock.WaitAsync();
        try
        {
            var result = await api.SendAsync<EmptyResponse>(HttpMethod.Post, "api/v1/auth/logout", csrf: true);
            Clear(result.IsSuccess ? "Bạn đã đăng xuất." : "Đã xóa phiên trên trang này nhưng máy chủ chưa xác nhận thu hồi phiên. Vui lòng thử đăng xuất lại.");
            return result;
        }
        finally { refreshLock.Release(); }
    }
}
