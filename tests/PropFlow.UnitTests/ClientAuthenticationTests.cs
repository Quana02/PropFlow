using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Tokens;
using PropFlow.Web.Client.Features.Authentication.Services;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Services.Authentication;
using PropFlow.Web.Client.Shared.Feedback;

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
    private static AuthSession CreateSession(Func<HttpRequestMessage, Task<HttpResponseMessage>> send, IJSRuntime? js = null) =>
        new(new ApiClient(new HttpClient(new Transport(send)) { BaseAddress = new("https://test.invalid/") }, NullLogger<ApiClient>.Instance), js);

    [Fact]
    public async Task Late_profile_response_cannot_restore_a_logged_out_account()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var login = Session("manager-token");
        var manager = login.User with { Role = "MANAGER" };
        var session = CreateSession(request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("csrf")
            ? Json(new CsrfResponse("csrf"))
            : new HttpResponseMessage(HttpStatusCode.NoContent)));
        session.Accept(login with { User = manager });
        using var http = new HttpClient(new Transport(async _ =>
        {
            started.SetResult();
            await release.Task;
            return Json(manager with { DisplayName = "Tên mới" });
        })) { BaseAddress = new("https://test.invalid/") };
        var logger = NullLogger<ApiClient>.Instance;
        var service = new AuthenticationService(new ApiClient(http, logger), new AuthenticatedApiClient(http, logger), session, new ToastService());

        var save = service.UpdateAsync(new UpdateProfileRequest("Tên mới", null));
        await started.Task;
        Assert.True((await session.LogoutAsync()).IsSuccess);
        release.SetResult();

        Assert.True((await save).IsSuccess);
        Assert.Null(session.AccessToken);
        Assert.Null(session.User);
        Assert.False((await session.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
    }

    [Fact]
    public async Task Api_client_preserves_specific_problem_message_and_metadata()
    {
        using var http = new HttpClient(new Transport(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new
            {
                title = "Email đã được sử dụng.",
                code = "email_conflict",
                traceId = "trace-test",
                errors = new Dictionary<string, string[]> { ["Email"] = ["Email không hợp lệ."] }
            })
        }))) { BaseAddress = new("https://test.invalid/") };
        var api = new ApiClient(http, NullLogger<ApiClient>.Instance);

        var result = await api.SendAsync<EmptyResponse>(HttpMethod.Post, "api/v1/auth/register", new { });

        Assert.False(result.IsSuccess);
        Assert.Equal("email_conflict", result.Code);
        Assert.Equal("Email đã được sử dụng.", result.Message);
        Assert.Equal("trace-test", result.TraceId);
        Assert.Equal("Email không hợp lệ.", Assert.Single(result.ValidationErrors!["Email"]));
    }

    [Fact]
    public async Task Concurrent_401s_share_one_refresh_and_replay_once_with_new_bearer()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshes = 0;
        var original = Session("old");
        var session = CreateSession(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("csrf")) return Json(new CsrfResponse("csrf"));
            Interlocked.Increment(ref refreshes); started.TrySetResult(); await release.Task;
            return Json(original with { AccessToken = "new" });
        });
        session.Accept(original);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delayed_logout_success_is_silent_and_failure_keeps_retry_warning(bool serverFailure)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var session = CreateSession(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("csrf")) return Json(new CsrfResponse("csrf"));
            attempts++;
            if (attempts == 1)
            {
                started.TrySetResult();
                await release.Task;
                if (serverFailure) return new(HttpStatusCode.ServiceUnavailable);
            }
            return new(HttpStatusCode.NoContent);
        });
        session.Accept(Session("old"));
        var logout = session.LogoutAsync();
        await started.Task;
        Assert.Null(session.AccessToken);
        Assert.Null(session.Notice);
        release.TrySetResult();
        var result = await logout;
        Assert.Equal(!serverFailure, result.IsSuccess);
        Assert.Null(session.User);
        if (serverFailure)
        {
            Assert.Equal(SessionRevocationState.Unconfirmed, session.Revocation);
            Assert.True((await session.LogoutAsync()).IsSuccess);
            Assert.Equal(2, attempts);
        }
        Assert.Equal(SessionRevocationState.None, session.Revocation);
        Assert.Null(session.Notice);
    }

    [Fact]
    public async Task Failed_logout_cannot_restore_the_unconfirmed_cookie_through_refresh()
    {
        var refreshes = 0;
        var session = CreateSession(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("csrf")) return Task.FromResult(Json(new CsrfResponse("csrf")));
            if (path.EndsWith("logout")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            refreshes++;
            return Task.FromResult(Json(Session("old-cookie-restored")));
        });
        session.Accept(Session("old"));
        Assert.False((await session.LogoutAsync()).IsSuccess);
        Assert.False(await session.RefreshAsync(null));
        Assert.Equal(0, refreshes);
        Assert.Null(session.User);
    }

    [Fact]
    public async Task Unconfirmed_logout_survives_tab_reload_and_blocks_login_until_retry_is_confirmed()
    {
        var browser = new RevocationStorage();
        var original = CreateSession(_ => throw new HttpRequestException("API offline"), browser);
        original.Accept(Session("old"));
        Assert.Equal("network", (await original.LogoutAsync()).Code);
        Assert.Equal("1", browser.Marker);

        var requested = new List<string>();
        var status = HttpStatusCode.BadGateway;
        var reloaded = CreateSession(request =>
        {
            var path = request.RequestUri!.Segments.Last();
            requested.Add(path);
            return Task.FromResult(path switch
            {
                "csrf" => Json(new CsrfResponse("csrf")),
                "logout" => new HttpResponseMessage(status),
                "login" => Json(Session("new")),
                _ => throw new InvalidOperationException("Unconfirmed cookie was used to refresh.")
            });
        }, browser);
        await reloaded.InitializeAsync();
        var loginRequest = new LoginRequest("resident", "password");
        Assert.Equal("revocation_pending", (await reloaded.LoginAsync(loginRequest)).Code);
        Assert.Empty(requested);
        using var resetHttp = new HttpClient(new Transport(_ => throw new InvalidOperationException("Reset would clear the pending cookie.")));
        var logger = NullLogger<ApiClient>.Instance;
        var auth = new AuthenticationService(new(resetHttp, logger), new(resetHttp, logger), reloaded, new ToastService());
        Assert.Equal("revocation_pending", (await auth.ResetAsync(new(Guid.NewGuid(), "proof", "New password! 123"))).Code);

        var rejected = await reloaded.LogoutAsync();
        Assert.Equal(502, rejected.StatusCode);
        Assert.Equal(SessionRevocationState.Unconfirmed, reloaded.Revocation);
        Assert.Equal("1", browser.Marker);
        status = HttpStatusCode.NoContent;
        Assert.True((await reloaded.LogoutAsync()).IsSuccess);
        Assert.Null(browser.Marker);
        Assert.True((await reloaded.LoginAsync(loginRequest)).IsSuccess);
        Assert.Equal("new", reloaded.AccessToken);
    }

    [Fact]
    public async Task Anonymous_logout_without_an_outstanding_session_is_silent()
    {
        var session = CreateSession(_ => throw new InvalidOperationException("Unexpected network request."));
        Assert.True((await session.LogoutAsync()).IsSuccess);
        Assert.Equal(SessionRevocationState.None, session.Revocation);
    }

    private sealed class RevocationStorage : IJSInProcessRuntime
    {
        public string? Marker { get; private set; }
        public TValue Invoke<TValue>(string identifier, params object?[]? args)
        {
            Assert.Equal("propflow:logout-unconfirmed", args![0]);
            switch (identifier)
            {
                case "sessionStorage.getItem": return (TValue)(object?)Marker!;
                case "sessionStorage.setItem": Marker = Assert.IsType<string>(args[1]); break;
                case "sessionStorage.removeItem": Marker = null; break;
                default: throw new InvalidOperationException(identifier);
            }
            return default!;
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => new(Invoke<TValue>(identifier, args));
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => new(Invoke<TValue>(identifier, args));
    }

    [Fact]
    public async Task Late_unauthorized_response_is_not_replayed_under_a_new_login()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = CreateSession(_ => throw new InvalidOperationException("Unexpected refresh."));
        session.Accept(Session("old"));
        var navigation = new Navigation();
        var calls = 0;
        using var http = new HttpClient(new AuthHttpHandler(session, navigation) { InnerHandler = new Transport(async _ =>
        {
            if (++calls == 1)
            {
                started.SetResult();
                await release.Task;
            }
            return new(HttpStatusCode.Unauthorized);
        }) });
        var pending = http.GetAsync("https://test.invalid/api/v1/resource");
        await started.Task;
        session.Accept(Session("new-login"));
        release.SetResult();
        using var response = await pending;
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, calls);
        Assert.Equal("new-login", session.AccessToken);
        Assert.Null(navigation.Destination);
    }

    [Fact]
    public async Task Refresh_cookie_for_a_different_user_cannot_replay_the_previous_users_request()
    {
        var session = CreateSession(request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("csrf")
            ? Json(new CsrfResponse("csrf")) : Json(Session("different-account"))));
        session.Accept(Session("old"));
        var navigation = new Navigation();
        var requests = 0;
        using var http = new HttpClient(new AuthHttpHandler(session, navigation) { InnerHandler = new Transport(_ =>
        {
            requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }) });
        using var response = await http.GetAsync("https://test.invalid/api/v1/resource");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, requests);
        Assert.Null(session.User);
        Assert.Equal("/login", navigation.Destination);
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
