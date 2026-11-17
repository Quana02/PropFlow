using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Domain.PaymentDestinations;
using PropFlow.Modules.Payments.Domain.PaymentInstructions;
using PropFlow.Modules.Payments.Infrastructure.PaymentSettings;
using PropFlow.Modules.Payments.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed partial class PaymentAuthorizationHttpTests
{
    private const string Accounts = Settings + "sepay/accounts";
    private const string OAuth = Settings + "sepay/oauth/";
    private const string Destination = Settings + "destination";

    private static string Digest<T>(T data) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(data)));

    private async Task<string> SettingsSnapshot()
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var destinations = await db.PaymentDestinations.AsNoTracking().OrderBy(x => x.Version).ToArrayAsync();
        var connection = await db.SePayConnections.AsNoTracking().SingleOrDefaultAsync();
        return Digest(new { destinations, connection });
    }

    private async Task<int> ChallengeCount()
    {
        using var scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>().PrivilegedActionChallenges.CountAsync();
    }

    private async Task<PrivilegedActionChallengeResult> Challenge(Actor admin, string path, object? body = null)
    {
        using var response = await Send(admin.Client, HttpMethod.Post, path, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var challenge = (await response.Content.ReadFromJsonAsync<PrivilegedActionChallengeResult>())!;
        Assert.NotEqual(Guid.Empty, challenge.ChallengeId);
        Assert.True(database.Factory.Mail.Codes.ContainsKey(admin.Email));
        return challenge;
    }

    private async Task<PaymentDestination> SeedDestination(Guid admin, Guid? providerAccount = null)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<IPaymentAccountProtector>();
        var row = new PaymentDestination(1, "970436", "VCB", "Vietcombank", protector.Protect("123456789"),
            "6789", "FIXTURE A", admin, Clock.GetUtcNow(), providerAccount);
        db.PaymentDestinations.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    [Fact]
    public async Task Payment_destination_routes_gate_deactivation_and_preserve_history()
    {
        var admin = await Login("ADMIN");
        var otherAdmin = await Login("ADMIN");
        var unverified = await Login("ADMIN", verified: false);
        var denied = await DeniedActors("ADMIN");
        var active = await SeedDestination(admin.Id);
        var resident = await Login("RESIDENT");
        var identity = await LinkResident(resident);
        var invoice = await SeedInvoice(identity, resident, admin.Id, settled: true);
        using (var historyScope = host.Services.CreateScope())
        {
            var historical = historyScope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            historical.PaymentInstructions.Add(new PaymentInstruction(invoice.Id, active.Id, Clock.GetUtcNow()));
            await historical.SaveChangesAsync();
        }
        var initial = await SettingsSnapshot();
        var ledger = await FinancialSnapshot();
        var draft = new PaymentDestinationDraft("970436", "VCB", "Vietcombank", "123456789", "FIXTURE A", active.Version);
        var deactivate = new PaymentDestinationChallengeRequest("DEACTIVATE", draft);
        await Deny(denied, HttpMethod.Post, Destination + "/change-challenge", deactivate);
        using var unverifiedResult = await Send(unverified.Client, HttpMethod.Post, Destination + "/change-challenge", deactivate);
        await AssertProblem(unverifiedResult, HttpStatusCode.Conflict, "verified_email_required");
        Assert.Equal(0, await ChallengeCount());
        Assert.Empty(database.Factory.Mail.Codes);
        var challenge = await Challenge(admin, Destination + "/change-challenge", deactivate);
        var proof = new DeactivatePaymentDestinationRequest(active.Version, challenge.ChallengeId, Otp(admin));
        var manual = new ConfirmPaymentDestinationRequest(draft, proof.ChallengeId, proof.Code);
        await Deny(denied, HttpMethod.Put, Destination, manual);
        using var retired = await Send(admin.Client, HttpMethod.Put, Destination, manual);
        await AssertProblem(retired, HttpStatusCode.Conflict, "sepay_selection_required");
        await Deny(denied, HttpMethod.Post, Destination + "/deactivate", proof);
        using var foreign = await Send(otherAdmin.Client, HttpMethod.Post, Destination + "/deactivate", proof);
        await AssertProblem(foreign, HttpStatusCode.BadRequest, "invalid_challenge");
        using var wrong = await Send(admin.Client, HttpMethod.Post, Destination + "/deactivate", proof with { Code = WrongCode(proof.Code) });
        await AssertProblem(wrong, HttpStatusCode.BadRequest, "invalid_challenge");
        Clock.Offset = TimeSpan.FromMinutes(6);
        using var expired = await Send(admin.Client, HttpMethod.Post, Destination + "/deactivate", proof);
        await AssertProblem(expired, HttpStatusCode.BadRequest, "invalid_challenge");
        Assert.Equal(initial, await SettingsSnapshot());
        Assert.Equal(1, await ChallengeCount());
        var replacement = await Challenge(admin, Destination + "/change-challenge", deactivate);
        proof = new(active.Version, replacement.ChallengeId, Otp(admin));
        using var accepted = await Send(admin.Client, HttpMethod.Post, Destination + "/deactivate", proof);
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        using var replay = await Send(admin.Client, HttpMethod.Post, Destination + "/deactivate", proof);
        await AssertProblem(replay, HttpStatusCode.Conflict, "payment_destination_missing");
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var retained = await db.PaymentDestinations.AsNoTracking().SingleAsync();
        Assert.Equal(active.Id, retained.Id);
        Assert.Equal(active.Version, retained.Version);
        Assert.False(retained.IsActive);
        Assert.Equal(admin.Id, retained.UpdatedBy);
        var auth = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        Assert.NotNull(await auth.PrivilegedActionChallenges.Where(x => x.Id == replacement.ChallengeId).Select(x => x.UsedAt).SingleAsync());
        Assert.Equal(ledger, await FinancialSnapshot());
        Assert.Equal(invoice.TotalAmount, await Paid(invoice.Id));
        Assert.Empty(provider.Calls);
    }

    private async Task<SePayOAuthCallback> StartOAuth(Actor admin)
    {
        var challenge = await Challenge(admin, OAuth + "challenge/CONNECT");
        using var started = await Send(admin.Client, HttpMethod.Post, OAuth + "start", new SePayOAuthConfirm(challenge.ChallengeId, Otp(admin)));
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        return await CallbackFrom(started);
    }

    private static async Task<SePayOAuthCallback> CallbackFrom(HttpResponseMessage response)
    {
        var start = (await response.Content.ReadFromJsonAsync<SePayOAuthStart>())!;
        var uri = new Uri(start.AuthorizationUrl);
        Assert.Equal("my.sepay.vn", uri.Host);
        var query = QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal("https://app.example.invalid/admin/sepay/callback", query["redirect_uri"].ToString());
        var state = query["state"].ToString();
        Assert.Equal(64, state.Length);
        return new(state, "fixture-code", null);
    }

    private async Task Connect(Actor admin)
    {
        var callback = await StartOAuth(admin);
        using var result = await Send(admin.Client, HttpMethod.Post, OAuth + "callback", callback);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.True((await result.Content.ReadFromJsonAsync<SePayOAuthState>())!.Connected);
    }

    [Fact]
    public async Task SePay_accounts_routes_require_admin_and_bind_save_to_the_selected_account()
    {
        var admin = await Login("ADMIN");
        var otherAdmin = await Login("ADMIN");
        var denied = await DeniedActors("ADMIN");
        var original = await SeedDestination(admin.Id);
        var resident = await Login("RESIDENT");
        var identity = await LinkResident(resident);
        var invoice = await SeedInvoice(identity, resident, admin.Id, settled: true);
        using (var historyScope = host.Services.CreateScope())
        {
            var history = historyScope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            var retired = await history.PaymentDestinations.SingleAsync(x => x.Id == original.Id);
            retired.Deactivate(admin.Id, Clock.GetUtcNow());
            history.PaymentInstructions.Add(new PaymentInstruction(invoice.Id, original.Id, Clock.GetUtcNow()));
            await history.SaveChangesAsync();
        }
        await Connect(admin);
        var before = await SettingsSnapshot();
        var ledger = await FinancialSnapshot();
        var calls = provider.Calls.Count;
        await Deny(denied, HttpMethod.Get, Accounts);
        Assert.Equal(calls, provider.Calls.Count);
        using var listed = await Send(admin.Client, HttpMethod.Get, Accounts);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var listedJson = await listed.Content.ReadAsStringAsync();
        var accounts = JsonSerializer.Deserialize<SePayBankAccountView[]>(listedJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(2, accounts.Length);
        Assert.Equal("******6789", accounts[0].MaskedAccountNumber);
        AssertSafeProviderResponse(listedJson);
        Assert.Equal(calls + 1, provider.Calls.Count);
        Assert.Equal(before, await SettingsSnapshot());
        var select = new SelectSePayAccountRequest(accounts[0].Id);
        calls = provider.Calls.Count;
        var challenges = await ChallengeCount();
        await Deny(denied, HttpMethod.Post, Accounts + "/preview", select);
        await Deny(denied, HttpMethod.Post, Accounts + "/change-challenge", select);
        await Deny(denied, HttpMethod.Post, Settings + "sepay/connection/check");
        Assert.Equal(calls, provider.Calls.Count);
        Assert.Equal(challenges, await ChallengeCount());
        using var preview = await Send(admin.Client, HttpMethod.Post, Accounts + "/preview", select);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var qr = (await preview.Content.ReadFromJsonAsync<PaymentDestinationPreview>())!;
        Assert.StartsWith("data:image/png;base64,", qr.QrImageUrl);
        Assert.Equal("******6789", qr.AccountNumber);
        using var invalidPreview = await Send(admin.Client, HttpMethod.Post, Accounts + "/preview", select with { AccountId = Guid.NewGuid() });
        await AssertProblem(invalidPreview, HttpStatusCode.Conflict, "sepay_account_changed");
        using var connection = await Send(admin.Client, HttpMethod.Post, Settings + "sepay/connection/check");
        Assert.Equal(HttpStatusCode.OK, connection.StatusCode);
        Assert.True((await connection.Content.ReadFromJsonAsync<SePayConnectionState>())!.ApiCheck!.Connected);
        provider.FailAccounts = true;
        using var unavailable = await Send(admin.Client, HttpMethod.Post, Settings + "sepay/connection/check");
        Assert.Equal(HttpStatusCode.OK, unavailable.StatusCode);
        Assert.False((await unavailable.Content.ReadFromJsonAsync<SePayConnectionState>())!.ApiCheck!.Connected);
        provider.FailAccounts = false;
        Assert.Equal(before, await SettingsSnapshot());
        var otp = await Challenge(admin, Accounts + "/change-challenge", select);
        var save = new ConfirmSePayAccountRequest(select.AccountId, 0, otp.ChallengeId, Otp(admin));
        calls = provider.Calls.Count;
        await Deny(denied, HttpMethod.Put, Accounts + "/destination", save);
        Assert.Equal(calls, provider.Calls.Count);
        Assert.Equal(challenges + 1, await ChallengeCount());
        foreach (var (actor, invalid) in new[] { (admin, save with { AccountId = accounts[1].Id }), (otherAdmin, save), (admin, save with { Code = WrongCode(save.Code) }) })
        {
            using var result = await Send(actor.Client, HttpMethod.Put, Accounts + "/destination", invalid);
            await AssertProblem(result, HttpStatusCode.BadRequest, "invalid_challenge");
        }
        using var invalidAccount = await Send(admin.Client, HttpMethod.Put, Accounts + "/destination", save with { AccountId = Guid.NewGuid() });
        await AssertProblem(invalidAccount, HttpStatusCode.Conflict, "sepay_account_changed");
        Clock.Offset = TimeSpan.FromMinutes(6);
        using var expired = await Send(admin.Client, HttpMethod.Put, Accounts + "/destination", save);
        await AssertProblem(expired, HttpStatusCode.BadRequest, "invalid_challenge");
        Assert.Equal(before, await SettingsSnapshot());
        var fresh = await Challenge(admin, Accounts + "/change-challenge", select);
        save = save with { ChallengeId = fresh.ChallengeId, Code = Otp(admin) };
        using var success = await Send(admin.Client, HttpMethod.Put, Accounts + "/destination", save);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        var destination = (await success.Content.ReadFromJsonAsync<PaymentDestinationView>())!;
        Assert.Equal(select.AccountId, destination.SePayAccountId);
        Assert.Equal(admin.Id, destination.UpdatedBy);
        Assert.True(destination.IsActive);
        Assert.Equal(original.Version + 1, destination.Version);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var selected = await db.PaymentDestinations.AsNoTracking().SingleAsync(x => x.Id == destination.Id);
        Assert.True(selected.ProtectedAccountNumber != "123456789");
        Assert.False((await db.PaymentDestinations.AsNoTracking().SingleAsync(x => x.Id == original.Id)).IsActive);
        Assert.Equal(original.Id, (await db.PaymentInstructions.AsNoTracking().SingleAsync(x => x.InvoiceId == invoice.Id)).PaymentDestinationId);
        var saved = await SettingsSnapshot();
        using var replay = await Send(admin.Client, HttpMethod.Put, Accounts + "/destination", save);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.Equal(saved, await SettingsSnapshot());
        Assert.Equal(ledger, await FinancialSnapshot());
    }

    private static void AssertSafeProviderResponse(string json) =>
        Assert.True(!json.Contains(FakeSePay.Access, StringComparison.Ordinal) &&
            !json.Contains(FakeSePay.Refresh, StringComparison.Ordinal) &&
            !json.Contains("123456789", StringComparison.Ordinal), "API exposed provider credentials or unmasked bank data.");

    [Fact]
    public async Task SePay_oauth_routes_gate_state_exchange_and_webhook_configuration()
    {
        var admin = await Login("ADMIN");
        var otherAdmin = await Login("ADMIN");
        var denied = await DeniedActors("ADMIN");
        var ledger = await FinancialSnapshot();
        var empty = await SettingsSnapshot();
        await Deny(denied, HttpMethod.Post, OAuth + "challenge/CONNECT");
        await Deny(denied, HttpMethod.Post, OAuth + "challenge/WEBHOOK");
        using var invalidAction = await Send(admin.Client, HttpMethod.Post, OAuth + "challenge/OTHER");
        await AssertProblem(invalidAction, HttpStatusCode.Conflict, "invalid_action");
        Assert.Equal(0, await ChallengeCount());
        Assert.Empty(database.Factory.Mail.Codes);
        var issued = await Challenge(admin, OAuth + "challenge/CONNECT");
        var confirm = new SePayOAuthConfirm(issued.ChallengeId, Otp(admin));
        var webhookOnly = await Challenge(otherAdmin, OAuth + "challenge/WEBHOOK");
        await Deny(denied, HttpMethod.Post, OAuth + "start", confirm);
        using var ownerMismatch = await Send(otherAdmin.Client, HttpMethod.Post, OAuth + "start", confirm);
        await AssertProblem(ownerMismatch, HttpStatusCode.BadRequest, "invalid_challenge");
        using var wrongPurpose = await Send(otherAdmin.Client, HttpMethod.Post, OAuth + "start",
            new SePayOAuthConfirm(webhookOnly.ChallengeId, Otp(otherAdmin)));
        await AssertProblem(wrongPurpose, HttpStatusCode.BadRequest, "invalid_challenge");
        using var wrong = await Send(admin.Client, HttpMethod.Post, OAuth + "start", confirm with { Code = WrongCode(confirm.Code) });
        await AssertProblem(wrong, HttpStatusCode.BadRequest, "invalid_challenge");
        Clock.Offset = TimeSpan.FromMinutes(6);
        using var expired = await Send(admin.Client, HttpMethod.Post, OAuth + "start", confirm);
        await AssertProblem(expired, HttpStatusCode.BadRequest, "invalid_challenge");
        Assert.Equal(empty, await SettingsSnapshot());
        Assert.Empty(provider.Calls);

        var pending = await StartOAuth(admin);
        using var access = host.Services.CreateScope();
        var db = access.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var auth = access.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var state = await db.SePayConnections.AsNoTracking().SingleAsync();
        Assert.Equal(admin.Id, state.PendingActor);
        Assert.NotNull(state.PendingExpiresAt);
        Assert.True(state.PendingStateHash is not null && state.PendingStateHash != pending.State);
        Assert.Null(state.ProtectedAccessToken);
        var consumed = await auth.PrivilegedActionChallenges.AsNoTracking()
            .Where(x => x.UserId == admin.Id).OrderByDescending(x => x.CreatedAt).FirstAsync();
        using var reusedStart = await Send(admin.Client, HttpMethod.Post, OAuth + "start",
            new SePayOAuthConfirm(consumed.Id, Otp(admin)));
        await AssertProblem(reusedStart, HttpStatusCode.BadRequest, "invalid_challenge");
        var pendingSnapshot = await SettingsSnapshot();
        await Deny(denied, HttpMethod.Post, OAuth + "callback", pending);
        using var foreign = await Send(otherAdmin.Client, HttpMethod.Post, OAuth + "callback", pending);
        await AssertProblem(foreign, HttpStatusCode.Conflict, "sepay_invalid_state");
        var alteredState = (pending.State[0] == 'A' ? 'B' : 'A') + pending.State[1..];
        using var invalidState = await Send(admin.Client, HttpMethod.Post, OAuth + "callback", pending with { State = alteredState });
        await AssertProblem(invalidState, HttpStatusCode.Conflict, "sepay_invalid_state");
        Assert.Equal(pendingSnapshot, await SettingsSnapshot());
        Assert.Empty(provider.Calls);
        Clock.Offset = TimeSpan.FromMinutes(17);
        using var expiredState = await Send(admin.Client, HttpMethod.Post, OAuth + "callback", pending);
        await AssertProblem(expiredState, HttpStatusCode.Conflict, "sepay_invalid_state");
        Assert.Empty(provider.Calls);

        var failing = await StartOAuth(admin);
        provider.FailExchange = true;
        using var badExchange = await Send(admin.Client, HttpMethod.Post, OAuth + "callback", failing);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, badExchange.StatusCode);
        var failedConnection = await db.SePayConnections.AsNoTracking().SingleAsync();
        Assert.True(failedConnection.PendingStateHash is null && failedConnection.ProtectedAccessToken is null && failedConnection.ProtectedRefreshToken is null);
        using var replayFailure = await Send(admin.Client, HttpMethod.Post, OAuth + "callback", failing);
        await AssertProblem(replayFailure, HttpStatusCode.Conflict, "sepay_invalid_state");
        Assert.Single(provider.Calls);
        provider.FailExchange = false;
        Clock.Offset += TimeSpan.FromSeconds(61);
        var ready = await StartOAuth(admin);
        using var connected = await Send(admin.Client, HttpMethod.Post, OAuth + "callback", ready);
        Assert.Equal(HttpStatusCode.OK, connected.StatusCode);
        var connection = await db.SePayConnections.AsNoTracking().SingleAsync();
        Assert.NotEqual(Guid.Empty, connection.Generation);
        Assert.Equal(admin.Id, connection.ConnectedBy);
        Assert.True(connection.ProtectedAccessToken is not null && !connection.ProtectedAccessToken.Contains(FakeSePay.Access, StringComparison.Ordinal));
        Assert.True(connection.ProtectedRefreshToken is not null && !connection.ProtectedRefreshToken.Contains(FakeSePay.Refresh, StringComparison.Ordinal));
        Assert.True(provider.ValidExchange);
        AssertSafeProviderResponse(await connected.Content.ReadAsStringAsync());
        using var replay = await Send(admin.Client, HttpMethod.Post, OAuth + "callback", ready);
        await AssertProblem(replay, HttpStatusCode.Conflict, "sepay_invalid_state");
        Assert.Equal(2, provider.Calls.Count);
        var destination = await SeedDestination(admin.Id, SePayOAuth.AccountId(connection.Generation, 19));

        var provision = await Challenge(otherAdmin, OAuth + "challenge/WEBHOOK");
        var confirmWebhook = new SePayOAuthConfirm(provision.ChallengeId, Otp(otherAdmin));
        var beforeWebhook = await SettingsSnapshot();
        await Deny(denied, HttpMethod.Post, OAuth + "webhook", confirmWebhook);
        using var wrongOwner = await Send(admin.Client, HttpMethod.Post, OAuth + "webhook", confirmWebhook);
        await AssertProblem(wrongOwner, HttpStatusCode.BadRequest, "invalid_challenge");
        using var wrongCode = await Send(otherAdmin.Client, HttpMethod.Post, OAuth + "webhook", confirmWebhook with { Code = WrongCode(confirmWebhook.Code) });
        await AssertProblem(wrongCode, HttpStatusCode.BadRequest, "invalid_challenge");
        Assert.Equal(beforeWebhook, await SettingsSnapshot());
        Assert.Equal(2, provider.Calls.Count);
        provider.FailWebhook = true;
        using var providerRefused = await Send(otherAdmin.Client, HttpMethod.Post, OAuth + "webhook", confirmWebhook);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, providerRefused.StatusCode);
        Assert.Equal(beforeWebhook, await SettingsSnapshot());
        provider.FailWebhook = false;
        Clock.Offset += TimeSpan.FromSeconds(61);
        var newProvision = await Challenge(admin, OAuth + "challenge/WEBHOOK");
        confirmWebhook = new(newProvision.ChallengeId, Otp(admin));
        using var configured = await Send(admin.Client, HttpMethod.Post, OAuth + "webhook", confirmWebhook);
        Assert.Equal(HttpStatusCode.OK, configured.StatusCode);
        Assert.True((await configured.Content.ReadFromJsonAsync<SePayOAuthState>())!.WebhookConfigured);
        var final = await db.SePayConnections.AsNoTracking().SingleAsync();
        Assert.Equal(destination.Id, final.WebhookDestinationId);
        Assert.Equal(44, final.WebhookId);
        Assert.Equal(19, provider.ConfiguredBank);
        Assert.Equal(2, provider.Calls.Count(call => call.Method == HttpMethod.Post && call.Path == "/api/v1/webhooks"));
        var configuredSnapshot = await SettingsSnapshot();
        using var duplicate = await Send(admin.Client, HttpMethod.Post, OAuth + "webhook", confirmWebhook);
        await AssertProblem(duplicate, HttpStatusCode.BadRequest, "invalid_challenge");
        Assert.Equal(configuredSnapshot, await SettingsSnapshot());
        Assert.Equal(ledger, await FinancialSnapshot());
    }
}
