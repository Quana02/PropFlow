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

// Regression coverage for the Mode A "investor signs first, then resident is
// assigned and the contract is sent" workflow and the legacy
// PENDING_INVESTOR_SIGNATURE path. Uses EF InMemory; PostgreSQL concurrency is
// covered by the dedicated integration tests.
public sealed class RemoteOnlineSignWorkflowTests
{
    [Fact]
    public async Task Mode_a_draft_without_resident_allows_admin_to_request_otp()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddModeAContractAsync();

        var challenge = await fixture.Service.BeginSigningAsync(
            fixture.Owner, true, contract.Id,
            new BeginSignature(SignatureMethod.TYPE, null, "Investor", "Arial"), default);

        Assert.NotEqual(Guid.Empty, challenge.SessionId);
        Assert.Equal(1, fixture.Otp.RequestCalls);
        var session = await fixture.Db.SigningSessions.SingleAsync();
        Assert.Equal(SignatureRole.INVESTOR, session.Role);
        Assert.Equal(fixture.Owner, session.SignerId);
        Assert.Null((await fixture.Db.Contracts.FindAsync(contract.Id))!.ResidentId);
        Assert.Equal(RemoteContractStatus.DRAFT, (await fixture.Db.Contracts.FindAsync(contract.Id))!.Status);
    }

    [Fact]
    public async Task Mode_a_draft_without_investor_signature_rejects_assign_and_send()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddModeAContractAsync();

        var assign = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.AssignAsync(fixture.Owner, contract.Id, new(fixture.Resident), default));
        Assert.Equal("investor_signature_required", assign.Code);
        Assert.Equal(409, assign.Status);

        var storedContract = await fixture.Db.Contracts.FindAsync(contract.Id);
        storedContract!.ResidentId = fixture.Resident;
        await fixture.Db.SaveChangesAsync();
        var send = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.SendAsync(fixture.Owner, contract.Id, new(), default));
        Assert.Equal("investor_signature_required", send.Code);
        Assert.Equal(409, send.Status);
        Assert.Equal(RemoteContractStatus.DRAFT, (await fixture.Db.Contracts.FindAsync(contract.Id))!.Status);
    }

    [Fact]
    public async Task Mode_a_admin_verify_and_finalize_moves_draft_to_investor_signed_draft()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddModeAContractAsync();
        var challenge = await fixture.Service.BeginSigningAsync(
            fixture.Owner, true, contract.Id,
            new BeginSignature(SignatureMethod.TYPE, null, "Investor", "Arial"), default);

        var summary = await fixture.Service.VerifyAndFinalizeAsync(
            fixture.Owner, true, contract.Id, new VerifySignature(challenge.SessionId, "123456"), default);

        Assert.Equal("INVESTOR_SIGNED_DRAFT", summary.Status);
        var stored = await fixture.Db.Contracts.FindAsync(contract.Id);
        Assert.Equal(RemoteContractStatus.INVESTOR_SIGNED_DRAFT, stored!.Status);
        Assert.False(string.IsNullOrWhiteSpace(stored.WorkingStorageKey));
        Assert.False(string.IsNullOrWhiteSpace(stored.WorkingSha256));
        Assert.Empty(fixture.Db.Documents.Where(x => x.IsFinal));
        Assert.Equal(1, fixture.Otp.VerifyCalls);
    }

    [Fact]
    public async Task Mode_a_after_admin_signature_assign_and_send_reaches_pending_resident()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddModeAContractAsync();
        await fixture.SignAsInvestorAsync(contract.Id);

        var assigned = await fixture.Service.AssignAsync(fixture.Owner, contract.Id, new(fixture.Resident), default);
        Assert.Equal(fixture.Resident, assigned.ResidentId);

        await fixture.Service.SendAsync(fixture.Owner, contract.Id, new(), default);

        var stored = await fixture.Db.Contracts.FindAsync(contract.Id);
        Assert.Equal(RemoteContractStatus.PENDING_RESIDENT_SIGNATURE, stored!.Status);
    }

    [Fact]
    public async Task Mode_a_resident_cannot_access_contract_before_it_is_sent()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddModeAContractAsync();
        await fixture.SignAsInvestorAsync(contract.Id);
        await fixture.Service.AssignAsync(fixture.Owner, contract.Id, new(fixture.Resident), default);

        var error = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.GetAsync(fixture.Resident, false, contract.Id, default));
        var signError = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.BeginSigningAsync(fixture.Resident, false, contract.Id,
                new BeginSignature(SignatureMethod.TYPE, null, "Resident", "Arial"), default));

        Assert.Equal(404, error.Status);
        Assert.Equal("contract_not_found", error.Code);
        Assert.Equal("contract_not_found", signError.Code);
    }

    [Fact]
    public async Task Legacy_pending_investor_signature_admin_can_still_sign_and_send_to_resident()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddModeAContractAsync();
        contract.Status = RemoteContractStatus.PENDING_INVESTOR_SIGNATURE;
        contract.ResidentId = fixture.Resident;
        await fixture.Db.SaveChangesAsync();

        var challenge = await fixture.Service.BeginSigningAsync(
            fixture.Owner, true, contract.Id,
            new BeginSignature(SignatureMethod.TYPE, null, "Investor", "Arial"), default);
        var summary = await fixture.Service.VerifyAndFinalizeAsync(
            fixture.Owner, true, contract.Id, new VerifySignature(challenge.SessionId, "123456"), default);

        Assert.Equal("PENDING_RESIDENT_SIGNATURE", summary.Status);
        Assert.Equal(RemoteContractStatus.PENDING_RESIDENT_SIGNATURE,
            (await fixture.Db.Contracts.FindAsync(contract.Id))!.Status);
    }

    [Fact]
    public async Task Mode_b_draft_does_not_require_admin_signature()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddModeBContractAsync();

        var adminSign = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.BeginSigningAsync(fixture.Owner, true, contract.Id,
                new BeginSignature(SignatureMethod.TYPE, null, "Investor", "Arial"), default));
        Assert.Equal("signing_not_available", adminSign.Code);

        await fixture.Service.AssignAsync(fixture.Owner, contract.Id, new(fixture.Resident), default);
        await fixture.Service.SendAsync(fixture.Owner, contract.Id, new(), default);

        var stored = await fixture.Db.Contracts.FindAsync(contract.Id);
        Assert.Equal(RemoteContractStatus.PENDING_RESIDENT_SIGNATURE, stored!.Status);

        var residentChallenge = await fixture.Service.BeginSigningAsync(
            fixture.Resident, false, contract.Id,
            new BeginSignature(SignatureMethod.TYPE, null, "Resident", "Arial"), default);
        var done = await fixture.Service.VerifyAndFinalizeAsync(
            fixture.Resident, false, contract.Id, new VerifySignature(residentChallenge.SessionId, "123456"), default);
        Assert.Equal("COMPLETED", done.Status);
    }

    [Fact]
    public async Task Mode_a_admin_cannot_sign_twice_after_finalize()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddModeAContractAsync();
        await fixture.SignAsInvestorAsync(contract.Id);

        var error = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.BeginSigningAsync(fixture.Owner, true, contract.Id,
                new BeginSignature(SignatureMethod.TYPE, null, "Investor", "Arial"), default));

        Assert.Equal("signing_not_available", error.Code);
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

        public async Task<RemoteContract> AddModeAContractAsync()
        {
            var contract = new RemoteContract
            {
                CreatorId = Owner,
                SigningMode = RemoteSigningMode.INVESTOR_AND_RESIDENT,
                ContractName = "Hợp đồng kiểm thử"
            };
            Db.Contracts.Add(contract);
            await Db.SaveChangesAsync();
            await UploadPdfAsync(contract);
            await Db.SignatureFields.AddAsync(new ContractSignatureField { ContractId = contract.Id, Role = SignatureRole.INVESTOR, Page = 1, X = 10, Y = 10, Width = 100, Height = 30 });
            await Db.SignatureFields.AddAsync(new ContractSignatureField { ContractId = contract.Id, Role = SignatureRole.RESIDENT, Page = 1, X = 10, Y = 50, Width = 100, Height = 30 });
            await Db.SaveChangesAsync();
            return contract;
        }

        public async Task<RemoteContract> AddModeBContractAsync()
        {
            var contract = new RemoteContract
            {
                CreatorId = Owner,
                SigningMode = RemoteSigningMode.PRE_SIGNED_INVESTOR,
                ContractName = "Hợp đồng kiểm thử"
            };
            Db.Contracts.Add(contract);
            await Db.SaveChangesAsync();
            await UploadPdfAsync(contract);
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

        public async Task<Guid> SignAsInvestorAsync(Guid contractId)
        {
            var challenge = await Service.BeginSigningAsync(Owner, true, contractId,
                new BeginSignature(SignatureMethod.TYPE, null, "Investor", "Arial"), default);
            await Service.VerifyAndFinalizeAsync(Owner, true, contractId,
                new VerifySignature(challenge.SessionId, "123456"), default);
            return challenge.SessionId;
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class CountingOtp : IPrivilegedActionOtp
    {
        public int RequestCalls;
        public int VerifyCalls;
        private readonly Dictionary<Guid, (Guid User, string Purpose, string Binding)> challenges = new();

        public Task<PrivilegedActionChallengeResult> RequestAsync(Guid userId, string purpose, string payloadHash, CancellationToken ct)
        {
            RequestCalls++;
            var id = Guid.NewGuid();
            challenges[id] = (userId, purpose, payloadHash);
            return Task.FromResult(new PrivilegedActionChallengeResult(id,
                DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow.AddSeconds(30), "***"));
        }

        public Task VerifyAsync(Guid userId, Guid challengeId, string purpose, string payloadHash, string code, CancellationToken ct)
        {
            VerifyCalls++;
            if (!challenges.TryGetValue(challengeId, out var c) || c.User != userId || c.Purpose != purpose || c.Binding != payloadHash || code != "123456")
                throw new InvalidOperationException("OTP rejected");
            challenges.Remove(challengeId);
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryStorage : IRemotePdfStorage
    {
        public Dictionary<string, byte[]> Data { get; } = new();
        public Task<StoredPdf> PutOriginalAsync(Guid contractId, Stream content, long length, CancellationToken ct) => Put(content, ct);
        public Task<StoredPdf> PutDerivedAsync(Guid contractId, string purpose, Stream content, long length, CancellationToken ct) => Put(content, ct);
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
