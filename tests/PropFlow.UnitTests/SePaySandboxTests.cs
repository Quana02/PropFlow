using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Infrastructure.PaymentSettings;
using PropFlow.Modules.Payments.Infrastructure.Persistence;

namespace PropFlow.UnitTests;

public sealed class SePaySandboxTests
{
    [Fact]
    public async Task PreviewUsesOnlySandboxHostAndDoesNotPersistToken()
    {
        using var f = new Fixture();
        var result = await f.Service.PreviewAsync(new("CONNECT", "sandbox-private-key", 0), default);
        Assert.True(result.Connected); Assert.Equal(1, result.AccountCount);
        Assert.Empty(await f.Db.SePaySandboxConnections.ToArrayAsync());
        Assert.Empty(await f.Db.SePayConnections.ToArrayAsync());
        Assert.DoesNotContain("sandbox-private-key", JsonSerializer.Serialize(result));
        Assert.All(f.Handler.Calls, x => Assert.Equal("userapi-sandbox.sepay.vn", x.Host));
    }

    [Fact]
    public async Task SaveRequiresOtpBoundToExactTokenAndActorAndEncryptsToken()
    {
        using var f = new Fixture();
        var challenge = await f.Service.ChallengeAsync(f.Actor, new("CONNECT", "token-a", 0), default);
        var calls = f.Handler.Calls.Count;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ConfirmAsync(f.Actor, new("CONNECT", "token-b", 0, challenge.ChallengeId, "123456"), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ConfirmAsync(Guid.NewGuid(), new("CONNECT", "token-a", 0, challenge.ChallengeId, "123456"), default));
        Assert.Equal(calls, f.Handler.Calls.Count);
        var saved = await f.Service.ConfirmAsync(f.Actor, new("CONNECT", "token-a", 0, challenge.ChallengeId, "123456"), default);
        Assert.True(saved.Configured); Assert.True(saved.Verified); Assert.Equal(1, saved.Version);
        var row = await f.Db.SePaySandboxConnections.SingleAsync();
        Assert.NotEqual("token-a", row.ProtectedApiToken); Assert.Equal(f.Actor, row.UpdatedBy);
        Assert.DoesNotContain("token-a", JsonSerializer.Serialize(saved));
        await Assert.ThrowsAsync<PaymentSettingsFailure>(() => f.Service.ConfirmAsync(f.Actor, new("CONNECT", "token-a", 0, challenge.ChallengeId, "123456"), default));
    }

    [Fact]
    public async Task SavingSandboxLeavesLiveConnectionAndDestinationsUntouched()
    {
        using var f = new Fixture();
        var generation = Guid.NewGuid();
        f.Db.SePayConnections.Add(new() { Generation = generation, ProtectedAccessToken = "live-encrypted-access", ProtectedRefreshToken = "live-encrypted-refresh" });
        await f.Db.SaveChangesAsync();
        await f.Connect();
        var live = await f.Db.SePayConnections.SingleAsync();
        Assert.Equal(generation, live.Generation); Assert.Equal("live-encrypted-access", live.ProtectedAccessToken);
        Assert.Empty(await f.Db.PaymentDestinations.ToArrayAsync());
        Assert.Empty(await f.Db.PaymentInstructions.ToArrayAsync());
        Assert.Empty(await f.Db.Payments.ToArrayAsync());
    }

    [Fact]
    public async Task StoredTokenCanReadMaskedAccountsButNeverReturnsBalancesOrSecrets()
    {
        using var f = new Fixture(); await f.Connect();
        var accounts = await f.Service.AccountsAsync(default);
        var account = Assert.Single(accounts);
        Assert.Equal("******6789", account.MaskedAccountNumber); Assert.True(account.Active);
        var json = JsonSerializer.Serialize(accounts);
        Assert.DoesNotContain("123456789", json); Assert.DoesNotContain("99999999", json); Assert.DoesNotContain("sandbox-key", json);
        Assert.True((await f.Service.CheckAsync(default)).Connected);
    }

    [Fact]
    public async Task ReplacingTokenInvalidatesStaleOtpAndDisconnectAlsoRequiresOtp()
    {
        using var f = new Fixture(); await f.Connect();
        var stale = await f.Service.ChallengeAsync(f.Actor, new("DISCONNECT", null, 1), default);
        var replacement = await f.Service.ChallengeAsync(f.Actor, new("CONNECT", "replacement-key", 1), default);
        await f.Service.ConfirmAsync(f.Actor, new("CONNECT", "replacement-key", 1, replacement.ChallengeId, "123456"), default);
        Assert.Equal("sepay_sandbox_changed", (await Assert.ThrowsAsync<PaymentSettingsFailure>(() => f.Service.ConfirmAsync(f.Actor, new("DISCONNECT", null, 1, stale.ChallengeId, "123456"), default))).Code);
        var disconnect = await f.Service.ChallengeAsync(f.Actor, new("DISCONNECT", null, 2), default);
        var calls = f.Handler.Calls.Count;
        var saved = await f.Service.ConfirmAsync(f.Actor, new("DISCONNECT", null, 2, disconnect.ChallengeId, "123456"), default);
        Assert.False(saved.Configured); Assert.False(saved.Verified); Assert.Equal(3, saved.Version);
        Assert.Null((await f.Db.SePaySandboxConnections.SingleAsync()).ProtectedApiToken);
        Assert.Equal(calls, f.Handler.Calls.Count);
    }

    [Fact]
    public async Task DemoModeRequiresOtpAndDisconnectNeverFallsBackToRealPayments()
    {
        using var f = new Fixture(); await f.Connect();
        var challenge = await f.Service.ChallengeAsync(f.Actor, new("DEMO_ON", null, 1), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ConfirmAsync(f.Actor, new("DEMO_ON", null, 1, challenge.ChallengeId, "bad"), default));
        Assert.False((await f.Service.StateAsync(default)).DemoEnabled);
        var enabled = await f.Service.ConfirmAsync(f.Actor, new("DEMO_ON", null, 1, challenge.ChallengeId, "123456"), default);
        Assert.True(enabled.DemoEnabled); Assert.True(enabled.Verified);
        var disconnect = await f.Service.ChallengeAsync(f.Actor, new("DISCONNECT", null, 2), default);
        var disconnected = await f.Service.ConfirmAsync(f.Actor, new("DISCONNECT", null, 2, disconnect.ChallengeId, "123456"), default);
        Assert.True(disconnected.DemoEnabled); Assert.False(disconnected.Configured);
        var stop = await f.Service.ChallengeAsync(f.Actor, new("DEMO_OFF", null, 3), default);
        Assert.False((await f.Service.ConfirmAsync(f.Actor, new("DEMO_OFF", null, 3, stop.ChallengeId, "123456"), default)).DemoEnabled);
    }

    [Theory]
    [InlineData(401, "sepay_sandbox_unauthorized")]
    [InlineData(403, "sepay_sandbox_forbidden")]
    [InlineData(302, "sepay_sandbox_unavailable")]
    [InlineData(500, "sepay_sandbox_unavailable")]
    public async Task ProviderErrorsAreSanitizedAndDoNotPersistCredentials(int status, string code)
    {
        using var f = new Fixture(); f.Handler.Respond = _ => Json((HttpStatusCode)status, "provider-private-response");
        var result = await f.Service.PreviewAsync(new("CONNECT", "secret", 0), default);
        Assert.False(result.Connected); Assert.Equal(code, result.Code); Assert.DoesNotContain("provider-private-response", result.Message);
        Assert.Empty(await f.Db.SePaySandboxConnections.ToArrayAsync());
    }

    [Fact]
    public async Task RevocationIsReflectedInStatusWithoutChangingSavedToken()
    {
        using var f = new Fixture(); await f.Connect();
        var encrypted = (await f.Db.SePaySandboxConnections.SingleAsync()).ProtectedApiToken;
        f.Handler.Respond = _ => Json(HttpStatusCode.Unauthorized, "revoked");
        await Assert.ThrowsAsync<PaymentSettingsFailure>(() => f.Service.CheckAsync(default));
        var state = await f.Service.StateAsync(default);
        Assert.True(state.Configured); Assert.False(state.Verified);
        Assert.Equal(encrypted, (await f.Db.SePaySandboxConnections.SingleAsync()).ProtectedApiToken);
    }

    [Fact]
    public async Task FailureAfterOtpVerificationPreservesPreviousToken()
    {
        using var f = new Fixture(); await f.Connect();
        var row = await f.Db.SePaySandboxConnections.SingleAsync(); var encrypted = row.ProtectedApiToken;
        var challenge = await f.Service.ChallengeAsync(f.Actor, new("CONNECT", "new-token", 1), default);
        f.Handler.Respond = _ => Json(HttpStatusCode.ServiceUnavailable, "");
        await Assert.ThrowsAsync<PaymentSettingsFailure>(() => f.Service.ConfirmAsync(f.Actor, new("CONNECT", "new-token", 1, challenge.ChallengeId, "123456"), default));
        Assert.Equal(encrypted, row.ProtectedApiToken); Assert.Equal(1, row.Version);
    }

    [Fact]
    public async Task PaginationReadsAllPagesAndStopsAtLastPage()
    {
        using var f = new Fixture();
        f.Handler.Respond = uri => Json(HttpStatusCode.OK, uri.Query.Contains("page=1&")
            ? Accounts.Replace("\"last_page\":1", "\"last_page\":2")
            : Accounts.Replace("sandbox-account-1", "sandbox-account-2").Replace("\"last_page\":1", "\"last_page\":2"));
        Assert.Equal(2, (await f.Service.PreviewAsync(new("CONNECT", "key", 0), default)).AccountCount);
        Assert.Equal(2, f.Handler.Calls.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("key\nheader")]
    [InlineData("key with spaces")]
    public async Task InvalidTokenNeverContactsProvider(string token)
    {
        using var f = new Fixture();
        await Assert.ThrowsAsync<PaymentSettingsFailure>(() => f.Service.PreviewAsync(new("CONNECT", token, 0), default));
        Assert.Empty(f.Handler.Calls);
    }

    private const string Accounts = """{"status":"success","data":[{"id":"sandbox-account-1","bank_full_name":"Vietcombank","bank_code":"VCB","account_holder_name":"TEST OWNER","account_number":"123456789","active":true,"accumulated":99999999}],"meta":{"pagination":{"last_page":1}}}""";
    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    [Fact]
    public async Task WebhookKeyRequiresOtpAndIsOnlyDisclosedInConfirmedAdminResponse()
    {
        using var f = new Fixture(); await f.Connect();
        var challenge = await f.Service.ChallengeAsync(f.Actor, new("WEBHOOK_KEY", null, 1), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ConfirmAsync(f.Actor, new("WEBHOOK_KEY", null, 1, challenge.ChallengeId, "bad"), default));
        Assert.False((await f.Service.StateAsync(default)).WebhookKeyConfigured);
        var response = await f.Service.ConfirmAsync(f.Actor, new("WEBHOOK_KEY", null, 1, challenge.ChallengeId, "123456"), default);
        Assert.Equal(64, response.WebhookApiKey!.Length);
        Assert.NotEqual(response.WebhookApiKey, (await f.Db.SePaySandboxConnections.SingleAsync()).ProtectedWebhookKey);
        Assert.Null((await f.Service.StateAsync(default)).WebhookApiKey);
        Assert.True((await f.Service.StateAsync(default)).WebhookKeyConfigured);
    }

    private sealed class Fixture : IDisposable
    {
        public readonly PaymentsDbContext Db = new(new DbContextOptionsBuilder<PaymentsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public readonly Handler Handler = new();
        public readonly Guid Actor = Guid.NewGuid();
        public readonly SePaySandbox Service;
        private readonly HttpClient http;
        public Fixture() { http = new(Handler); Service = new(http, Db, new EphemeralDataProtectionProvider(), new Otp(), TimeProvider.System); }
        public async Task Connect()
        {
            var challenge = await Service.ChallengeAsync(Actor, new("CONNECT", "sandbox-key", 0), default);
            await Service.ConfirmAsync(Actor, new("CONNECT", "sandbox-key", 0, challenge.ChallengeId, "123456"), default);
        }
        public void Dispose() { http.Dispose(); Db.Dispose(); }
    }
    private sealed class Handler : HttpMessageHandler
    {
        public readonly List<Uri> Calls = [];
        public Func<Uri, HttpResponseMessage> Respond = _ => Json(HttpStatusCode.OK, Accounts);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Assert.Equal("Bearer", request.Headers.Authorization!.Scheme); Calls.Add(request.RequestUri!); return Task.FromResult(Respond(request.RequestUri!)); }
    }
    private sealed class Otp : IPrivilegedActionOtp
    {
        private readonly Dictionary<Guid, (Guid Actor, string Purpose, string Hash)> codes = [];
        public Task<PrivilegedActionChallengeResult> RequestAsync(Guid userId, string purpose, string hash, CancellationToken ct)
        { var id = Guid.NewGuid(); codes[id] = (userId, purpose, hash); return Task.FromResult(new PrivilegedActionChallengeResult(id, DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow, "a***@example.com")); }
        public Task VerifyAsync(Guid userId, Guid challengeId, string purpose, string hash, string code, CancellationToken ct)
        {
            if (code != "123456" || !codes.TryGetValue(challengeId, out var binding) || binding != (userId, purpose, hash)) throw new InvalidOperationException("OTP rejected");
            codes.Remove(challengeId); return Task.CompletedTask;
        }
    }
}
