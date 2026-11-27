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

public sealed class RemoteOnlineSignAuthorizationTests
{
    [Fact]
    public async Task Create_normalizes_a_local_offset_deadline_to_utc_before_persisting()
    {
        await using var fixture = new Fixture();
        var localDeadline = new DateTimeOffset(2026, 12, 1, 9, 30, 0, TimeSpan.FromHours(7));

        var summary = await fixture.Service.CreateAsync(
            fixture.Owner,
            new CreateRemoteContract(localDeadline, "Hợp đồng kiểm thử"),
            default);
        var stored = await fixture.Db.Contracts.SingleAsync(x => x.Id == summary.Id);

        Assert.Equal(localDeadline.ToUniversalTime(), stored.Deadline);
        Assert.Equal(TimeSpan.Zero, stored.Deadline!.Value.Offset);
        Assert.Equal(localDeadline.ToUniversalTime(), summary.Deadline);
    }

    [Fact]
    public async Task Owner_admin_can_read_own_contract_but_another_admin_gets_not_found()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddContractAsync();

        var own = await fixture.Service.GetAsync(fixture.Owner, true, contract.Id, default);
        var other = await Assert.ThrowsAsync<RemoteContractException>(() => fixture.Service.GetAsync(Guid.NewGuid(), true, contract.Id, default));

        Assert.Equal(contract.Id, own.Id);
        Assert.Equal(404, other.Status);
        Assert.Equal("contract_not_found", other.Code);
    }

    [Fact]
    public async Task Eligible_assigned_resident_can_read_contract_document_fields_and_history()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddContractAsync();
        await fixture.AddOriginalAsync(contract);
        fixture.Db.SignatureFields.Add(new ContractSignatureField { ContractId = contract.Id, Role = SignatureRole.RESIDENT, Page = 1, X = 10, Y = 10, Width = 100, Height = 30 });
        fixture.Db.Events.Add(new ContractEvent { ContractId = contract.Id, ActorId = fixture.Owner, Type = "TEST" });
        contract.Status = RemoteContractStatus.PENDING_RESIDENT_SIGNATURE;
        await fixture.Db.SaveChangesAsync();

        Assert.Equal(contract.Id, (await fixture.Service.GetAsync(fixture.Resident, false, contract.Id, default)).Id);
        Assert.Single(await fixture.Service.GetFieldsAsync(fixture.Resident, false, contract.Id, default));
        Assert.Single(await fixture.Service.HistoryAsync(fixture.Resident, false, contract.Id, default));
        var document = await fixture.Service.DownloadOriginalAsync(fixture.Resident, false, contract.Id, default);
        await using (document.Content) Assert.True(document.Content.Length > 0);
    }

    [Fact]
    public async Task Different_resident_cannot_read_document_fields_or_history()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddContractAsync();
        await fixture.AddOriginalAsync(contract);

        var other = Guid.NewGuid();
        await Assert404(() => fixture.Service.GetAsync(other, false, contract.Id, default));
        await Assert404(() => fixture.Service.GetFieldsAsync(other, false, contract.Id, default));
        await Assert404(() => fixture.Service.HistoryAsync(other, false, contract.Id, default));
        await Assert404(() => fixture.Service.DownloadOriginalAsync(other, false, contract.Id, default));
    }

    [Fact]
    public async Task User_without_resident_role_cannot_enter_resident_access_path()
    {
        var user = Guid.NewGuid();
        await using var fixture = new Fixture(residentRole: id => id == fixtureResidentPlaceholder);
        // The assigned user is eligible; the caller below is not. The temporary fixture id is not used as an actor.
        var contract = await fixture.AddContractAsync();
        var ex = await Assert.ThrowsAsync<RemoteContractException>(() => fixture.Service.GetAsync(user, false, contract.Id, default));
        Assert.Equal(403, ex.Status);
        Assert.Equal("resident_access_unavailable", ex.Code);
    }

    [Fact]
    public async Task Assign_rejects_nonexistent_nonresident_and_inactive_accounts()
    {
        var target = Guid.NewGuid();
        await using (var missing = new Fixture(accountStatus: _ => null))
        {
            var contract = await missing.AddContractAsync();
            await AssertEligibilityFailure(() => missing.Service.AssignAsync(missing.Owner, contract.Id, new(target), default));
        }
        await using (var wrongRole = new Fixture(residentRole: _ => false))
        {
            var contract = await wrongRole.AddContractAsync();
            await AssertEligibilityFailure(() => wrongRole.Service.AssignAsync(wrongRole.Owner, contract.Id, new(target), default));
        }
        await using (var inactive = new Fixture(accountStatus: _ => "LOCKED"))
        {
            var contract = await inactive.AddContractAsync();
            await AssertEligibilityFailure(() => inactive.Service.AssignAsync(inactive.Owner, contract.Id, new(target), default));
        }
    }

    [Fact]
    public async Task Assign_rejects_account_without_active_resident_profile()
    {
        await using var fixture = new Fixture(activeResident: _ => false);
        var contract = await fixture.AddContractAsync();
        var ex = await Assert.ThrowsAsync<RemoteContractException>(() => fixture.Service.AssignAsync(fixture.Owner, contract.Id, new(Guid.NewGuid()), default));
        Assert.Equal("resident_not_eligible", ex.Code);
    }

    private static readonly Guid fixtureResidentPlaceholder = Guid.NewGuid();
    private static async Task AssertEligibilityFailure(Func<Task<RemoteContractSummary>> action)
    {
        var ex = await Assert.ThrowsAsync<RemoteContractException>(action);
        Assert.Equal(400, ex.Status);
        Assert.Equal("resident_not_eligible", ex.Code);
    }
    private static async Task Assert404<T>(Func<Task<T>> action)
    {
        var ex = await Assert.ThrowsAsync<RemoteContractException>(action);
        Assert.Equal(404, ex.Status);
        Assert.Equal("contract_not_found", ex.Code);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Guid Owner { get; } = Guid.NewGuid();
        public Guid Resident { get; } = Guid.NewGuid();
        public RemoteOnlineSignDbContext Db { get; }
        public MemoryStorage Storage { get; } = new();
        public RemoteContractService Service { get; }

        public Fixture(Func<Guid, bool>? residentRole = null, Func<Guid, string?>? accountStatus = null, Func<Guid, bool>? activeResident = null)
        {
            Db = new(new DbContextOptionsBuilder<RemoteOnlineSignDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
            Service = new(Db, Storage, new Otp(), new PdfSignatureStamper(), NullLogger<RemoteContractService>.Instance,
                new RemoteTestAccountAccess(residentRole), new RemoteTestAccountDirectory(accountStatus), new RemoteTestResidentScope(activeResident), new RemoteTestContractCodes());
        }
        public async Task<RemoteContract> AddContractAsync()
        {
            var contract = new RemoteContract { CreatorId = Owner, ResidentId = Resident, SigningMode = RemoteSigningMode.PRE_SIGNED_INVESTOR };
            Db.Contracts.Add(contract);
            await Db.SaveChangesAsync();
            return contract;
        }
        public async Task AddOriginalAsync(RemoteContract contract)
        {
            var bytes = Pdf();
            var key = "test/" + Guid.NewGuid().ToString("N");
            Storage.Data[key] = bytes;
            Db.Documents.Add(new ContractDocument { ContractId = contract.Id, StorageKey = key, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), Size = bytes.Length, PageCount = 1 });
            await Db.SaveChangesAsync();
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class Otp : IPrivilegedActionOtp
    {
        public Task<PrivilegedActionChallengeResult> RequestAsync(Guid userId, string purpose, string payloadHash, CancellationToken ct) => throw new NotSupportedException();
        public Task VerifyAsync(Guid userId, Guid challengeId, string purpose, string payloadHash, string code, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class MemoryStorage : IRemotePdfStorage
    {
        public Dictionary<string, byte[]> Data { get; } = new();
        public Task<StoredPdf> PutOriginalAsync(Guid contractId, Stream content, long length, CancellationToken ct) => throw new NotSupportedException();
        public Task<StoredPdf> PutDerivedAsync(Guid contractId, string purpose, Stream content, long length, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream(Data[key], writable: false));
        public Task<bool> ExistsWithHashAsync(string key, string hash, CancellationToken ct) => Task.FromResult(true);
        public Task DeleteIfUnreferencedAsync(string key, CancellationToken ct) => Task.CompletedTask;
    }
    private static byte[] Pdf()
    {
        using var document = new PdfDocument(); document.AddPage(); using var stream = new MemoryStream(); document.Save(stream, false); return stream.ToArray();
    }
}
