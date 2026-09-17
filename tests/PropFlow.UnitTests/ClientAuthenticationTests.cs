using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Tokens;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Services.Authentication;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-01")]
public sealed class ClientAuthenticationTests
{
    private sealed class Transport(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request);
    }
    private sealed class Navigation : NavigationManager
    {
        public Navigation() => Initialize("https://test.invalid/", "https://test.invalid/account");
        public string? Destination { get; private set; }
        protected override void NavigateToCore(string uri, bool forceLoad) => Destination = uri;
    }
    private static HttpResponseMessage Json<T>(T body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    private static SessionResponse Session(string token) => new(token, DateTimeOffset.UtcNow.AddMinutes(15),
        new(Guid.NewGuid(), "resident", "Cư dân", "test@example.invalid", null, "ACTIVE", true, "RESIDENT", []));
    private static AuthSession CreateSession(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) =>
        new(new ApiClient(new HttpClient(new Transport(send)) { BaseAddress = new("https://test.invalid/") }, NullLogger<ApiClient>.Instance));

    [Fact]
    public async Task Concurrent_401s_share_one_refresh_and_replay_once_with_new_bearer()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshes = 0;
        var session = CreateSession(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("csrf")) return Json(new CsrfResponse("csrf"));
            Interlocked.Increment(ref refreshes); started.TrySetResult(); await release.Task;
            return Json(Session("new"));
        });
        session.Accept(Session("old"));
        var replays = 0; var originals = 0; var navigation = new Navigation();
        using var http = new HttpClient(new AuthHttpHandler(session, navigation) { InnerHandler = new Transport(request =>
        {
            if (request.Headers.Authorization?.Parameter == "old") { Interlocked.Increment(ref originals); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)); }
            Assert.Equal("new", request.Headers.Authorization?.Parameter); Interlocked.Increment(ref replays);
            return Task.FromResult(Json(new { success = true }));
        }) }) { BaseAddress = new("https://test.invalid/") };
        var first = http.GetAsync("api/v1/resource"); await started.Task;
        var second = http.GetAsync("api/v1/resource"); release.TrySetResult();
        var results = await Task.WhenAll(first, second);
        Assert.All(results, response => { Assert.Equal(HttpStatusCode.OK, response.StatusCode); response.Dispose(); });
        Assert.Equal(1, refreshes); Assert.Equal(2, originals); Assert.Equal(2, replays); Assert.Null(navigation.Destination);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Non_401_does_not_refresh_or_clear_authentication(HttpStatusCode status)
    {
        var calls = 0; var navigation = new Navigation();
        var session = CreateSession(_ => { calls++; return Task.FromResult(Json(Session("unexpected"))); });
        session.Accept(Session("old"));
        using var http = new HttpClient(new AuthHttpHandler(session, navigation) { InnerHandler = new Transport(_ => Task.FromResult(new HttpResponseMessage(status))) });
        using var result = await http.GetAsync("https://test.invalid/api/v1/resource");
        Assert.Equal(status, result.StatusCode); Assert.Equal(0, calls); Assert.Equal("old", session.AccessToken); Assert.Null(navigation.Destination);
    }

    [Fact]
    public async Task Refresh_failure_clears_state_and_redirects_without_retry_loop()
    {
        var session = CreateSession(request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("csrf")
            ? Json(new CsrfResponse("csrf")) : new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        session.Accept(Session("old")); var navigation = new Navigation(); var calls = 0;
        using var http = new HttpClient(new AuthHttpHandler(session, navigation) { InnerHandler = new Transport(_ => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)); }) });
        using var result = await http.GetAsync("https://test.invalid/api/v1/resource");
        Assert.Equal(1, calls); Assert.Null(session.AccessToken); Assert.Null(session.User); Assert.Equal("/login", navigation.Destination);
        Assert.False((await session.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
    }

    [Fact]
    public async Task Reload_initialization_restores_from_cookie_once_without_persisted_bearer()
    {
        var calls = 0;
        var session = CreateSession(request =>
        {
            Assert.Null(request.Headers.Authorization);
            if (request.RequestUri!.AbsolutePath.EndsWith("csrf")) return Task.FromResult(Json(new CsrfResponse("csrf")));
            calls++; return Task.FromResult(Json(Session("restored")));
        });
        Assert.Null(session.AccessToken);
        await Task.WhenAll(session.InitializeAsync(), session.InitializeAsync());
        Assert.Equal(1, calls); Assert.Equal("restored", session.AccessToken);
        Assert.True((await session.GetAuthenticationStateAsync()).User.IsInRole("RESIDENT"));
    }

    [Fact]
    public async Task Anonymous_login_page_does_not_show_background_refresh_network_failure()
    {
        var session = CreateSession(_ => throw new HttpRequestException("API unavailable"));
        await session.InitializeAsync();
        Assert.Null(session.AccessToken);
        Assert.Null(session.Notice);

        var login = await session.LoginAsync(new LoginRequest("resident", "password"));
        Assert.False(login.IsSuccess);
        Assert.Equal("network", login.Code);
    }

    [Fact]
    public async Task Logout_invalidates_inflight_refresh_before_waiting_for_server()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = CreateSession(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("csrf")) return Json(new CsrfResponse("csrf"));
            if (request.RequestUri.AbsolutePath.EndsWith("logout")) return new(HttpStatusCode.NoContent);
            started.TrySetResult(); await release.Task; return Json(Session("stale"));
        });
        session.Accept(Session("old")); var refresh = session.RefreshAsync("old"); await started.Task;
        var logout = session.LogoutAsync(); Assert.Null(session.AccessToken);
        release.TrySetResult(); await Task.WhenAll(refresh, logout);
        Assert.Null(session.AccessToken); Assert.Null(session.User); Assert.True((await logout).IsSuccess);
    }

    [Fact]
    public void Recovery_proof_expires_and_cannot_be_used_twice()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = new PasswordResetToken(Guid.NewGuid(), "otp-hash", now.AddMinutes(5), now);
        expired.AuthorizeReset("proof-hash", now.AddMinutes(5), now);
        Assert.False(expired.CanResetAt(now.AddMinutes(5)));
        Assert.Throws<InvalidOperationException>(() => expired.ConsumeProof(now.AddMinutes(5)));
        var active = new PasswordResetToken(Guid.NewGuid(), "otp-hash", now.AddMinutes(5), now);
        active.AuthorizeReset("proof-hash", now.AddMinutes(5), now); active.ConsumeProof(now);
        Assert.False(active.CanResetAt(now)); Assert.Throws<InvalidOperationException>(() => active.ConsumeProof(now));
    }
}
