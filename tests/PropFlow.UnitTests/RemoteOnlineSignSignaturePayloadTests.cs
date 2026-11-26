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

// Coverage for signature payload lifecycle cleanup (SIGNATURE-SECURITY-01):
// payload must be released after successful finalization and when a session is
// invalidated, while retryable sessions must keep their payload.
public sealed class RemoteOnlineSignSignaturePayloadTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
    private static readonly byte[] Png2 = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    [Fact]
    public async Task Draw_finalize_clears_payload_and_keeps_evidence()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddPendingContractAsync(fixture.Resident);
        var challenge = await fixture.Service.BeginSigningAsync(fixture.Resident, false, contract.Id,
            new BeginSignature(SignatureMethod.DRAW, Png, null, null), default);

        var summary = await fixture.Service.VerifyAndFinalizeAsync(fixture.Resident, false, contract.Id,
            new VerifySignature(challenge.SessionId, "123456"), default);

        Assert.Equal("COMPLETED", summary.Status);
        var session = await fixture.Db.SigningSessions.SingleAsync(x => x.ContractId == contract.Id);
        Assert.Null(session.SignatureImage);
        Assert.Null(session.TypedSignatureText);
        Assert.Null(session.TypedSignatureFont);
        Assert.NotNull(session.FinalizedAt);
        Assert.NotNull(session.OtpVerifiedAt);
        Assert.False(string.IsNullOrWhiteSpace(session.SignatureSha256));
        Assert.False(string.IsNullOrWhiteSpace(session.BindingHash));
        Assert.Equal(SignatureMethod.DRAW, session.Method);
        Assert.Equal(fixture.Resident, session.SignerId);
        Assert.Single(await fixture.Db.Documents.Where(x => x.ContractId == contract.Id && x.IsFinal).ToListAsync());
    }

    [Fact]
    public async Task Upload_and_type_image_finalize_clear_payload()
    {
        await using var fixture = new Fixture();
        var upload = await fixture.AddPendingContractAsync(fixture.Resident);
        var typeImage = await fixture.AddPendingContractAsync(fixture.Resident);

        var c1 = await fixture.Service.BeginSigningAsync(fixture.Resident, false, upload.Id,
            new BeginSignature(SignatureMethod.UPLOAD, Png, null, null), default);
        await fixture.Service.VerifyAndFinalizeAsync(fixture.Resident, false, upload.Id,
            new VerifySignature(c1.SessionId, "123456"), default);

        var c2 = await fixture.Service.BeginSigningAsync(fixture.Resident, false, typeImage.Id,
            new BeginSignature(SignatureMethod.TYPE, Png2, null, null), default);
        await fixture.Service.VerifyAndFinalizeAsync(fixture.Resident, false, typeImage.Id,
            new VerifySignature(c2.SessionId, "123456"), default);

        foreach (var id in new[] { upload.Id, typeImage.Id })
        {
            var session = await fixture.Db.SigningSessions.SingleAsync(x => x.ContractId == id);
            Assert.Null(session.SignatureImage);
            Assert.NotNull(session.FinalizedAt);
        }
    }

    [Fact]
    public async Task Type_text_path_finalize_clears_text_and_font()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddPendingContractAsync(fixture.Resident);
        var challenge = await fixture.Service.BeginSigningAsync(fixture.Resident, false, contract.Id,
            new BeginSignature(SignatureMethod.TYPE, null, "Nguyen Van A", "Arial"), default);

        var stored = await fixture.Db.SigningSessions.SingleAsync(x => x.ContractId == contract.Id);
        Assert.Equal("Nguyen Van A", stored.TypedSignatureText);
        Assert.Equal("Arial", stored.TypedSignatureFont);

        await fixture.Service.VerifyAndFinalizeAsync(fixture.Resident, false, contract.Id,
            new VerifySignature(challenge.SessionId, "123456"), default);

        stored = await fixture.Db.SigningSessions.SingleAsync(x => x.ContractId == contract.Id);
        Assert.Null(stored.SignatureImage);
        Assert.Null(stored.TypedSignatureText);
        Assert.Null(stored.TypedSignatureFont);
        Assert.NotNull(stored.FinalizedAt);
    }

    [Fact]
    public async Task Mode_a_investor_finalize_clears_payload_and_keeps_working_pdf()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddModeAContractAsync();
        var challenge = await fixture.Service.BeginSigningAsync(fixture.Owner, true, contract.Id,
            new BeginSignature(SignatureMethod.DRAW, Png, null, null), default);
        await fixture.Service.VerifyAndFinalizeAsync(fixture.Owner, true, contract.Id,
            new VerifySignature(challenge.SessionId, "123456"), default);

        var stored = await fixture.Db.Contracts.FindAsync(contract.Id);
        Assert.False(string.IsNullOrWhiteSpace(stored!.WorkingStorageKey));
        Assert.False(string.IsNullOrWhiteSpace(stored.WorkingSha256));
        var session = await fixture.Db.SigningSessions.SingleAsync(x => x.ContractId == contract.Id);
        Assert.Null(session.SignatureImage);
        Assert.NotNull(session.FinalizedAt);
        Assert.False(string.IsNullOrWhiteSpace(session.SignatureSha256));
    }

    [Fact]
    public async Task Wrong_otp_does_not_clear_retryable_payload()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddPendingContractAsync(fixture.Resident);
        var challenge = await fixture.Service.BeginSigningAsync(fixture.Resident, false, contract.Id,
            new BeginSignature(SignatureMethod.DRAW, Png, null, null), default);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.VerifyAndFinalizeAsync(fixture.Resident, false, contract.Id,
                new VerifySignature(challenge.SessionId, "000000"), default));

        var session = await fixture.Db.SigningSessions.SingleAsync(x => x.ContractId == contract.Id);
        Assert.Equal(Png, session.SignatureImage);
        Assert.Null(session.FinalizedAt);
        Assert.Null(session.OtpVerifiedAt);
    }

    [Fact]
    public async Task Verified_finalize_storage_failure_preserves_payload_then_retry_clears()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddPendingContractAsync(fixture.Resident);
        var challenge = await fixture.Service.BeginSigningAsync(fixture.Resident, false, contract.Id,
            new BeginSignature(SignatureMethod.DRAW, Png, null, null), default);

        fixture.Storage.FailNextDerivedPuts = 1;
        var failed = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.VerifyAndFinalizeAsync(fixture.Resident, false, contract.Id,
                new VerifySignature(challenge.SessionId, "123456"), default));
        Assert.Equal("pdf_storage_failed", failed.Code);

        var session = await fixture.Db.SigningSessions.SingleAsync(x => x.ContractId == contract.Id);
        Assert.Equal(Png, session.SignatureImage);
        Assert.NotNull(session.OtpVerifiedAt);
        Assert.Null(session.FinalizedAt);

        var summary = await fixture.Service.VerifyAndFinalizeAsync(fixture.Resident, false, contract.Id,
            new VerifySignature(challenge.SessionId, "123456"), default);
        Assert.Equal("COMPLETED", summary.Status);
        session = await fixture.Db.SigningSessions.SingleAsync(x => x.ContractId == contract.Id);
        Assert.Null(session.SignatureImage);
        Assert.NotNull(session.FinalizedAt);
    }

    [Fact]
    public async Task Idempotent_finalize_works_without_payload()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddPendingContractAsync(fixture.Resident);
        var challenge = await fixture.Service.BeginSigningAsync(fixture.Resident, false, contract.Id,
            new BeginSignature(SignatureMethod.DRAW, Png, null, null), default);
        await fixture.Service.VerifyAndFinalizeAsync(fixture.Resident, false, contract.Id,
            new VerifySignature(challenge.SessionId, "123456"), default);

        var again = await fixture.Service.VerifyAndFinalizeAsync(fixture.Resident, false, contract.Id,
            new VerifySignature(challenge.SessionId, "123456"), default);

        Assert.Equal("COMPLETED", again.Status);
        Assert.Single(await fixture.Db.Documents.Where(x => x.ContractId == contract.Id && x.IsFinal).ToListAsync());
        Assert.Single(await fixture.Db.Events.Where(x => x.ContractId == contract.Id && x.Type == "CONTRACT_COMPLETED").ToListAsync());
    }

    [Fact]
    public async Task Reject_clears_unfinalized_payload_but_keeps_evidence()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddPendingContractAsync(fixture.Resident);
        await fixture.Service.BeginSigningAsync(fixture.Resident, false, contract.Id,
            new BeginSignature(SignatureMethod.UPLOAD, Png, null, null), default);

        await fixture.Service.RejectAsync(fixture.Resident, contract.Id, "Từ chối", default);

        var session = await fixture.Db.SigningSessions.SingleAsync(x => x.ContractId == contract.Id);
        Assert.Null(session.SignatureImage);
        Assert.True(session.ExpiresAt <= DateTimeOffset.UtcNow);
        Assert.False(string.IsNullOrWhiteSpace(session.SignatureSha256));
        Assert.False(string.IsNullOrWhiteSpace(session.BindingHash));
        Assert.Null(session.FinalizedAt);
    }

    [Fact]
    public async Task Expire_clears_unfinalized_payload()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddPendingContractAsync(fixture.Resident);
        await fixture.Service.BeginSigningAsync(fixture.Resident, false, contract.Id,
            new BeginSignature(SignatureMethod.DRAW, Png, null, null), default);

        var stored = await fixture.Db.Contracts.FindAsync(contract.Id);
        stored!.Deadline = DateTimeOffset.UtcNow.AddHours(-1);
        await fixture.Db.SaveChangesAsync();
        await fixture.Service.ListAsync(fixture.Owner, true, default);

        var session = await fixture.Db.SigningSessions.SingleAsync(x => x.ContractId == contract.Id);
        Assert.Null(session.SignatureImage);
        Assert.True(session.ExpiresAt <= DateTimeOffset.UtcNow);
        Assert.Equal(RemoteContractStatus.EXPIRED, stored.Status);
    }

    [Fact]
    public async Task Resend_same_resident_stores_fresh_payload()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddPendingContractAsync(fixture.Resident);
        await fixture.Service.BeginSigningAsync(fixture.Resident, false, contract.Id,
            new BeginSignature(SignatureMethod.DRAW, Png, null, null), default);
        await fixture.Service.RejectAsync(fixture.Resident, contract.Id, "Từ chối", default);
        var rejected = await fixture.Db.Contracts.FindAsync(contract.Id);
        await fixture.Service.ReassignResidentAsync(fixture.Owner, contract.Id,
            new ReassignRemoteResident(fixture.Resident, rejected!.Version), default);

        var fresh = await fixture.Service.BeginSigningAsync(fixture.Resident, false, contract.Id,
            new BeginSignature(SignatureMethod.DRAW, Png2, null, null), default);

        var session = await fixture.Db.SigningSessions.SingleAsync(x => x.ContractId == contract.Id);
        Assert.Equal(Png2, session.SignatureImage);
        Assert.NotEqual(fresh.SessionId, Guid.Empty);
    }

    [Fact]
    public async Task Renewal_preserves_finalized_signing_evidence()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddModeAContractAsync();
        var challenge = await fixture.Service.BeginSigningAsync(fixture.Owner, true, contract.Id,
            new BeginSignature(SignatureMethod.DRAW, Png, null, null), default);
        await fixture.Service.VerifyAndFinalizeAsync(fixture.Owner, true, contract.Id,
            new VerifySignature(challenge.SessionId, "123456"), default);
        await fixture.Service.AssignAsync(fixture.Owner, contract.Id, new(fixture.Resident), default);
        await fixture.Service.SendAsync(fixture.Owner, contract.Id, new(), default);

        var stored = await fixture.Db.Contracts.FindAsync(contract.Id);
        stored!.Deadline = DateTimeOffset.UtcNow.AddHours(-1);
        await fixture.Db.SaveChangesAsync();
        await fixture.Service.ListAsync(fixture.Owner, true, default);
        stored = await fixture.Db.Contracts.FindAsync(contract.Id);
        Assert.Equal(RemoteContractStatus.EXPIRED, stored!.Status);

        await fixture.Service.RenewDeadlineAsync(fixture.Owner, contract.Id,
            new RenewRemoteDeadline(DateTimeOffset.UtcNow.AddDays(3), stored.Version), default);

        var session = await fixture.Db.SigningSessions.SingleAsync(x => x.ContractId == contract.Id);
        Assert.NotNull(session.FinalizedAt);
        Assert.Null(session.SignatureImage);
        Assert.False(string.IsNullOrWhiteSpace(session.SignatureSha256));
        Assert.False(string.IsNullOrWhiteSpace(stored.WorkingSha256));
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

        public async Task<RemoteContract> AddPendingContractAsync(Guid residentId)
        {
            var contract = new RemoteContract
            {
                CreatorId = Owner, SigningMode = RemoteSigningMode.PRE_SIGNED_INVESTOR,
                Status = RemoteContractStatus.PENDING_RESIDENT_SIGNATURE,
                Deadline = DateTimeOffset.UtcNow.AddDays(7),
                ResidentId = residentId, ContractName = "Hợp đồng kiểm thử"
            };
            Db.Contracts.Add(contract);
            await Db.SaveChangesAsync();
            await UploadPdfAsync(contract);
            await Db.SignatureFields.AddAsync(new ContractSignatureField { ContractId = contract.Id, Role = SignatureRole.RESIDENT, Page = 1, X = 10, Y = 50, Width = 100, Height = 30 });
            await Db.SaveChangesAsync();
            return contract;
        }

        public async Task<RemoteContract> AddModeAContractAsync()
        {
            var contract = new RemoteContract
            {
                CreatorId = Owner, SigningMode = RemoteSigningMode.INVESTOR_AND_RESIDENT,
                Status = RemoteContractStatus.DRAFT, ContractName = "Hợp đồng kiểm thử"
            };
            Db.Contracts.Add(contract);
            await Db.SaveChangesAsync();
            await UploadPdfAsync(contract);
            await Db.SignatureFields.AddAsync(new ContractSignatureField { ContractId = contract.Id, Role = SignatureRole.INVESTOR, Page = 1, X = 10, Y = 10, Width = 100, Height = 30 });
            await Db.SignatureFields.AddAsync(new ContractSignatureField { ContractId = contract.Id, Role = SignatureRole.RESIDENT, Page = 1, X = 10, Y = 50, Width = 100, Height = 30 });
            await Db.SaveChangesAsync();
            return contract;
        }

        private async Task UploadPdfAsync(RemoteContract contract)
        {
            var bytes = Pdf();
            var key = "test/" + Guid.NewGuid().ToString("N");
            Storage.Data[key] = bytes;
            Db.Documents.Add(new ContractDocument
            {
                ContractId = contract.Id, StorageKey = key,
                Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                Size = bytes.Length, PageCount = 1
            });
            await Db.SaveChangesAsync();
        }

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
