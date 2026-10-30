using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Domain.PaymentDestinations;
using PropFlow.Modules.Payments.Infrastructure.PaymentSettings;
using PropFlow.Modules.Payments.Infrastructure.ResidentPayments;
using PropFlow.Modules.Payments.Infrastructure.Persistence;

namespace PropFlow.UnitTests;

public sealed class SePayAccountSelectionTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly BankOption Bank = new("970436", "VCB", "Vietcombank", "Vietcombank", null);

    [Fact]
    public async Task SyncReadsAllPagesAndOnlyExposesMaskedAccounts()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("userapi.sepay.vn", request.RequestUri!.Host);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            var page = request.RequestUri.Query.Contains("page=2&") ? 2 : 1;
            var body = System.Text.Json.JsonSerializer.Serialize(new { status = "success", data = new[] {
                new { id = ids[page - 1], bank_short_name = "Vietcombank", account_number = "0123456789",
                    account_holder_name = "NGUYEN VAN A", active = page == 1 ? System.Text.Json.JsonSerializer.SerializeToElement(true) : System.Text.Json.JsonSerializer.SerializeToElement(1), accumulated = 123456789 }
            }, meta = new { pagination = new { last_page = 2, has_more = page == 1 } } });
            return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }));
        var client = new SePayBankAccounts(http, Options.Create(new SePayOptions { ApiToken = "token" }), new Banks());
        var selection = new SePayAccountSelection(client, null!);
        var accounts = await selection.ListAsync(default);
        Assert.Equal(2, accounts.Count);
        Assert.All(accounts, x => { Assert.True(x.Active); Assert.Equal("******6789", x.MaskedAccountNumber); });
        var json = System.Text.Json.JsonSerializer.Serialize(accounts);
        Assert.DoesNotContain("0123456789", json);
        Assert.DoesNotContain("123456789", json);
    }

    [Fact]
    public async Task SaveRechecksProviderAndRejectsChangedAccountAfterOtp()
    {
        var accounts = new Accounts(); var store = new Store(); var otp = new Otp();
        var settings = new PaymentDestinationSettings(store, new Protector(), new Banks(), otp, TimeProvider.System);
        var selection = new SePayAccountSelection(accounts, settings);
        var challenge = await selection.ChallengeAsync(Actor, new(accounts.Account.Id), default);
        accounts.Account = accounts.Account with { AccountNumber = "9999999999" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => selection.SaveAsync(Actor,
            new(accounts.Account.Id, 0, challenge.ChallengeId, "123456"), default));
        Assert.Null(store.Saved);
    }

    [Fact]
    public async Task InactiveAccountsCannotRequestOtpAndPreviewUsesLocalImage()
    {
        var accounts = new Accounts(); var otp = new Otp();
        var settings = new PaymentDestinationSettings(new Store(), new Protector(), new Banks(), otp, TimeProvider.System);
        var selection = new SePayAccountSelection(accounts, settings);
        var preview = await selection.PreviewAsync(new(accounts.Account.Id), default);
        Assert.StartsWith("data:image/png;base64,", preview.QrImageUrl);
        Assert.Equal("******6789", preview.AccountNumber);
        accounts.Account = accounts.Account with { Active = false };
        var error = await Assert.ThrowsAsync<PaymentSettingsFailure>(() => selection.ChallengeAsync(Actor, new(accounts.Account.Id), default));
        Assert.Equal("sepay_account_inactive", error.Code);
        Assert.Null(otp.Hash);
    }

    [Fact]
    public async Task SelectedAccountIsSavedOnlyAfterMatchingOtp()
    {
        var accounts = new Accounts(); var store = new Store();
        var settings = new PaymentDestinationSettings(store, new Protector(), new Banks(), new Otp(), TimeProvider.System);
        var selection = new SePayAccountSelection(accounts, settings);
        var challenge = await selection.ChallengeAsync(Actor, new(accounts.Account.Id), default);
        var saved = await selection.SaveAsync(Actor, new(accounts.Account.Id, 0, challenge.ChallengeId, "123456"), default);
        Assert.Equal("******6789", saved.MaskedAccountNumber);
        Assert.Equal("protected:0123456789", store.Saved!.ProtectedAccountNumber);
        Assert.Equal(accounts.Account.Id, saved.SePayAccountId);
        Assert.Equal(accounts.Account.Id, store.Saved.SePayAccountId);
        Assert.Equal(Actor, saved.CreatedBy);
        Assert.NotNull(saved.CreatedAt);
        Assert.Equal(2, accounts.Reads);
    }

    [Fact]
    public async Task OtpCannotBeReusedForAnotherSePayIdWithIdenticalBankDetails()
    {
        var accounts = new Accounts(); var store = new Store();
        var settings = new PaymentDestinationSettings(store, new Protector(), new Banks(), new Otp(), TimeProvider.System);
        var selection = new SePayAccountSelection(accounts, settings);
        var challenge = await selection.ChallengeAsync(Actor, new(accounts.Account.Id), default);
        accounts.Account = accounts.Account with { Id = Guid.NewGuid() };
        await Assert.ThrowsAsync<InvalidOperationException>(() => selection.SaveAsync(Actor,
            new(accounts.Account.Id, 0, challenge.ChallengeId, "123456"), default));
        Assert.Null(store.Saved);
    }

    [Fact]
    public async Task DisabledAccountAfterChallengeCannotBeSaved()
    {
        var accounts = new Accounts(); var store = new Store();
        var selection = new SePayAccountSelection(accounts, new PaymentDestinationSettings(store, new Protector(), new Banks(), new Otp(), TimeProvider.System));
        var challenge = await selection.ChallengeAsync(Actor, new(accounts.Account.Id), default);
        accounts.Account = accounts.Account with { Active = false };
        var failure = await Assert.ThrowsAsync<PaymentSettingsFailure>(() => selection.SaveAsync(Actor,
            new(accounts.Account.Id, 0, challenge.ChallengeId, "123456"), default));
        Assert.Equal("sepay_account_inactive", failure.Code);
        Assert.Null(store.Saved);
    }

    [Fact]
    public async Task StaleVersionIsRejectedBeforeOtpIsConsumed()
    {
        var accounts = new Accounts(); var store = new Store(); var otp = new Otp();
        var selection = new SePayAccountSelection(accounts, new PaymentDestinationSettings(store, new Protector(), new Banks(), otp, TimeProvider.System));
        var challenge = await selection.ChallengeAsync(Actor, new(accounts.Account.Id), default);
        store.Saved = new(1, Bank.Bin, Bank.Code, Bank.Name, "protected:other", "9999", "OTHER ACCOUNT", Actor, DateTimeOffset.UtcNow);
        var failure = await Assert.ThrowsAsync<PaymentSettingsFailure>(() => selection.SaveAsync(Actor,
            new(accounts.Account.Id, 0, challenge.ChallengeId, "123456"), default));
        Assert.Equal("payment_destination_changed", failure.Code);
        Assert.Equal(0, otp.Verifications);
    }

    [Fact]
    public async Task LegacyManualEndpointCannotAcceptForgedSePaySource()
    {
        var controller = new PropFlow.Modules.Payments.Presentation.PaymentSettingsController(null!);
        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };
        var result = await controller.Save(new(new(Bank.Bin, Bank.Code, Bank.Name, "0123456789", "NGUYEN VAN A", SePayAccountId: Guid.NewGuid()), Guid.NewGuid(), "123456"), default);
        Assert.Equal(409, Assert.IsType<Microsoft.AspNetCore.Mvc.ObjectResult>(result.Result).StatusCode);
        var challenge = await controller.Challenge(new("UPSERT", new(Bank.Bin, Bank.Code, Bank.Name, "0123456789", "NGUYEN VAN A")), default);
        Assert.Equal(409, Assert.IsType<Microsoft.AspNetCore.Mvc.ObjectResult>(challenge.Result).StatusCode);
    }

    [Fact]
    public async Task HistoryRetainsSePaySourceCreatorAndOriginalConfirmationTime()
    {
        await using var db = new PaymentsDbContext(new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var sourceId = Guid.NewGuid();
        var confirmedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var destination = new PaymentDestination(1, Bank.Bin, Bank.Code, Bank.Name, "protected", "6789", "NGUYEN VAN A", Actor, confirmedAt, sourceId);
        destination.Deactivate(Guid.NewGuid(), confirmedAt.AddHours(1));
        db.PaymentDestinations.Add(destination);
        db.PaymentDestinations.Add(new(2, Bank.Bin, Bank.Code, Bank.Name, "legacy", "9999", "LEGACY ACCOUNT", Actor, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var history = await new PaymentDestinationStore(db).HistoryAsync(default);
        Assert.Null(history[0].SePayAccountId);
        var closed = history.Single(x => x.Version == 1);
        Assert.Equal(sourceId, closed.SePayAccountId);
        Assert.Equal(Actor, closed.CreatedBy);
        Assert.Equal(confirmedAt, closed.CreatedAt);
        Assert.False(closed.IsActive);
        Assert.NotNull(closed.DeactivatedAt);
    }

    private sealed class Accounts : ISePayBankAccounts
    {
        public SePayBankAccount Account = new(Guid.NewGuid(), Bank.Bin, Bank.Code, Bank.Name, "0123456789", "NGUYEN VAN A", true);
        public int Reads;
        public Task<IReadOnlyList<SePayBankAccount>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SePayBankAccount>>([Account]);
        public Task<SePayBankAccount> GetAsync(Guid id, CancellationToken ct) { Reads++; Assert.Equal(Account.Id, id); return Task.FromResult(Account); }
    }
    private sealed class Banks : IPaymentBankDirectory
    { public Task<IReadOnlyList<BankOption>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<BankOption>>([Bank]); }
    private sealed class Protector : IPaymentAccountProtector
    { public string Protect(string value) => "protected:" + value; }
    private sealed class Otp : IPrivilegedActionOtp
    {
        public string? Hash;
        public int Verifications;
        private readonly Guid id = Guid.NewGuid();
        public Task<PrivilegedActionChallengeResult> RequestAsync(Guid user, string purpose, string hash, CancellationToken ct)
        { Hash = hash; return Task.FromResult(new PrivilegedActionChallengeResult(id, DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow, "a***@test.vn")); }
        public Task VerifyAsync(Guid user, Guid challenge, string purpose, string hash, string code, CancellationToken ct)
        { Verifications++; if (id != challenge || hash != Hash || code != "123456") throw new InvalidOperationException("OTP mismatch"); return Task.CompletedTask; }
    }
    private sealed class Store : IPaymentDestinationStore
    {
        public PaymentDestination? Saved;
        public Task<PaymentDestination?> ActiveAsync(CancellationToken ct) => Task.FromResult(Saved);
        public Task<IReadOnlyList<PaymentDestination>> HistoryAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<PaymentDestination>>([]);
        public Task<PaymentDestination> AddReplacementAsync(PaymentDestinationWrite w, int version, Guid actor, DateTimeOffset now, CancellationToken ct)
        { Saved = new(1, w.BankBin, w.BankCode, w.BankName, w.ProtectedAccountNumber, w.AccountNumberLast4, w.AccountName, actor, now, w.SePayAccountId); return Task.FromResult(Saved); }
        public Task DeactivateAsync(int version, Guid actor, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request)); }
}
