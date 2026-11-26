using System.Security.Cryptography;
using System.Text.Json;
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

// Coverage for resident rejection with reason and ADMIN reassignment
// (UI-06.7C/D). Uses EF InMemory like the other RemoteOnlineSign tests.
public sealed class RemoteOnlineSignRejectionTests
{
    [Fact]
    public async Task Reject_requires_reason_and_length_limit()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddPendingContractAsync(fixture.Resident);

        foreach (var bad in new string?[] { null, "", "   " })
        {
            var error = await Assert.ThrowsAsync<RemoteContractException>(
                () => fixture.Service.RejectAsync(fixture.Resident, contract.Id, bad, default));
            Assert.Equal("reject_reason_required", error.Code);
        }
        var tooLong = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.RejectAsync(fixture.Resident, contract.Id, new string('a', 501), default));
        Assert.Equal("reject_reason_too_long", tooLong.Code);
        Assert.Equal(RemoteContractStatus.PENDING_RESIDENT_SIGNATURE,
            (await fixture.Db.Contracts.FindAsync(contract.Id))!.Status);
    }

    [Fact]
    public async Task Reject_wrong_resident_or_wrong_status_is_blocked()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddPendingContractAsync(fixture.Resident);
        var stranger = Guid.NewGuid();

        var wrongResident = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.RejectAsync(stranger, contract.Id, "Không đồng ý", default));
        Assert.Equal(404, wrongResident.Status);

        var adminReject = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.RejectAsync(fixture.Owner, contract.Id, "Không đồng ý", default));
        Assert.Equal(404, adminReject.Status);

        var storedContract = await fixture.Db.Contracts.FindAsync(contract.Id);
        storedContract!.Status = RemoteContractStatus.COMPLETED;
        await fixture.Db.SaveChangesAsync();
        var completed = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.RejectAsync(fixture.Resident, contract.Id, "Không đồng ý", default));
        Assert.Equal("contract_not_pending", completed.Code);
    }

    [Fact]
    public async Task Reject_persists_reason_and_blocks_old_session()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddPendingContractAsync(fixture.Resident);
        var challenge = await fixture.Service.BeginSigningAsync(fixture.Resident, false, contract.Id,
            new BeginSignature(SignatureMethod.TYPE, null, "Resident", "Arial"), default);

        await fixture.Service.RejectAsync(fixture.Resident, contract.Id, "  Nội dung hợp đồng không đúng.  ", default);

        var stored = await fixture.Db.Contracts.FindAsync(contract.Id);
        Assert.Equal(RemoteContractStatus.REJECTED, stored!.Status);
        var audit = await fixture.Db.Events.SingleAsync(x => x.ContractId == contract.Id && x.Type == "CONTRACT_REJECTED");
        Assert.Equal(fixture.Resident, audit.ActorId);
        Assert.Equal("Nội dung hợp đồng không đúng.", ReasonOf(audit.Metadata));

        var session = await fixture.Db.SigningSessions.SingleAsync(x => x.ContractId == contract.Id);
        Assert.True(session.ExpiresAt <= DateTimeOffset.UtcNow);

        var verify = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.VerifyAndFinalizeAsync(fixture.Resident, false, contract.Id,
                new VerifySignature(challenge.SessionId, "123456"), default));
        Assert.Contains(verify.Code, new[] { "signing_session_expired", "signing_not_available", "contract_expired", "signing_context_changed" });
        Assert.Equal(RemoteContractStatus.REJECTED, (await fixture.Db.Contracts.FindAsync(contract.Id))!.Status);
    }

    [Fact]
    public async Task Reassign_mode_b_restores_pending_resident_and_preserves_documents()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddPendingContractAsync(fixture.Resident);
        await fixture.Service.RejectAsync(fixture.Resident, contract.Id, "Từ chối", default);
        var rejected = await fixture.Db.Contracts.FindAsync(contract.Id);
        var newResident = Guid.NewGuid();

        var summary = await fixture.Service.ReassignResidentAsync(fixture.Owner, contract.Id,
            new ReassignRemoteResident(newResident, rejected!.Version), default);

        Assert.Equal("PENDING_RESIDENT_SIGNATURE", summary.Status);
        var stored = await fixture.Db.Contracts.FindAsync(contract.Id);
        Assert.Equal(RemoteContractStatus.PENDING_RESIDENT_SIGNATURE, stored!.Status);
        Assert.Equal(newResident, stored.ResidentId);
        Assert.Equal(contract.ContractName, stored.ContractName);
        Assert.Equal(contract.ContractCode, stored.ContractCode);
        Assert.Single(await fixture.Db.Documents.Where(x => x.ContractId == contract.Id && !x.IsFinal).ToListAsync());
        Assert.Contains(await fixture.Db.Events.Where(x => x.ContractId == contract.Id).ToListAsync(),
            x => x.Type == "RESIDENT_REASSIGNED" && x.Metadata.Contains(newResident.ToString()));
        Assert.Contains(await fixture.Db.Events.Where(x => x.ContractId == contract.Id).ToListAsync(),
            x => x.Type == "CONTRACT_REJECTED");
    }

    [Fact]
    public async Task Reassign_mode_a_preserves_investor_signature()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddModeAContractAsync();
        await fixture.SignAsInvestorAsync(contract.Id);
        await fixture.Service.AssignAsync(fixture.Owner, contract.Id, new(fixture.Resident), default);
        await fixture.Service.SendAsync(fixture.Owner, contract.Id, new(), default);
        await fixture.Service.RejectAsync(fixture.Resident, contract.Id, "Từ chối", default);
        var rejected = await fixture.Db.Contracts.FindAsync(contract.Id);
        Assert.False(string.IsNullOrWhiteSpace(rejected!.WorkingSha256));

        var newResident = Guid.NewGuid();
        var summary = await fixture.Service.ReassignResidentAsync(fixture.Owner, contract.Id,
            new ReassignRemoteResident(newResident, rejected.Version), default);

        Assert.Equal("PENDING_RESIDENT_SIGNATURE", summary.Status);
        var stored = await fixture.Db.Contracts.FindAsync(contract.Id);
        Assert.Equal(RemoteContractStatus.PENDING_RESIDENT_SIGNATURE, stored!.Status);
        Assert.Equal(rejected.WorkingSha256, stored.WorkingSha256);
        Assert.Equal(2, await fixture.Db.SignatureFields.CountAsync(x => x.ContractId == contract.Id));
    }

    [Fact]
    public async Task Reassign_validates_status_admin_and_deadline()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddPendingContractAsync(fixture.Resident);
        await fixture.Service.RejectAsync(fixture.Resident, contract.Id, "Từ chối", default);
        var rejected = await fixture.Db.Contracts.FindAsync(contract.Id);

        var notRejected = await fixture.AddPendingContractAsync(Guid.NewGuid());
        var wrong = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.ReassignResidentAsync(fixture.Owner, notRejected.Id,
                new ReassignRemoteResident(Guid.NewGuid(), notRejected.Version), default));
        Assert.Equal("contract_not_rejected", wrong.Code);

        var residentDenied = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.ReassignResidentAsync(fixture.Resident, contract.Id,
                new ReassignRemoteResident(Guid.NewGuid(), rejected.Version), default));
        Assert.Equal(404, residentDenied.Status);

        var toExpire = await fixture.Db.Contracts.FindAsync(contract.Id);
        toExpire!.Deadline = DateTimeOffset.UtcNow.AddHours(-1);
        await fixture.Db.SaveChangesAsync();
        var expiredDeadline = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.ReassignResidentAsync(fixture.Owner, contract.Id,
                new ReassignRemoteResident(Guid.NewGuid(), rejected.Version), default));
        Assert.Equal("deadline_expired", expiredDeadline.Code);
    }

    [Fact]
    public async Task Reassign_same_resident_mode_b_resends_with_fresh_session()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddPendingContractAsync(fixture.Resident);
        var stale = await fixture.Service.BeginSigningAsync(fixture.Resident, false, contract.Id,
            new BeginSignature(SignatureMethod.TYPE, null, "Resident", "Arial"), default);
        await fixture.Service.RejectAsync(fixture.Resident, contract.Id, "Từ chối", default);
        var rejected = await fixture.Db.Contracts.FindAsync(contract.Id);

        var summary = await fixture.Service.ReassignResidentAsync(fixture.Owner, contract.Id,
            new ReassignRemoteResident(fixture.Resident, rejected!.Version), default);

        Assert.Equal("PENDING_RESIDENT_SIGNATURE", summary.Status);
        var stored = await fixture.Db.Contracts.FindAsync(contract.Id);
        Assert.Equal(fixture.Resident, stored!.ResidentId);
        Assert.Single(await fixture.Db.Documents.Where(x => x.ContractId == contract.Id && !x.IsFinal).ToListAsync());
        var events = await fixture.Db.Events.Where(x => x.ContractId == contract.Id).ToListAsync();
        Assert.Contains(events, x => x.Type == "CONTRACT_RESENT_TO_RESIDENT");
        Assert.DoesNotContain(events, x => x.Type == "RESIDENT_REASSIGNED");
        Assert.Contains(events, x => x.Type == "CONTRACT_REJECTED");

        var oldVerify = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.VerifyAndFinalizeAsync(fixture.Resident, false, contract.Id,
                new VerifySignature(stale.SessionId, "123456"), default));
        Assert.Contains(oldVerify.Code, new[] { "signing_session_expired", "signing_not_available", "signing_context_changed" });

        var fresh = await fixture.Service.BeginSigningAsync(fixture.Resident, false, contract.Id,
            new BeginSignature(SignatureMethod.TYPE, null, "Resident", "Arial"), default);
        var done = await fixture.Service.VerifyAndFinalizeAsync(fixture.Resident, false, contract.Id,
            new VerifySignature(fresh.SessionId, "123456"), default);
        Assert.Equal("COMPLETED", done.Status);
    }

    [Fact]
    public async Task Reassign_same_resident_mode_a_preserves_investor_signature()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddModeAContractAsync();
        await fixture.SignAsInvestorAsync(contract.Id);
        await fixture.Service.AssignAsync(fixture.Owner, contract.Id, new(fixture.Resident), default);
        await fixture.Service.SendAsync(fixture.Owner, contract.Id, new(), default);
        await fixture.Service.RejectAsync(fixture.Resident, contract.Id, "Từ chối", default);
        var rejected = await fixture.Db.Contracts.FindAsync(contract.Id);

        var summary = await fixture.Service.ReassignResidentAsync(fixture.Owner, contract.Id,
            new ReassignRemoteResident(fixture.Resident, rejected!.Version), default);

        Assert.Equal("PENDING_RESIDENT_SIGNATURE", summary.Status);
        var stored = await fixture.Db.Contracts.FindAsync(contract.Id);
        Assert.Equal(RemoteContractStatus.PENDING_RESIDENT_SIGNATURE, stored!.Status);
        Assert.Equal(rejected.WorkingSha256, stored.WorkingSha256);
        Assert.Equal(2, await fixture.Db.SignatureFields.CountAsync(x => x.ContractId == contract.Id));
        Assert.Contains(await fixture.Db.Events.Where(x => x.ContractId == contract.Id).ToListAsync(),
            x => x.Type == "CONTRACT_RESENT_TO_RESIDENT");
    }

    [Fact]
    public async Task Old_resident_loses_access_and_cannot_see_rejection_reason()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddPendingContractAsync(fixture.Resident);
        await fixture.Service.RejectAsync(fixture.Resident, contract.Id, "Lý do riêng tư", default);
        var rejected = await fixture.Db.Contracts.FindAsync(contract.Id);
        var newResident = Guid.NewGuid();
        await fixture.Service.ReassignResidentAsync(fixture.Owner, contract.Id,
            new ReassignRemoteResident(newResident, rejected!.Version), default);

        var oldList = await fixture.Service.ListAsync(fixture.Resident, false, default);
        Assert.DoesNotContain(oldList, x => x.Id == contract.Id);
        var oldGet = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.GetAsync(fixture.Resident, false, contract.Id, default));
        Assert.Equal(404, oldGet.Status);
        var oldHistory = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.HistoryAsync(fixture.Resident, false, contract.Id, default));
        Assert.Equal(404, oldHistory.Status);

        var newHistory = await fixture.Service.HistoryAsync(newResident, false, contract.Id, default);
        Assert.DoesNotContain(newHistory, x => x.Type == "CONTRACT_REJECTED");
        Assert.Contains(newHistory, x => x.Type == "RESIDENT_REASSIGNED");

        var newList = await fixture.Service.ListAsync(newResident, false, default);
        Assert.Contains(newList, x => x.Id == contract.Id && x.Status == "PENDING_RESIDENT_SIGNATURE");
    }

    [Fact]
    public async Task Multiple_rejections_keep_full_history()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddPendingContractAsync(fixture.Resident);
        var resident2 = Guid.NewGuid();
        var resident3 = Guid.NewGuid();

        await fixture.Service.RejectAsync(fixture.Resident, contract.Id, "Lý do 1", default);
        var r1 = await fixture.Db.Contracts.FindAsync(contract.Id);
        await fixture.Service.ReassignResidentAsync(fixture.Owner, contract.Id, new(resident2, r1!.Version), default);

        await fixture.Service.RejectAsync(resident2, contract.Id, "Lý do 2", default);
        var r2 = await fixture.Db.Contracts.FindAsync(contract.Id);
        await fixture.Service.ReassignResidentAsync(fixture.Owner, contract.Id, new(resident3, r2!.Version), default);

        var adminHistory = await fixture.Service.HistoryAsync(fixture.Owner, true, contract.Id, default);
        var rejections = adminHistory.Where(x => x.Type == "CONTRACT_REJECTED").ToList();
        Assert.Equal(2, rejections.Count);
        Assert.Contains(rejections, x => ReasonOf(x.Metadata) == "Lý do 1");
        Assert.Contains(rejections, x => ReasonOf(x.Metadata) == "Lý do 2");
        Assert.Equal(2, adminHistory.Count(x => x.Type == "RESIDENT_REASSIGNED"));

        var stored = await fixture.Db.Contracts.FindAsync(contract.Id);
        Assert.Equal(resident3, stored!.ResidentId);
        Assert.Equal(RemoteContractStatus.PENDING_RESIDENT_SIGNATURE, stored.Status);
    }

    [Fact]
    public async Task New_resident_can_sign_after_reassignment()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddPendingContractAsync(fixture.Resident);
        await fixture.Service.RejectAsync(fixture.Resident, contract.Id, "Từ chối", default);
        var rejected = await fixture.Db.Contracts.FindAsync(contract.Id);
        var newResident = Guid.NewGuid();
        await fixture.Service.ReassignResidentAsync(fixture.Owner, contract.Id,
            new ReassignRemoteResident(newResident, rejected!.Version), default);

        var challenge = await fixture.Service.BeginSigningAsync(newResident, false, contract.Id,
            new BeginSignature(SignatureMethod.TYPE, null, "New Resident", "Arial"), default);
        var done = await fixture.Service.VerifyAndFinalizeAsync(newResident, false, contract.Id,
            new VerifySignature(challenge.SessionId, "123456"), default);
        Assert.Equal("COMPLETED", done.Status);
    }

    private static string? ReasonOf(string metadata)
    {
        using var json = JsonDocument.Parse(metadata);
        return json.RootElement.TryGetProperty("reason", out var value) ? value.GetString() : null;
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

        public async Task<RemoteContract> AddPendingContractAsync(Guid residentId, RemoteSigningMode mode = RemoteSigningMode.PRE_SIGNED_INVESTOR)
        {
            var contract = new RemoteContract
            {
                CreatorId = Owner, SigningMode = mode,
                Status = RemoteContractStatus.PENDING_RESIDENT_SIGNATURE,
                Deadline = DateTimeOffset.UtcNow.AddDays(7),
                ResidentId = residentId, ContractName = "Hợp đồng kiểm thử"
            };
            Db.Contracts.Add(contract);
            await Db.SaveChangesAsync();
            await UploadPdfAsync(contract);
            if (mode == RemoteSigningMode.INVESTOR_AND_RESIDENT)
                await Db.SignatureFields.AddAsync(new ContractSignatureField { ContractId = contract.Id, Role = SignatureRole.INVESTOR, Page = 1, X = 10, Y = 10, Width = 100, Height = 30 });
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
