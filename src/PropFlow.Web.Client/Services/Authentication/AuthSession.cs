using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Services.Authentication;

public enum SessionRevocationState { None, Revoking, Unconfirmed }

public sealed class AuthSession(ApiClient api, IJSRuntime? js = null) : AuthenticationStateProvider, IAsyncDisposable
{
    private const string RevocationMarker = "propflow:logout-unconfirmed";
    private const string PendingSessionMarker = "propflow:logout-session";
    private readonly SemaphoreSlim cookieLock = new(1, 1);
    private Task? initialization;
    private Task? transportCheck;
    private DotNetObjectReference<AuthSession>? browserReference;
    private IJSObjectReference? tabModule;
    private IJSObjectReference? tabChannel;
    private Guid? sessionId;
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
        if (js is not null)
        {
            try
            {
                tabModule = await js.InvokeAsync<IJSObjectReference>("import", "./js/session-tabs.js");
                browserReference = DotNetObjectReference.Create(this);
                tabChannel = await tabModule.InvokeAsync<IJSObjectReference>("observeLogout", browserReference);
            }
            catch (JSException) { /* HTTP and SignalR still enforce server-side validity. */ }
        }
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
        sessionId = ReadSessionId(response.AccessToken);
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
        sessionId = null;
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
            var expectedSession = sessionId;
            var headers = SessionHeaders(expectedSession);
            var result = await api.SendAsync<SessionResponse>(HttpMethod.Post, "api/v1/auth/refresh", csrf: true, headers: headers);
            // Another tab may have just rotated the shared cookie. Retry once with the cookie the browser now holds.
            if (js is not null && result.Code == "invalid_credentials" && Version == version)
            {
                await Task.Delay(120);
                if (Version != version) return false;
                result = await api.SendAsync<SessionResponse>(HttpMethod.Post, "api/v1/auth/refresh", csrf: true, headers: headers);
            }
            if (Version != version) return false;
            if (result.IsSuccess)
            {
                if ((userId is null || result.Data!.User.Id == userId)
                    && (expectedSession is null || ReadSessionId(result.Data!.AccessToken) == expectedSession))
                {
                    SetSession(result.Data!);
                    return true;
                }
                ResetSession("Phiên đăng nhập đã thay đổi. Vui lòng đăng nhập lại.");
                return false;
            }
            // An anonymous visit has no session to expire. A failed background restore
            // must not display an API outage as a login-form error before submission.
            if (result.StatusCode == 401 || rejectedToken is null)
                ResetSession(rejectedToken == null ? null : "Phiên đăng nhập không còn hiệu lực. Vui lòng đăng nhập lại.");
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

        var endingSession = sessionId ?? ReadPendingSession();
        Revocation = SessionRevocationState.Revoking;
        WriteRevocationMarker(pending: true, endingSession);
        // Invalidate in-flight refresh and protected requests before waiting for the API.
        Clear();
        await cookieLock.WaitAsync();
        try
        {
            var result = await api.SendAsync<EmptyResponse>(HttpMethod.Post, "api/v1/auth/logout", csrf: true,
                headers: SessionHeaders(endingSession));
            if (result.IsSuccess && result.StatusCode == 204)
            {
                Revocation = SessionRevocationState.None;
                WriteRevocationMarker(pending: false);
                if (tabChannel is not null && endingSession is Guid ended)
                {
                    try { await tabChannel.InvokeVoidAsync("publish", ended.ToString()); }
                    catch (JSException) { /* The authoritative logout has already committed. */ }
                }
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

    private static Dictionary<string, string>? SessionHeaders(Guid? id) =>
        id is Guid value ? new() { ["X-PropFlow-Session"] = value.ToString() } : null;

    private static Guid? ReadSessionId(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3) return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
            return json.RootElement.TryGetProperty("sid", out var claim) && claim.TryGetGuid(out var id) ? id : null;
        }
        catch (FormatException) { return null; }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    [JSInvokable]
    public void SessionEndedInAnotherTab(string id)
    {
        if (Guid.TryParse(id, out var ended) && ended == sessionId)
            Clear("Phiên đăng nhập đã kết thúc ở một tab khác.");
    }

    public HubConnection ObserveTransport(HubConnection connection)
    {
        connection.Reconnecting += _ => CheckAfterTransportDisconnectAsync();
        connection.Closed += _ => CheckAfterTransportDisconnectAsync();
        return connection;
    }

    private Task CheckAfterTransportDisconnectAsync() =>
        transportCheck is { IsCompleted: false } running ? running : transportCheck = CheckTransportSessionAsync();

    private async Task CheckTransportSessionAsync()
    {
        if (AccessToken is not { } token || Revocation != SessionRevocationState.None) return;
        var version = Version;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        try
        {
            var result = await api.SendAsync<EmptyResponse>(HttpMethod.Get, "api/v1/auth/me",
                headers: new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" }, ct: timeout.Token);
            if (result.StatusCode == 401 && Version == version) await RefreshAsync(token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
    }

    private Guid? ReadPendingSession()
    {
        try
        {
            return js is IJSInProcessRuntime browser && Guid.TryParse(browser.Invoke<string?>("sessionStorage.getItem", PendingSessionMarker), out var id)
                ? id : null;
        }
        catch (JSException) { return null; }
    }

    private static bool ReadRevocationMarker(IJSRuntime? runtime)
    {
        try { return runtime is IJSInProcessRuntime browser && browser.Invoke<string?>("sessionStorage.getItem", RevocationMarker) == "1"; }
        catch (JSException) { return false; }
    }

    private void WriteRevocationMarker(bool pending, Guid? id = null)
    {
        if (js is not IJSInProcessRuntime browser) return;
        try
        {
            if (pending) browser.InvokeVoid("sessionStorage.setItem", RevocationMarker, "1");
            else browser.InvokeVoid("sessionStorage.removeItem", RevocationMarker);
            if (pending && id is Guid target) browser.InvokeVoid("sessionStorage.setItem", PendingSessionMarker, target.ToString());
            else browser.InvokeVoid("sessionStorage.removeItem", PendingSessionMarker);
        }
        catch (JSException) { /* The state remains active in memory if browser storage is unavailable. */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (tabChannel is not null)
        {
            await tabChannel.InvokeVoidAsync("close");
            await tabChannel.DisposeAsync();
        }
        browserReference?.Dispose();
        if (tabModule is not null) await tabModule.DisposeAsync();
    }
}
