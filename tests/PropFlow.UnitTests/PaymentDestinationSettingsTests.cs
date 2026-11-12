using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Domain.PaymentDestinations;

namespace PropFlow.UnitTests;

public sealed class PaymentDestinationSettingsTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);
    private static readonly BankOption Bank = new("970436", "VCB", "Vietcombank", "Ngân hàng TMCP Ngoại thương Việt Nam", null);

    [Fact]
    public async Task SaveAsync_NormalizesAndMasksAccount_AndBindsOtpToExactDraft()
    {
        var store = new MemoryStore();
        var otp = new BindingOtp();
        var settings = Create(store, otp);
        var draft = new PaymentDestinationDraft("970436", "ignored", "ignored", " 0123 456 789 ", "Nguyễn Văn An", 0);

        var challenge = await settings.RequestChallengeAsync(Actor, new("upsert", draft), CancellationToken.None);
        var saved = await settings.SaveAsync(
            Actor,
            new(draft, challenge.ChallengeId, "123456"),
            CancellationToken.None);

        Assert.Equal(1, saved.Version);
        Assert.Equal("******6789", saved.MaskedAccountNumber);
        Assert.Equal("NGUYEN VAN AN", saved.AccountName);
        Assert.Equal("VCB", saved.BankCode);
        Assert.Equal("protected:0123456789", store.Items.Single().ProtectedAccountNumber);
        Assert.Equal(otp.RequestedPayloadHash, otp.VerifiedPayloadHash);
    }

    [Fact]
    public async Task SaveAsync_RejectsOtpRequestedForDifferentAccount()
    {
        var otp = new BindingOtp();
        var settings = Create(new MemoryStore(), otp);
        var original = new PaymentDestinationDraft(Bank.Bin, Bank.Code, Bank.Name, "0123456789", "NGUYEN VAN AN", 0);
        var changed = original with { AccountNumber = "9999999999" };
        var challenge = await settings.RequestChallengeAsync(Actor, new("UPSERT", original), CancellationToken.None);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            settings.SaveAsync(Actor, new(changed, challenge.ChallengeId, "123456"), CancellationToken.None));

        Assert.Equal("OTP payload mismatch.", failure.Message);
    }

    [Fact]
    public async Task RecreateAfterDeactivate_ContinuesVersionHistory()
    {
        var store = new MemoryStore();
        var otp = new BindingOtp();
        var settings = Create(store, otp);
        var first = new PaymentDestinationDraft(Bank.Bin, Bank.Code, Bank.Name, "0123456789", "NGUYEN VAN AN", 0);
        var firstChallenge = await settings.RequestChallengeAsync(Actor, new("UPSERT", first), CancellationToken.None);
        var saved = await settings.SaveAsync(Actor, new(first, firstChallenge.ChallengeId, "123456"), CancellationToken.None);
        var deactivateChallenge = await settings.RequestChallengeAsync(
            Actor,
            new("DEACTIVATE", first with { ExpectedVersion = saved.Version }),
            CancellationToken.None);
        await settings.DeactivateAsync(Actor, new(saved.Version, deactivateChallenge.ChallengeId, "123456"), CancellationToken.None);

        var second = first with { AccountNumber = "1234567890", ExpectedVersion = 0 };
        var secondChallenge = await settings.RequestChallengeAsync(Actor, new("UPSERT", second), CancellationToken.None);
        var recreated = await settings.SaveAsync(Actor, new(second, secondChallenge.ChallengeId, "123456"), CancellationToken.None);

        Assert.Equal(2, recreated.Version);
        Assert.Equal(2, store.Items.Count);
        Assert.Single(store.Items, item => item.IsActive);
    }

    [Fact]
    public async Task PreviewAsync_UsesNormalizedVietQrInformation()
    {
        var settings = Create(new MemoryStore(), new BindingOtp());

        var preview = await settings.PreviewAsync(
            new(Bank.Bin, "wrong", "wrong", "0123-456-789", "Nguyễn Văn An"),
            CancellationToken.None);

        Assert.Contains("970436-0123456789-compact2.png", preview.QrImageUrl, StringComparison.Ordinal);
        Assert.Contains("accountName=NGUYEN%20VAN%20AN", preview.QrImageUrl, StringComparison.Ordinal);
        Assert.Equal(Bank.Name, preview.BankName);
    }

    private static PaymentDestinationSettings Create(MemoryStore store, BindingOtp otp) =>
        new(store, new FakeProtector(), new FakeBanks(), otp, new FixedTimeProvider(Now));

    private sealed class FakeProtector : IPaymentAccountProtector
    {
        public string Protect(string accountNumber) => "protected:" + accountNumber;
    }

    private sealed class FakeBanks : IPaymentBankDirectory
    {
        public Task<IReadOnlyList<BankOption>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BankOption>>([Bank]);
    }

    private sealed class BindingOtp : IPrivilegedActionOtp
    {
        private readonly Guid challengeId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        public string? RequestedPayloadHash { get; private set; }
        public string? VerifiedPayloadHash { get; private set; }

        public Task<PrivilegedActionChallengeResult> RequestAsync(Guid userId, string purpose, string payloadHash, CancellationToken ct)
        {
            RequestedPayloadHash = payloadHash;
            return Task.FromResult(new PrivilegedActionChallengeResult(challengeId, Now.AddMinutes(5), Now.AddMinutes(1), "a***n@example.test"));
        }

        public Task VerifyAsync(Guid userId, Guid challenge, string purpose, string payloadHash, string code, CancellationToken ct)
        {
            VerifiedPayloadHash = payloadHash;
            if (challenge != challengeId || code != "123456" || payloadHash != RequestedPayloadHash)
                throw new InvalidOperationException("OTP payload mismatch.");
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryStore : IPaymentDestinationStore
    {
        public List<PaymentDestination> Items { get; } = [];

        public Task<PaymentDestination?> ActiveAsync(CancellationToken ct) =>
            Task.FromResult(Items.SingleOrDefault(item => item.IsActive));

        public Task<IReadOnlyList<PaymentDestination>> HistoryAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PaymentDestination>>(Items.OrderByDescending(item => item.Version).ToArray());

        public Task<PaymentDestination> AddReplacementAsync(
            PaymentDestinationWrite write,
            int expectedVersion,
            Guid actorUserId,
            DateTimeOffset now,
            CancellationToken ct)
        {
            var active = Items.SingleOrDefault(item => item.IsActive);
            if ((active?.Version ?? 0) != expectedVersion) throw new PaymentSettingsFailure(409, "conflict", "conflict");
            active?.Deactivate(actorUserId, now);
            var item = new PaymentDestination(
                Items.Select(existing => existing.Version).DefaultIfEmpty().Max() + 1,
                write.BankBin,
                write.BankCode,
                write.BankName,
                write.ProtectedAccountNumber,
                write.AccountNumberLast4,
                write.AccountName,
                actorUserId,
                now);
            Items.Add(item);
            return Task.FromResult(item);
        }

        public Task DeactivateAsync(int expectedVersion, Guid actorUserId, DateTimeOffset now, CancellationToken ct)
        {
            var active = Items.SingleOrDefault(item => item.IsActive);
            if (active?.Version != expectedVersion) throw new PaymentSettingsFailure(409, "conflict", "conflict");
            active.Deactivate(actorUserId, now);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
