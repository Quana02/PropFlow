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

// Coverage for Delete Draft (UI-07.1): only ADMIN-owned DRAFT contracts may be
// deleted; every other status and any finalized-signature evidence must be
// rejected; dependent rows and Cloudinary assets are cleaned up post-commit.
public sealed class RemoteOnlineSignDeleteDraftTests
{
    [Fact]
    public async Task Admin_deletes_own_draft_and_dependents_are_removed()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddDraftAsync();
        await fixture.Db.SignatureFields.AddAsync(new ContractSignatureField { ContractId = contract.Id, Role = SignatureRole.INVESTOR, Page = 1, X = 1, Y = 1, Width = 50, Height = 20 });
        await fixture.Db.SigningSessions.AddAsync(new ContractSigningSession { ContractId = contract.Id, Role = SignatureRole.INVESTOR, SignerId = fixture.Owner, Method = SignatureMethod.DRAW, SignatureSha256 = "abc", BindingHash = "def" });
        await fixture.Db.SaveChangesAsync();
        var docKey = (await fixture.Db.Documents.SingleAsync(x => x.ContractId == contract.Id)).StorageKey;

        await fixture.Service.DeleteDraftAsync(fixture.Owner, contract.Id, default);

        Assert.Null(await fixture.Db.Contracts.FindAsync(contract.Id));
        Assert.Empty(await fixture.Db.Documents.Where(x => x.ContractId == contract.Id).ToListAsync());
        Assert.Empty(await fixture.Db.SignatureFields.Where(x => x.ContractId == contract.Id).ToListAsync());
        Assert.Empty(await fixture.Db.SigningSessions.Where(x => x.ContractId == contract.Id).ToListAsync());
        Assert.Contains(docKey, fixture.Storage.DeletedKeys);
        // Audit history is retained for traceability, including the delete marker.
        Assert.Contains(await fixture.Db.Events.Where(x => x.ContractId == contract.Id).ToListAsync(),
            x => x.Type == "CONTRACT_DRAFT_DELETED");
    }

    [Fact]
    public async Task Admin_cannot_delete_other_admins_draft()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddDraftAsync(owner: Guid.NewGuid());

        var error = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.DeleteDraftAsync(fixture.Owner, contract.Id, default));

        Assert.Equal(404, error.Status);
        Assert.NotNull(await fixture.Db.Contracts.FindAsync(contract.Id));
    }

    [Fact]
    public async Task Missing_contract_returns_404()
    {
        await using var fixture = new Fixture();
        var error = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.DeleteDraftAsync(fixture.Owner, Guid.NewGuid(), default));
        Assert.Equal(404, error.Status);
    }

    [Theory]
    [InlineData(RemoteContractStatus.PENDING_INVESTOR_SIGNATURE)]
    [InlineData(RemoteContractStatus.INVESTOR_SIGNED_DRAFT)]
    [InlineData(RemoteContractStatus.PENDING_RESIDENT_SIGNATURE)]
    [InlineData(RemoteContractStatus.COMPLETED)]
    [InlineData(RemoteContractStatus.REJECTED)]
    [InlineData(RemoteContractStatus.EXPIRED)]
    [InlineData(RemoteContractStatus.CANCELLED)]
    public async Task Non_draft_statuses_are_rejected(RemoteContractStatus status)
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddDraftAsync();
        var stored = await fixture.Db.Contracts.FindAsync(contract.Id);
        stored!.Status = status;
        await fixture.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.DeleteDraftAsync(fixture.Owner, contract.Id, default));

        Assert.Equal(409, error.Status);
        Assert.Equal("contract_not_draft", error.Code);
        Assert.NotNull(await fixture.Db.Contracts.FindAsync(contract.Id));
        Assert.Empty(fixture.Storage.DeletedKeys);
    }

    [Fact]
    public async Task Draft_with_finalized_session_is_rejected()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddDraftAsync();
        await fixture.Db.SigningSessions.AddAsync(new ContractSigningSession
        {
            ContractId = contract.Id, Role = SignatureRole.INVESTOR, SignerId = fixture.Owner,
            Method = SignatureMethod.DRAW, SignatureSha256 = "abc", BindingHash = "def",
            FinalizedAt = DateTimeOffset.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<RemoteContractException>(
            () => fixture.Service.DeleteDraftAsync(fixture.Owner, contract.Id, default));

        Assert.Equal(409, error.Status);
        Assert.NotNull(await fixture.Db.Contracts.FindAsync(contract.Id));
    }

    [Fact]
    public async Task Delete_does_not_affect_other_contracts_or_code_counters()
    {
        await using var fixture = new Fixture();
        var draft = await fixture.AddDraftAsync();
        var other = await fixture.AddDraftAsync();
        var otherDocKey = (await fixture.Db.Documents.SingleAsync(x => x.ContractId == other.Id)).StorageKey;
        var counterBefore = await fixture.Db.ContractCodeCounters.ToListAsync();

        await fixture.Service.DeleteDraftAsync(fixture.Owner, draft.Id, default);

        Assert.NotNull(await fixture.Db.Contracts.FindAsync(other.Id));
        Assert.Contains(otherDocKey, fixture.Storage.Data.Keys);
        Assert.DoesNotContain(otherDocKey, fixture.Storage.DeletedKeys);
        var counterAfter = await fixture.Db.ContractCodeCounters.ToListAsync();
        Assert.Equal(counterBefore.Count, counterAfter.Count);
        Assert.Equal(counterBefore.Select(x => x.LastNumber), counterAfter.Select(x => x.LastNumber));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Guid Owner { get; } = Guid.NewGuid();
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

        public async Task<RemoteContract> AddDraftAsync(Guid? owner = null)
        {
            var contract = new RemoteContract
            {
                CreatorId = owner ?? Owner, SigningMode = RemoteSigningMode.PRE_SIGNED_INVESTOR,
                Status = RemoteContractStatus.DRAFT, ContractName = "Hợp đồng kiểm thử",
                ContractCode = "ROS-" + Guid.NewGuid().ToString("N")[..8]
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
            await Db.SaveChangesAsync();
            return contract;
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class CountingOtp : IPrivilegedActionOtp
    {
        public Task<PrivilegedActionChallengeResult> RequestAsync(Guid userId, string purpose, string payloadHash, CancellationToken ct) =>
            Task.FromResult(new PrivilegedActionChallengeResult(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow.AddSeconds(30), "***"));
        public Task VerifyAsync(Guid userId, Guid challengeId, string purpose, string payloadHash, string code, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class MemoryStorage : IRemotePdfStorage
    {
        public Dictionary<string, byte[]> Data { get; } = new();
        public List<string> DeletedKeys { get; } = new();
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
        public Task DeleteIfUnreferencedAsync(string key, CancellationToken ct) { Data.Remove(key); DeletedKeys.Add(key); return Task.CompletedTask; }
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
