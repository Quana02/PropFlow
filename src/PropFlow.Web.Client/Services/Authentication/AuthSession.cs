using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Services.Authentication;

public enum SessionRevocationState { None, Revoking, Unconfirmed }

public sealed class AuthSession(ApiClient api, IJSRuntime? js = null) : AuthenticationStateProvider
{
    private const string RevocationMarker = "propflow:logout-unconfirmed";
    private readonly SemaphoreSlim cookieLock = new(1, 1);
    private Task? initialization;
    internal int Version { get; private set; }
    public string? AccessToken { get; private set; }
    public DateTimeOffset? AccessTokenExpiresAt { get; private set; }
    public AccountResponse? User { get; private set; }
    public string? Notice { get; private set; }
    public SessionRevocationState Revocation { get; private set; } = ReadRevocationMarker(js)
        ? SessionRevocationState.Unconfirmed : SessionRevocationState.None;
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
        Version++;
        SetSession(response);
    }

    private void SetSession(SessionResponse response)
    {
        AccessToken = response.AccessToken;
        AccessTokenExpiresAt = response.ExpiresAt;
        User = response.User;
        Notice = null;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
    public void UpdateUser(AccountResponse user)
    {
        // A profile response can arrive after logout or after another account signs in.
        if (AccessToken is null || User?.Id != user.Id) return;
        User = user;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
    public void Clear(string? notice = null)
    {
        Version++;
        ResetSession(notice);
    }

    private void ResetSession(string? notice)
    {
        AccessToken = null; AccessTokenExpiresAt = null; User = null; Notice = notice;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
    public async Task<string?> GetValidAccessTokenAsync()
    {
        await InitializeAsync();
        var token = AccessToken;
        if (token is null) return null;

        // SignalR can ask for the token more than once while negotiating transports.
        // Rotating a healthy token on every call races the refresh cookie and can tear down
        // an otherwise valid Hub connection. Refresh only when the token is nearly expired.
        if (AccessTokenExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow.AddSeconds(30))
            await RefreshAsync(token);

        return AccessToken;
    }
    public async Task<bool> RefreshAsync(string? rejectedToken)
    {
        var version = Version;
        if (Revocation != SessionRevocationState.None) return false;
        await cookieLock.WaitAsync();
        try
        {
            if (Version != version || Revocation != SessionRevocationState.None) return false;
            if (AccessToken != null && AccessToken != rejectedToken) return true;
            if (AccessToken is null && rejectedToken is not null) return false;
            var userId = User?.Id;
            var result = await api.SendAsync<SessionResponse>(HttpMethod.Post, "api/v1/auth/refresh", csrf: true);
            if (Version != version) return false;
            if (result.IsSuccess)
            {
                if (userId is null || result.Data!.User.Id == userId)
                {
                    SetSession(result.Data!);
                    return true;
                }
                ResetSession("Phiên đăng nhập đã thay đổi. Vui lòng đăng nhập lại.");
                return false;
            }
            // An anonymous visit has no session to expire. A failed background restore
            // must not display an API outage as a login-form error before submission.
            ResetSession(rejectedToken == null ? null : result.Message ?? "Phiên đăng nhập đã hết hạn.");
            return false;
        }
        finally { cookieLock.Release(); }
    }
    public async Task<ApiResult<SessionResponse>> LoginAsync(LoginRequest request)
    {
        await cookieLock.WaitAsync();
        try
        {
            // A new login would replace the cookie needed to retry revoking the previous session.
            if (Revocation != SessionRevocationState.None)
                return new(false, Code: "revocation_pending", Message: "Vui lòng thử thu hồi phiên trước khi đăng nhập lại.");
            var version = Version;
            var result = await api.SendAsync<SessionResponse>(HttpMethod.Post, "api/v1/auth/login", request, csrf: true);
            if (Version != version) return new(false, Code: "session_changed");
            if (result.IsSuccess) Accept(result.Data!);
            return result;
        }
        finally { cookieLock.Release(); }
    }
    public async Task<ApiResult<EmptyResponse>> LogoutAsync()
    {
        if (Revocation == SessionRevocationState.Revoking) return new(false, Code: "revocation_in_progress");
        if (User is null && Revocation == SessionRevocationState.None) return new(true, new EmptyResponse());

        Revocation = SessionRevocationState.Revoking;
        WriteRevocationMarker(pending: true);
        // Invalidate in-flight refresh and protected requests before waiting for the API.
        Clear();
        await cookieLock.WaitAsync();
        try
        {
            var result = await api.SendAsync<EmptyResponse>(HttpMethod.Post, "api/v1/auth/logout", csrf: true);
            if (result.IsSuccess && result.StatusCode == 204)
            {
                Revocation = SessionRevocationState.None;
                WriteRevocationMarker(pending: false);
            }
            else if (result.IsSuccess)
                return new(false, StatusCode: result.StatusCode, Code: "revocation_unconfirmed");
            return result;
        }
        finally
        {
            if (Revocation == SessionRevocationState.Revoking) Revocation = SessionRevocationState.Unconfirmed;
            cookieLock.Release();
            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        }
    }

    private static bool ReadRevocationMarker(IJSRuntime? runtime)
    {
        try { return runtime is IJSInProcessRuntime browser && browser.Invoke<string?>("sessionStorage.getItem", RevocationMarker) == "1"; }
        catch (JSException) { return false; }
    }

    private void WriteRevocationMarker(bool pending)
    {
        if (js is not IJSInProcessRuntime browser) return;
        try
        {
            if (pending) browser.InvokeVoid("sessionStorage.setItem", RevocationMarker, "1");
            else browser.InvokeVoid("sessionStorage.removeItem", RevocationMarker);
        }
        catch (JSException) { /* The state remains active in memory if browser storage is unavailable. */ }
    }
}
