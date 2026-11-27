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

// Coverage for lazy deadline expiration and ADMIN deadline renewal
// (UI-06.7B). Uses EF InMemory like RemoteOnlineSignWorkflowTests.
public sealed class RemoteOnlineSignExpirationTests
{
    [Fact]
    public async Task Pending_contract_past_deadline_expires_once_on_list()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddContractAsync(RemoteSigningMode.PRE_SIGNED_INVESTOR,
            RemoteContractStatus.PENDING_RESIDENT_SIGNATURE, DateTimeOffset.UtcNow.AddHours(-1), fixture.Resident);

        var list = await fixture.Service.ListAsync(fixture.Owner, true, default);
        var item = Assert.Single(list, x => x.Id == contract.Id);
        Assert.Equal("EXPIRED", item.Status);
        Assert.Equal(RemoteContractStatus.EXPIRED, (await fixture.Db.Contracts.FindAsync(contract.Id))!.Status);

        var events = await fixture.Db.Events.CountAsync(x => x.ContractId == contract.Id && x.Type == "CONTRACT_EXPIRED");
        Assert.Equal(1, events);

        await fixture.Service.ListAsync(fixture.Owner, true, default);
        Assert.Equal(1, await fixture.Db.Events.CountAsync(x => x.ContractId == contract.Id && x.Type == "CONTRACT_EXPIRED"));
    }

    [Fact]
    public async Task Due_contract_worker_expires_once_and_publishes_after_transition()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddContractAsync(RemoteSigningMode.PRE_SIGNED_INVESTOR,
            RemoteContractStatus.PENDING_RESIDENT_SIGNATURE, DateTimeOffset.UtcNow.AddMinutes(-1), fixture.Resident);

        Assert.Equal(1, await fixture.Service.ExpireDueContractsAsync(100, default));
        Assert.Equal(RemoteContractStatus.EXPIRED, (await fixture.Db.Contracts.FindAsync(contract.Id))!.Status);
        var change = Assert.Single(fixture.Notifier.Changes);
        Assert.Equal((contract.Id, "PENDING_RESIDENT_SIGNATURE", "EXPIRED"), (change.ContractId, change.PreviousStatus, change.NewStatus));

        Assert.Equal(0, await fixture.Service.ExpireDueContractsAsync(100, default));
        Assert.Single(fixture.Notifier.Changes);
        Assert.Equal(1, await fixture.Db.Events.CountAsync(x => x.ContractId == contract.Id && x.Type == "CONTRACT_EXPIRED"));
    }

    [Fact]
    public async Task Successful_rejection_publishes_and_notifier_failure_does_not_rollback()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddContractAsync(RemoteSigningMode.PRE_SIGNED_INVESTOR,
            RemoteContractStatus.PENDING_RESIDENT_SIGNATURE, DateTimeOffset.UtcNow.AddDays(1), fixture.Resident);

        fixture.Notifier.Throw = true;
        await fixture.Service.RejectAsync(fixture.Resident, contract.Id, "Không đồng ý", default);

        Assert.Equal(RemoteContractStatus.REJECTED, (await fixture.Db.Contracts.FindAsync(contract.Id))!.Status);
        Assert.Single(fixture.Notifier.Changes);
    }

    [Fact]
    public async Task Successful_resident_finalization_publishes_completed_but_failed_otp_does_not()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddContractAsync(RemoteSigningMode.PRE_SIGNED_INVESTOR,
            RemoteContractStatus.PENDING_RESIDENT_SIGNATURE, DateTimeOffset.UtcNow.AddDays(1), fixture.Resident);
        var challenge = await fixture.Service.BeginSigningAsync(fixture.Resident, false, contract.Id,
            new BeginSignature(SignatureMethod.TYPE, null, "Resident", "Arial"), default);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.VerifyAndFinalizeAsync(fixture.Resident, false, contract.Id,
            new VerifySignature(challenge.SessionId, "000000"), default));
        Assert.Empty(fixture.Notifier.Changes);

        await fixture.Service.VerifyAndFinalizeAsync(fixture.Resident, false, contract.Id,
            new VerifySignature(challenge.SessionId, "123456"), default);
        var change = Assert.Single(fixture.Notifier.Changes);
        Assert.Equal((contract.Id, "PENDING_RESIDENT_SIGNATURE", "COMPLETED"), (change.ContractId, change.PreviousStatus, change.NewStatus));
    }

    [Fact]
    public async Task Pending_contract_future_or_null_deadline_does_not_expire()
    {
        await using var fixture = new Fixture();
        var future = await fixture.AddContractAsync(RemoteSigningMode.PRE_SIGNED_INVESTOR,
            RemoteContractStatus.PENDING_RESIDENT_SIGNATURE, DateTimeOffset.UtcNow.AddDays(2), fixture.Resident);
        var noDeadline = await fixture.AddContractAsync(RemoteSigningMode.PRE_SIGNED_INVESTOR,
            RemoteContractStatus.PENDING_RESIDENT_SIGNATURE, null, fixture.Resident);

        var list = await fixture.Service.ListAsync(fixture.Owner, true, default);
        Assert.Equal("PENDING_RESIDENT_SIGNATURE", Assert.Single(list, x => x.Id == future.Id).Status);
        Assert.Equal("PENDING_RESIDENT_SIGNATURE", Assert.Single(list, x => x.Id == noDeadline.Id).Status);
        Assert.Equal(0, await fixture.Db.Events.CountAsync(x => x.Type == "CONTRACT_EXPIRED"));
    }

    [Fact]
    public async Task Terminal_and_draft_contracts_do_not_expire()
    {
        await using var fixture = new Fixture();
        var completed = await fixture.AddContractAsync(RemoteSigningMode.PRE_SIGNED_INVESTOR,
            RemoteContractStatus.COMPLETED, DateTimeOffset.UtcNow.AddHours(-1), fixture.Resident);
        var cancelled = await fixture.AddContractAsync(RemoteSigningMode.PRE_SIGNED_INVESTOR,
            RemoteContractStatus.CANCELLED, DateTimeOffset.UtcNow.AddHours(-1), fixture.Resident);
        var draft = await fixture.AddContractAsync(RemoteSigningMode.INVESTOR_AND_RESIDENT,
            RemoteContractStatus.DRAFT, DateTimeOffset.UtcNow.AddHours(-1), null);
        var investorSigned = await fixture.AddContractAsync(RemoteSigningMode.INVESTOR_AND_RESIDENT,
            RemoteContractStatus.INVESTOR_SIGNED_DRAFT, DateTimeOffset.UtcNow.AddHours(-1), fixture.Resident);

        var list = await fixture.Service.ListAsync(fixture.Owner, true, default);

        Assert.Equal(RemoteContractStatus.COMPLETED, (await fixture.Db.Contracts.FindAsync(completed.Id))!.Status);
        Assert.Equal(RemoteContractStatus.CANCELLED, (await fixture.Db.Contracts.FindAsync(cancelled.Id))!.Status);
        Assert.Contains(list, x => x.Id == cancelled.Id && x.Status == "CANCELLED");
        Assert.Equal(RemoteContractStatus.DRAFT, (await fixture.Db.Contracts.FindAsync(draft.Id))!.Status);
        Assert.Equal(RemoteContractStatus.INVESTOR_SIGNED_DRAFT, (await fixture.Db.Contracts.FindAsync(investorSigned.Id))!.Status);
        Assert.Equal(0, await fixture.Db.Events.CountAsync(x => x.Type == "CONTRACT_EXPIRED"));
    }

    [Fact]
    public async Task Legacy_cancelled_contract_is_read_only()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddContractAsync(RemoteSigningMode.PRE_SIGNED_INVESTOR,
            RemoteContractStatus.CANCELLED, DateTimeOffset.UtcNow.AddDays(1), fixture.Resident);

        var detail = await fixture.Service.GetAsync(fixture.Owner, true, contract.Id, default);
        Assert.Equal("CANCELLED", detail.Status);

        var sign = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.BeginSigningAsync(fixture.Owner, true, contract.Id,
                new BeginSignature(SignatureMethod.TYPE, null, "Investor", "Arial"), default));
        Assert.Equal("signing_not_available", sign.Code);

        var edit = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.UpdateMetadataAsync(fixture.Owner, contract.Id,
                new UpdateRemoteContractMetadata("Tên mới", null, contract.Version), default));
        Assert.Equal("metadata_edit_not_allowed", edit.Code);

        var reassign = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.ReassignResidentAsync(fixture.Owner, contract.Id,
                new ReassignRemoteResident(Guid.NewGuid(), contract.Version), default));
        Assert.Equal("contract_not_rejected", reassign.Code);
    }

    [Fact]
    public async Task Resident_can_view_expired_contract_but_cannot_sign()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddContractAsync(RemoteSigningMode.PRE_SIGNED_INVESTOR,
            RemoteContractStatus.PENDING_RESIDENT_SIGNATURE, DateTimeOffset.UtcNow.AddHours(-1), fixture.Resident);

        var residentList = await fixture.Service.ListAsync(fixture.Resident, false, default);
        Assert.Contains(residentList, x => x.Id == contract.Id && x.Status == "EXPIRED");
        var detail = await fixture.Service.GetAsync(fixture.Resident, false, contract.Id, default);
        Assert.Equal("EXPIRED", detail.Status);

        var error = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.BeginSigningAsync(fixture.Resident, false, contract.Id,
                new BeginSignature(SignatureMethod.TYPE, null, "Resident", "Arial"), default));
        Assert.Equal("contract_expired", error.Code);
    }

    [Fact]
    public async Task Admin_cannot_begin_or_verify_signing_after_expiry()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddContractAsync(RemoteSigningMode.INVESTOR_AND_RESIDENT,
            RemoteContractStatus.PENDING_INVESTOR_SIGNATURE, DateTimeOffset.UtcNow.AddHours(-1), null);

        var begin = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.BeginSigningAsync(fixture.Owner, true, contract.Id,
                new BeginSignature(SignatureMethod.TYPE, null, "Investor", "Arial"), default));
        Assert.Equal("contract_expired", begin.Code);
    }

    [Fact]
    public async Task Renew_deadline_restores_mode_b_to_pending_resident()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddContractAsync(RemoteSigningMode.PRE_SIGNED_INVESTOR,
            RemoteContractStatus.PENDING_RESIDENT_SIGNATURE, DateTimeOffset.UtcNow.AddHours(-1), fixture.Resident);
        await fixture.Service.ListAsync(fixture.Owner, true, default);
        var expired = await fixture.Db.Contracts.FindAsync(contract.Id);
        Assert.Equal(RemoteContractStatus.EXPIRED, expired!.Status);

        var summary = await fixture.Service.RenewDeadlineAsync(fixture.Owner, contract.Id,
            new RenewRemoteDeadline(DateTimeOffset.UtcNow.AddDays(3), expired.Version), default);

        Assert.Equal("PENDING_RESIDENT_SIGNATURE", summary.Status);
        var stored = await fixture.Db.Contracts.FindAsync(contract.Id);
        Assert.Equal(RemoteContractStatus.PENDING_RESIDENT_SIGNATURE, stored!.Status);
        Assert.True(stored.Deadline > DateTimeOffset.UtcNow);
        Assert.Equal(contract.ContractName, stored.ContractName);
        Assert.Equal(fixture.Resident, stored.ResidentId);
        Assert.Equal(2, await fixture.Db.SignatureFields.CountAsync(x => x.ContractId == contract.Id));
        Assert.Contains(await fixture.Db.Events.Where(x => x.ContractId == contract.Id).ToListAsync(),
            x => x.Type == "CONTRACT_DEADLINE_RENEWED");
    }

    [Fact]
    public async Task Renew_deadline_restores_mode_a_with_investor_signature_to_pending_resident()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddModeAContractAsync();
        await fixture.SignAsInvestorAsync(contract.Id);
        await fixture.Service.AssignAsync(fixture.Owner, contract.Id, new(fixture.Resident), default);
        await fixture.Service.SendAsync(fixture.Owner, contract.Id, new(), default);
        var stored = await fixture.Db.Contracts.FindAsync(contract.Id);
        stored!.Deadline = DateTimeOffset.UtcNow.AddHours(-1);
        await fixture.Db.SaveChangesAsync();

        await fixture.Service.ListAsync(fixture.Owner, true, default);
        stored = await fixture.Db.Contracts.FindAsync(contract.Id);
        Assert.Equal(RemoteContractStatus.EXPIRED, stored!.Status);

        var summary = await fixture.Service.RenewDeadlineAsync(fixture.Owner, contract.Id,
            new RenewRemoteDeadline(DateTimeOffset.UtcNow.AddDays(3), stored.Version), default);

        Assert.Equal("PENDING_RESIDENT_SIGNATURE", summary.Status);
        var after = await fixture.Db.Contracts.FindAsync(contract.Id);
        Assert.Equal(RemoteContractStatus.PENDING_RESIDENT_SIGNATURE, after!.Status);
        Assert.False(string.IsNullOrWhiteSpace(after.WorkingSha256));
    }

    [Fact]
    public async Task Renew_deadline_restores_legacy_pending_investor()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddContractAsync(RemoteSigningMode.INVESTOR_AND_RESIDENT,
            RemoteContractStatus.PENDING_INVESTOR_SIGNATURE, DateTimeOffset.UtcNow.AddHours(-1), null);
        await fixture.Service.ListAsync(fixture.Owner, true, default);
        var expired = await fixture.Db.Contracts.FindAsync(contract.Id);

        var summary = await fixture.Service.RenewDeadlineAsync(fixture.Owner, contract.Id,
            new RenewRemoteDeadline(DateTimeOffset.UtcNow.AddDays(3), expired!.Version), default);

        Assert.Equal("PENDING_INVESTOR_SIGNATURE", summary.Status);
    }

    [Fact]
    public async Task Renew_deadline_rejects_invalid_input_and_non_admin()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddContractAsync(RemoteSigningMode.PRE_SIGNED_INVESTOR,
            RemoteContractStatus.PENDING_RESIDENT_SIGNATURE, DateTimeOffset.UtcNow.AddHours(-1), fixture.Resident);
        await fixture.Service.ListAsync(fixture.Owner, true, default);
        var expired = await fixture.Db.Contracts.FindAsync(contract.Id);

        var past = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.RenewDeadlineAsync(fixture.Owner, contract.Id,
                new RenewRemoteDeadline(DateTimeOffset.UtcNow.AddHours(-1), expired!.Version), default));
        Assert.Equal("deadline_must_be_future", past.Code);

        var notExpired = await fixture.AddContractAsync(RemoteSigningMode.PRE_SIGNED_INVESTOR,
            RemoteContractStatus.PENDING_RESIDENT_SIGNATURE, DateTimeOffset.UtcNow.AddDays(2), fixture.Resident);
        var wrong = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.RenewDeadlineAsync(fixture.Owner, notExpired.Id,
                new RenewRemoteDeadline(DateTimeOffset.UtcNow.AddDays(3), notExpired.Version), default));
        Assert.Equal("contract_not_expired", wrong.Code);

        var residentDenied = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.RenewDeadlineAsync(fixture.Resident, contract.Id,
                new RenewRemoteDeadline(DateTimeOffset.UtcNow.AddDays(3), expired.Version), default));
        Assert.Equal(404, residentDenied.Status);
    }

    [Fact]
    public async Task Old_signing_session_cannot_be_used_after_renewal()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddContractAsync(RemoteSigningMode.PRE_SIGNED_INVESTOR,
            RemoteContractStatus.PENDING_RESIDENT_SIGNATURE, DateTimeOffset.UtcNow.AddHours(1), fixture.Resident);
        var challenge = await fixture.Service.BeginSigningAsync(fixture.Resident, false, contract.Id,
            new BeginSignature(SignatureMethod.TYPE, null, "Resident", "Arial"), default);

        var stored = await fixture.Db.Contracts.FindAsync(contract.Id);
        stored!.Deadline = DateTimeOffset.UtcNow.AddHours(-1);
        await fixture.Db.SaveChangesAsync();
        await fixture.Service.ListAsync(fixture.Owner, true, default);
        stored = await fixture.Db.Contracts.FindAsync(contract.Id);

        await fixture.Service.RenewDeadlineAsync(fixture.Owner, contract.Id,
            new RenewRemoteDeadline(DateTimeOffset.UtcNow.AddDays(3), stored!.Version), default);

        var error = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.VerifyAndFinalizeAsync(fixture.Resident, false, contract.Id,
                new VerifySignature(challenge.SessionId, "123456"), default));
        Assert.Contains(error.Code, new[] { "signing_session_expired", "signing_context_changed", "contract_expired" });
        Assert.Equal(RemoteContractStatus.PENDING_RESIDENT_SIGNATURE,
            (await fixture.Db.Contracts.FindAsync(contract.Id))!.Status);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Guid Owner { get; } = Guid.NewGuid();
        public Guid Resident { get; } = Guid.NewGuid();
        public RemoteOnlineSignDbContext Db { get; }
        public MemoryStorage Storage { get; } = new();
        public CountingOtp Otp { get; } = new();
        public RecordingNotifier Notifier { get; } = new();
        public RemoteContractService Service { get; }

        public Fixture()
        {
            Db = new(new DbContextOptionsBuilder<RemoteOnlineSignDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
            Service = new(Db, Storage, Otp, new PdfSignatureStamper(), NullLogger<RemoteContractService>.Instance,
                new RemoteTestAccountAccess(), new RemoteTestAccountDirectory(), new RemoteTestResidentScope(),
                new RemoteContractCodeGenerator(Db), Notifier);
        }

        public Task<RemoteContract> AddContractAsync(RemoteSigningMode mode, RemoteContractStatus status,
            DateTimeOffset? deadline, Guid? residentId) => AddContractAsync(mode, status, deadline, residentId, true);

        public async Task<RemoteContract> AddContractAsync(RemoteSigningMode mode, RemoteContractStatus status,
            DateTimeOffset? deadline, Guid? residentId, bool withDocument)
        {
            var contract = new RemoteContract
            {
                CreatorId = Owner, SigningMode = mode, Status = status,
                Deadline = deadline, ResidentId = residentId, ContractName = "Hợp đồng kiểm thử"
            };
            Db.Contracts.Add(contract);
            await Db.SaveChangesAsync();
            if (withDocument) await UploadPdfAsync(contract);
            await Db.SignatureFields.AddAsync(new ContractSignatureField { ContractId = contract.Id, Role = SignatureRole.INVESTOR, Page = 1, X = 10, Y = 10, Width = 100, Height = 30 });
            await Db.SignatureFields.AddAsync(new ContractSignatureField { ContractId = contract.Id, Role = SignatureRole.RESIDENT, Page = 1, X = 10, Y = 50, Width = 100, Height = 30 });
            await Db.SaveChangesAsync();
            return contract;
        }

        public async Task<RemoteContract> AddModeAContractAsync()
        {
            var contract = await AddContractAsync(RemoteSigningMode.INVESTOR_AND_RESIDENT,
                RemoteContractStatus.DRAFT, null, null);
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

    private sealed class RecordingNotifier : IRemoteContractStatusNotifier
    {
        public List<RemoteContractStatusChanged> Changes { get; } = [];
        public bool Throw { get; set; }
        public Task NotifyAsync(RemoteContractStatusChanged change, CancellationToken ct)
        {
            Changes.Add(change);
            if (Throw) throw new InvalidOperationException("delivery unavailable");
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
