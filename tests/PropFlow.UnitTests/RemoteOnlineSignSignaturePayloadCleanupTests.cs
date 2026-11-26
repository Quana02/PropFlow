using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharpCore.Pdf;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.RemoteOnlineSign.Application;
using PropFlow.Modules.RemoteOnlineSign.Domain;
using PropFlow.Modules.RemoteOnlineSign.Infrastructure.Persistence;
using PropFlow.Modules.RemoteOnlineSign.Infrastructure.Storage;

namespace PropFlow.UnitTests;

// Coverage for the periodic signature payload cleanup (SIGNATURE-SECURITY-02):
// payloads of sessions that can no longer finalize or retry must be released,
// while sessions still able to sign, verify OTP, or retry finalization keep theirs.
public sealed class RemoteOnlineSignSignaturePayloadCleanupTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public async Task Expired_non_retryable_session_payload_is_cleaned_and_metadata_kept()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddPendingContractAsync();
        await fixture.BeginAsync(contract.Id);
        var session = await fixture.SessionAsync(contract.Id);
        var sha = session.SignatureSha256;
        var binding = session.BindingHash;
        session.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await fixture.Db.SaveChangesAsync();

        var cleaned = await fixture.Service.CleanupExpiredSignaturePayloadsAsync(100, default);

        Assert.Equal(1, cleaned);
        session = await fixture.SessionAsync(contract.Id);
        Assert.Null(session.SignatureImage);
        Assert.Equal(sha, session.SignatureSha256);
        Assert.Equal(binding, session.BindingHash);
        Assert.Equal(SignatureMethod.DRAW, session.Method);
        Assert.Equal(fixture.Resident, session.SignerId);
        Assert.NotNull(session.AuthChallengeId);
    }

    [Fact]
    public async Task Finalized_legacy_payload_is_cleaned()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddPendingContractAsync();
        var challenge = await fixture.BeginAsync(contract.Id);
        await fixture.Service.VerifyAndFinalizeAsync(fixture.Resident, false, contract.Id,
            new VerifySignature(challenge.SessionId, "123456"), default);

        var session = await fixture.SessionAsync(contract.Id);
        session.SignatureImage = Png; // simulate pre-SIGNATURE-SECURITY-01 legacy payload
        await fixture.Db.SaveChangesAsync();

        var cleaned = await fixture.Service.CleanupExpiredSignaturePayloadsAsync(100, default);

        Assert.Equal(1, cleaned);
        session = await fixture.SessionAsync(contract.Id);
        Assert.Null(session.SignatureImage);
        Assert.NotNull(session.FinalizedAt);
    }

    [Fact]
    public async Task Valid_pending_otp_session_is_preserved()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddPendingContractAsync();
        await fixture.BeginAsync(contract.Id);

        var cleaned = await fixture.Service.CleanupExpiredSignaturePayloadsAsync(100, default);

        Assert.Equal(0, cleaned);
        Assert.Equal(Png, (await fixture.SessionAsync(contract.Id)).SignatureImage);
    }

    [Fact]
    public async Task Verified_retryable_session_is_preserved_until_finalize()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddPendingContractAsync();
        var challenge = await fixture.BeginAsync(contract.Id);
        fixture.Storage.FailNextDerivedPuts = 1;
        await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.VerifyAndFinalizeAsync(fixture.Resident, false, contract.Id,
                new VerifySignature(challenge.SessionId, "123456"), default));

        // OTP verified, finalization failed temporarily: session is retryable.
        var cleaned = await fixture.Service.CleanupExpiredSignaturePayloadsAsync(100, default);
        Assert.Equal(0, cleaned);
        Assert.Equal(Png, (await fixture.SessionAsync(contract.Id)).SignatureImage);

        var summary = await fixture.Service.VerifyAndFinalizeAsync(fixture.Resident, false, contract.Id,
            new VerifySignature(challenge.SessionId, "123456"), default);
        Assert.Equal("COMPLETED", summary.Status);
        Assert.Null((await fixture.SessionAsync(contract.Id)).SignatureImage);
    }

    [Fact]
    public async Task Type_text_font_payload_is_cleaned_when_session_expires()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddPendingContractAsync();
        await fixture.Service.BeginSigningAsync(fixture.Resident, false, contract.Id,
            new BeginSignature(SignatureMethod.TYPE, null, "Nguyen Van A", "Arial"), default);
        var session = await fixture.SessionAsync(contract.Id);
        session.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await fixture.Db.SaveChangesAsync();

        var cleaned = await fixture.Service.CleanupExpiredSignaturePayloadsAsync(100, default);

        Assert.Equal(1, cleaned);
        session = await fixture.SessionAsync(contract.Id);
        Assert.Null(session.TypedSignatureText);
        Assert.Null(session.TypedSignatureFont);
    }

    [Fact]
    public async Task Session_without_payload_is_ignored_and_cleanup_is_idempotent()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddPendingContractAsync();
        await fixture.BeginAsync(contract.Id);
        var session = await fixture.SessionAsync(contract.Id);
        session.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await fixture.Db.SaveChangesAsync();

        Assert.Equal(1, await fixture.Service.CleanupExpiredSignaturePayloadsAsync(100, default));
        Assert.Equal(0, await fixture.Service.CleanupExpiredSignaturePayloadsAsync(100, default));
        Assert.Equal(0, await fixture.Service.CleanupExpiredSignaturePayloadsAsync(100, default));
    }

    [Fact]
    public async Task New_payload_replaced_by_begin_is_not_lost()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddPendingContractAsync();
        await fixture.BeginAsync(contract.Id);
        var session = await fixture.SessionAsync(contract.Id);
        session.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await fixture.Db.SaveChangesAsync();

        // User returns and starts a fresh signing attempt on the same session row.
        await fixture.Service.BeginSigningAsync(fixture.Resident, false, contract.Id,
            new BeginSignature(SignatureMethod.DRAW, Png, null, null), default);

        var cleaned = await fixture.Service.CleanupExpiredSignaturePayloadsAsync(100, default);

        Assert.Equal(0, cleaned);
        Assert.Equal(Png, (await fixture.SessionAsync(contract.Id)).SignatureImage);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Guid Owner { get; } = Guid.NewGuid();
        public Guid Resident { get; } = Guid.NewGuid();
        public RemoteOnlineSignDbContext Db { get; }
        public MemoryStorage Storage { get; } = new();
        public CountingOtp Otp { get; } = new();
        public RemoteContractService Service { get; }

        public Fixture()
        {
            Db = new(new DbContextOptionsBuilder<RemoteOnlineSignDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
            Service = new(Db, Storage, Otp, new PdfSignatureStamper(), NullLogger<RemoteContractService>.Instance,
                new RemoteTestAccountAccess(), new RemoteTestAccountDirectory(), new RemoteTestResidentScope(),
                new RemoteContractCodeGenerator(Db));
        }

        public async Task<RemoteContract> AddPendingContractAsync()
        {
            var contract = new RemoteContract
            {
                CreatorId = Owner, SigningMode = RemoteSigningMode.PRE_SIGNED_INVESTOR,
                Status = RemoteContractStatus.PENDING_RESIDENT_SIGNATURE,
                Deadline = DateTimeOffset.UtcNow.AddDays(7),
                ResidentId = Resident, ContractName = "Hợp đồng kiểm thử"
            };
            Db.Contracts.Add(contract);
            await Db.SaveChangesAsync();
            var bytes = Pdf();
            var key = "test/" + Guid.NewGuid().ToString("N");
            Storage.Data[key] = bytes;
            Db.Documents.Add(new ContractDocument
            {
                ContractId = contract.Id, StorageKey = key,
                Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                Size = bytes.Length, PageCount = 1
            });
            Db.SignatureFields.Add(new ContractSignatureField { ContractId = contract.Id, Role = SignatureRole.RESIDENT, Page = 1, X = 10, Y = 50, Width = 100, Height = 30 });
            await Db.SaveChangesAsync();
            return contract;
        }

        public Task<SigningChallenge> BeginAsync(Guid contractId) =>
            Service.BeginSigningAsync(Resident, false, contractId,
                new BeginSignature(SignatureMethod.DRAW, Png, null, null), default);

        public Task<ContractSigningSession> SessionAsync(Guid contractId) =>
            Db.SigningSessions.SingleAsync(x => x.ContractId == contractId);

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class CountingOtp : IPrivilegedActionOtp
    {
        private readonly Dictionary<Guid, (Guid User, string Purpose, string Binding)> challenges = new();

        public Task<PrivilegedActionChallengeResult> RequestAsync(Guid userId, string purpose, string payloadHash, CancellationToken ct)
        {
            var id = Guid.NewGuid();
            challenges[id] = (userId, purpose, payloadHash);
            return Task.FromResult(new PrivilegedActionChallengeResult(id,
                DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow.AddSeconds(30), "***"));
        }

        public Task VerifyAsync(Guid userId, Guid challengeId, string purpose, string payloadHash, string code, CancellationToken ct)
        {
            if (!challenges.TryGetValue(challengeId, out var c) || c.User != userId || c.Purpose != purpose || c.Binding != payloadHash || code != "123456")
                throw new InvalidOperationException("OTP rejected");
            challenges.Remove(challengeId);
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryStorage : IRemotePdfStorage
    {
        public Dictionary<string, byte[]> Data { get; } = new();
        public int FailNextDerivedPuts;
        public Task<StoredPdf> PutOriginalAsync(Guid contractId, Stream content, long length, CancellationToken ct) => Put(content, ct);
        public Task<StoredPdf> PutDerivedAsync(Guid contractId, string purpose, Stream content, long length, CancellationToken ct)
        {
            if (FailNextDerivedPuts > 0) { FailNextDerivedPuts--; throw new IOException("storage unavailable"); }
            return Put(content, ct);
        }
        private async Task<StoredPdf> Put(Stream content, CancellationToken ct)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, ct);
            var bytes = copy.ToArray();
            var key = "derived/" + Guid.NewGuid().ToString("N");
            Data[key] = bytes;
            return new StoredPdf(key, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.Length);
        }
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) =>
            Data.TryGetValue(key, out var bytes)
                ? Task.FromResult<Stream>(new MemoryStream(bytes, writable: false))
                : throw new FileNotFoundException();
        public Task<bool> ExistsWithHashAsync(string key, string hash, CancellationToken ct) => Task.FromResult(true);
        public Task DeleteIfUnreferencedAsync(string key, CancellationToken ct) { Data.Remove(key); return Task.CompletedTask; }
    }

    private static byte[] Pdf()
    {
        using var document = new PdfDocument();
        document.AddPage();
        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }
}
