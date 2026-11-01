using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Domain.PaymentDestinations;
using PropFlow.Modules.Payments.Infrastructure.PaymentSettings;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Infrastructure.ResidentPayments;

namespace PropFlow.UnitTests;

public sealed class SePayOAuthTests
{
    [Fact]
    public async Task MissingApplicationCannotStartOrUseLegacyToken()
    {
        using var f = new Fixture(); f.Config.ClientSecret = "";
        Assert.False((await f.Service.StateAsync(default)).Available);
        Assert.Equal("sepay_oauth_not_configured", (await Assert.ThrowsAsync<PaymentSettingsFailure>(() => f.Start())).Code);
        Assert.Empty(f.Handler.Requests);
    }

    [Fact]
    public async Task StartRequiresBoundOtpAndNeverPutsSecretsInAuthorizationUrl()
    {
        using var f = new Fixture();
        var challenge = await f.Service.ChallengeAsync(f.Actor, "CONNECT", default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.StartAsync(f.Actor, new(challenge.ChallengeId, "000000"), default));
        Assert.Empty(await f.Db.SePayConnections.ToArrayAsync());
        var start = await f.Service.StartAsync(f.Actor, new(challenge.ChallengeId, "123456"), default);
        Assert.StartsWith("https://my.sepay.vn/oauth/authorize?", start.AuthorizationUrl);
        Assert.Contains("bank-account%3Aread", start.AuthorizationUrl);
        Assert.DoesNotContain(f.Config.ClientSecret, start.AuthorizationUrl);
        Assert.DoesNotContain("transaction%3Aread", start.AuthorizationUrl);
        Assert.Empty(f.Handler.Requests);
        Assert.DoesNotContain(f.State(start), (await f.Db.SePayConnections.SingleAsync()).PendingStateHash!);
    }

    [Fact]
    public async Task CallbackIsActorBoundEncryptedAndOneUse()
    {
        using var f = new Fixture(); var start = await f.Start();
        var request = new SePayOAuthCallback(f.State(start), "valid-code", null);
        var error = await Assert.ThrowsAsync<PaymentSettingsFailure>(() => f.Service.CompleteAsync(Guid.NewGuid(), request, default));
        Assert.Equal("sepay_invalid_state", error.Code); Assert.Empty(f.Handler.Requests);
        await f.Service.CompleteAsync(f.Actor, request, default);
        var row = await f.Db.SePayConnections.SingleAsync();
        Assert.NotEqual("access-secret", row.ProtectedAccessToken);
        Assert.NotEqual("refresh-secret", row.ProtectedRefreshToken);
        Assert.Equal(f.Actor, row.ConnectedBy);
        Assert.True((await f.Service.StateAsync(default)).Connected);
        var response = JsonSerializer.Serialize(await f.Service.StateAsync(default));
        Assert.DoesNotContain("access-secret", response); Assert.DoesNotContain("refresh-secret", response);
        Assert.Equal("access-secret", (await f.Service.AccessAsync(default)).Token);
        await Assert.ThrowsAsync<PaymentSettingsFailure>(() => f.Service.CompleteAsync(f.Actor, request, default));
        Assert.Single(f.Handler.Requests);
    }

    [Fact]
    public async Task ExpiredOrSupersededStateNeverExchangesCode()
    {
        using var f = new Fixture(); var old = await f.Start(); var current = await f.Start();
        await Assert.ThrowsAsync<PaymentSettingsFailure>(() => f.Service.CompleteAsync(f.Actor, new(f.State(old), "code", null), default));
        var row = await f.Db.SePayConnections.SingleAsync(); row.PendingExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<PaymentSettingsFailure>(() => f.Service.CompleteAsync(f.Actor, new(f.State(current), "code", null), default));
        Assert.Empty(f.Handler.Requests);
    }

    [Fact]
    public async Task DenialAndExchangeFailurePreservePreviousConnectionAndDestination()
    {
        using var f = new Fixture(); await f.Connect();
        var before = (await f.Service.AccessAsync(default)).Generation;
        var denied = await f.Start();
        Assert.Equal("sepay_consent_denied", (await Assert.ThrowsAsync<PaymentSettingsFailure>(() => f.Service.CompleteAsync(f.Actor, new(f.State(denied), null, "access_denied"), default))).Code);
        Assert.Equal(before, (await f.Service.AccessAsync(default)).Generation);
        var failed = await f.Start(); f.Handler.Respond = _ => Fixture.Json(HttpStatusCode.BadGateway, "provider secrets");
        await Assert.ThrowsAsync<PaymentSettingsFailure>(() => f.Service.CompleteAsync(f.Actor, new(f.State(failed), "code", null), default));
        Assert.Equal(before, (await f.Service.AccessAsync(default)).Generation);
    }

    [Fact]
    public async Task ExpiredAccessTokenRotatesRefreshTokenWithoutBrowserSecrets()
    {
        using var f = new Fixture(); await f.Connect();
        var row = await f.Db.SePayConnections.SingleAsync(); row.AccessExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1); await f.Db.SaveChangesAsync();
        f.Handler.Respond = r =>
        {
            Assert.Equal("https://my.sepay.vn/oauth/token", r.Url);
            Assert.Contains("grant_type=refresh_token", r.Body!); Assert.Contains("refresh_token=refresh-secret", r.Body!);
            return Fixture.Json(HttpStatusCode.OK, """{"access_token":"rotated-access","refresh_token":"rotated-refresh","expires_in":3600} """);
        };
        Assert.Equal("rotated-access", (await f.Service.AccessAsync(default)).Token);
        Assert.DoesNotContain("rotated-refresh", row.ProtectedRefreshToken!);
        Assert.False(row.RequiresReconnect);
    }

    [Fact]
    public async Task RevokedRefreshRequiresReconnectAndNeverFallsBackToSharedToken()
    {
        using var f = new Fixture(); await f.Connect();
        var row = await f.Db.SePayConnections.SingleAsync(); row.AccessExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1); await f.Db.SaveChangesAsync();
        f.Handler.Respond = _ => Fixture.Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");
        Assert.Equal("sepay_reconnect_required", (await Assert.ThrowsAsync<PaymentSettingsFailure>(() => f.Service.AccessAsync(default))).Code);
        Assert.True(row.RequiresReconnect); Assert.False((await f.Service.StateAsync(default)).Connected);
        var count = f.Handler.Requests.Count;
        await Assert.ThrowsAsync<PaymentSettingsFailure>(() => f.Service.AccessAsync(default));
        Assert.Equal(count, f.Handler.Requests.Count);
    }

    [Fact]
    public async Task NumericOAuthAccountsAreMaskedAndBoundToConnectionGeneration()
    {
        using var f = new Fixture(); await f.Connect();
        f.Handler.Respond = _ => Fixture.Json(HttpStatusCode.OK, Fixture.Accounts);
        var accounts = new OAuthSePayBankAccounts(f.Service);
        var list = await accounts.ListAsync(default);
        Assert.Single(list); Assert.Equal("970436", list[0].BankBin); Assert.True(list[0].Active);
        Assert.Equal(list[0], await accounts.GetAsync(list[0].Id, default));
        var row = await f.Db.SePayConnections.SingleAsync(); row.Generation = Guid.NewGuid(); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<PaymentSettingsFailure>(() => accounts.GetAsync(list[0].Id, default));
        Assert.NotEqual(list[0].Id, (await accounts.ListAsync(default))[0].Id);
    }

    [Fact]
    public async Task UnauthorizedGetRefreshesOnceThenRetriesAgainstOAuthHost()
    {
        using var f = new Fixture(); await f.Connect();
        var reads = 0;
        f.Handler.Respond = r =>
        {
            if (r.Url.EndsWith("/oauth/token")) return Fixture.Json(HttpStatusCode.OK, """{"access_token":"new-access","refresh_token":"new-refresh","expires_in":3600}""");
            Assert.StartsWith("https://my.sepay.vn/api/v1/", r.Url);
            reads++; return reads == 1 ? Fixture.Json(HttpStatusCode.Unauthorized, "") : Fixture.Json(HttpStatusCode.OK, Fixture.Accounts);
        };
        using var response = await f.Service.GetAsync("bank-accounts", default);
        Assert.Equal(2, reads);
        Assert.Equal("new-access", f.Handler.Requests.Last().Bearer);
    }

    [Fact]
    public async Task WebhookProvisionRequiresOtpUsesSelectedBankAndRecoversExistingWebhook()
    {
        using var f = new Fixture(); await f.Connect();
        var generation = (await f.Service.AccessAsync(default)).Generation;
        var destination = new PaymentDestination(1, "970436", "VCB", "Vietcombank", f.Protector.Protect("123456789"), "6789", "NGUYEN VAN A", f.Actor, DateTimeOffset.UtcNow, SePayOAuth.AccountId(generation, 19));
        f.Db.PaymentDestinations.Add(destination); await f.Db.SaveChangesAsync();
        var challenge = await f.Service.ChallengeAsync(f.Actor, "WEBHOOK", default);
        var before = f.Handler.Requests.Count;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ConfigureWebhookAsync(f.Actor, new(challenge.ChallengeId, "bad"), default));
        Assert.Equal(before, f.Handler.Requests.Count);
        f.Handler.Respond = r =>
        {
            if (r.Url.Contains("bank-accounts")) return Fixture.Json(HttpStatusCode.OK, Fixture.Accounts);
            if (r.Method == HttpMethod.Get) return Fixture.Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { status = "success", data = new[] { new { id = 44, name = "PropFlow " + destination.Id.ToString("N") } } }));
            Assert.Equal(HttpMethod.Patch, r.Method); Assert.EndsWith("webhooks/44", r.Url);
            using var payload = JsonDocument.Parse(r.Body!);
            Assert.Equal(19, payload.RootElement.GetProperty("bank_account_id").GetInt32());
            Assert.Equal("In_only", payload.RootElement.GetProperty("event_type").GetString());
            Assert.Equal("HMAC_SHA256", payload.RootElement.GetProperty("authen_type").GetString());
            Assert.Equal(new string('s', 40), payload.RootElement.GetProperty("secret_key").GetString());
            Assert.Equal("https://api.example.com/api/v1/payments/webhooks/sepay", payload.RootElement.GetProperty("webhook_url").GetString());
            return Fixture.Json(HttpStatusCode.OK, """{"status":"success","data":{"id":44}}""");
        };
        await f.Service.ConfigureWebhookAsync(f.Actor, new(challenge.ChallengeId, "123456"), default);
        Assert.True((await f.Service.StateAsync(default)).WebhookConfigured);
        Assert.Equal(destination.Id, (await f.Db.SePayConnections.SingleAsync()).WebhookDestinationId);
    }

    [Fact]
    public async Task DestinationChangeInvalidatesWebhookOtp()
    {
        using var f = new Fixture(); await f.Connect();
        var challenge = await f.Service.ChallengeAsync(f.Actor, "WEBHOOK", default);
        f.Db.PaymentDestinations.Add(new PaymentDestination(1, "970436", "VCB", "Vietcombank", "protected", "6789", "NGUYEN VAN A", f.Actor, DateTimeOffset.UtcNow, Guid.NewGuid()));
        await f.Db.SaveChangesAsync();
        var before = f.Handler.Requests.Count;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ConfigureWebhookAsync(f.Actor, new(challenge.ChallengeId, "123456"), default));
        Assert.Equal(before, f.Handler.Requests.Count);
    }

    [Theory]
    [InlineData("https://localhost:7201/admin/sepay/callback", true)]
    [InlineData("https://app.example.com/admin/sepay/callback", true)]
    [InlineData("http://app.example.com/admin/sepay/callback", false)]
    [InlineData("https://evil@app.example.com/admin/sepay/callback", false)]
    [InlineData("https://app.example.com/admin/sepay/callback?next=evil", false)]
    [InlineData("https://app.example.com/other", false)]
    public void RedirectIsFixedHttpsCallback(string url, bool valid) => Assert.Equal(valid, SePayOAuth.ValidRedirect(url));

    private sealed class Fixture : IDisposable
    {
        public const string Accounts = """{"status":"success","data":[{"id":19,"account_number":"123456789","account_holder_name":"NGUYEN VAN A","active":true,"bank":{"bin":"970436","code":"VCB","full_name":"Vietcombank"}}],"meta":{"pagination":{"current_page":1,"last_page":1}}}""";
        public readonly PaymentsDbContext Db = new(new DbContextOptionsBuilder<PaymentsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public readonly Handler Handler = new();
        public readonly SePayOAuthOptions Config = new() { Enabled = true, ClientId = "propflow-client", ClientSecret = "client-secret-123", RedirectUri = "https://app.example.com/admin/sepay/callback" };
        public readonly Guid Actor = Guid.NewGuid();
        public readonly SePayOAuth Service;
        public readonly PaymentAccountProtector Protector;
        private readonly HttpClient http;
        public Fixture()
        {
            http = new(Handler); var protection = new EphemeralDataProtectionProvider(); Protector = new(protection);
            Service = new(http, Db, protection, Options.Create(Config), Options.Create(new SePayOptions { Enabled = true,
                AuthenticationMode = "HMAC", WebhookSecret = new string('s', 40), WebhookPublicBaseUrl = "https://api.example.com", ApiToken = "legacy-must-not-be-used" }), new Otp(), TimeProvider.System, Protector);
        }
        public async Task<SePayOAuthStart> Start()
        {
            var challenge = await Service.ChallengeAsync(Actor, "CONNECT", default);
            return await Service.StartAsync(Actor, new(challenge.ChallengeId, "123456"), default);
        }
        public async Task Connect() { var start = await Start(); await Service.CompleteAsync(Actor, new(State(start), "valid-code", null), default); }
        public string State(SePayOAuthStart start) => Uri.UnescapeDataString(start.AuthorizationUrl.Split('&').Single(x => x.StartsWith("state="))[6..]);
        public static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        public void Dispose() { http.Dispose(); Db.Dispose(); }
    }
    private sealed record Request(HttpMethod Method, string Url, string? Body, string? Bearer);
    private sealed class Handler : HttpMessageHandler
    {
        public readonly List<Request> Requests = [];
        public Func<Request, HttpResponseMessage> Respond = _ => Fixture.Json(HttpStatusCode.OK, """{"access_token":"access-secret","refresh_token":"refresh-secret","expires_in":3600}""");
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var captured = new Request(request.Method, request.RequestUri!.AbsoluteUri, request.Content is null ? null : await request.Content.ReadAsStringAsync(ct), request.Headers.Authorization?.Parameter);
            Requests.Add(captured); return Respond(captured);
        }
    }
    private sealed class Otp : IPrivilegedActionOtp
    {
        private readonly Dictionary<Guid, (Guid Actor, string Purpose, string Hash)> codes = [];
        public Task<PrivilegedActionChallengeResult> RequestAsync(Guid userId, string purpose, string payloadHash, CancellationToken ct)
        {
            var id = Guid.NewGuid(); codes[id] = (userId, purpose, payloadHash);
            return Task.FromResult(new PrivilegedActionChallengeResult(id, DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow, "a***@example.com"));
        }
        public Task VerifyAsync(Guid userId, Guid challengeId, string purpose, string payloadHash, string code, CancellationToken ct)
        {
            if (code != "123456" || !codes.TryGetValue(challengeId, out var binding) || binding != (userId, purpose, payloadHash)) throw new InvalidOperationException("OTP binding rejected");
            codes.Remove(challengeId); return Task.CompletedTask;
        }
    }
}
