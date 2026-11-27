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

public sealed class RemoteOnlineSignIntegrityTests
{
    [Fact]
    public async Task Valid_source_snapshot_is_hashed_and_can_finalize()
    {
        await using var fixture = new Fixture(RemoteSigningMode.PRE_SIGNED_INVESTOR);
        var session = await fixture.PrepareResidentSessionAsync();

        await fixture.Service.VerifyAndFinalizeAsync(fixture.Resident, false, fixture.Contract.Id, new(session, "123456"), default);

        Assert.Equal(RemoteContractStatus.COMPLETED, (await fixture.Db.Contracts.AsNoTracking().SingleAsync(x => x.Id == fixture.Contract.Id)).Status);
        Assert.Equal(1, fixture.Stamper.Calls);
        Assert.Single(fixture.Db.Documents.Where(x => x.IsFinal));
    }

    [Fact]
    public async Task Changed_source_after_otp_request_fails_closed_before_otp_consumption_or_stamping()
    {
        await using var fixture = new Fixture(RemoteSigningMode.PRE_SIGNED_INVESTOR);
        var session = await fixture.PrepareResidentSessionAsync();
        fixture.Storage.Replace(fixture.SourceKey, Pdf("changed"));

        var error = await Assert.ThrowsAsync<RemoteContractException>(() => fixture.Service.VerifyAndFinalizeAsync(fixture.Resident, false, fixture.Contract.Id, new(session, "123456"), default));

        Assert.Equal("document_integrity_failed", error.Code);
        Assert.Equal(0, fixture.Otp.VerifyCalls);
        Assert.Equal(0, fixture.Stamper.Calls);
        Assert.Empty(fixture.Db.Documents.Where(x => x.IsFinal));
        Assert.Equal(RemoteContractStatus.PENDING_RESIDENT_SIGNATURE, fixture.Contract.Status);
    }

    [Fact]
    public async Task Changed_source_before_otp_request_cannot_create_signing_binding()
    {
        await using var fixture = new Fixture(RemoteSigningMode.PRE_SIGNED_INVESTOR);
        await fixture.PrepareForSigningAsync();
        fixture.Storage.Replace(fixture.SourceKey, Pdf("changed"));

        var error = await Assert.ThrowsAsync<RemoteContractException>(() => fixture.Service.BeginSigningAsync(fixture.Resident, false, fixture.Contract.Id, new(SignatureMethod.TYPE, null, "Resident", "Arial"), default));

        Assert.Equal("document_integrity_failed", error.Code);
        Assert.Equal(0, fixture.Otp.RequestCalls);
        Assert.Empty(fixture.Db.SigningSessions);
    }

    [Fact]
    public async Task Changed_revision_after_otp_request_rejects_old_signing_context()
    {
        await using var fixture = new Fixture(RemoteSigningMode.PRE_SIGNED_INVESTOR);
        var session = await fixture.PrepareResidentSessionAsync();
        var persisted = await fixture.Db.Contracts.SingleAsync(x => x.Id == fixture.Contract.Id);
        persisted.Revision++;
        await fixture.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<RemoteContractException>(() => fixture.Service.VerifyAndFinalizeAsync(fixture.Resident, false, fixture.Contract.Id, new(session, "123456"), default));

        Assert.Equal("signing_context_changed", error.Code);
        Assert.Equal(0, fixture.Otp.VerifyCalls);
        Assert.Equal(0, fixture.Stamper.Calls);
    }

    [Fact]
    public async Task Missing_source_fails_closed_without_artifact_or_completion()
    {
        await using var fixture = new Fixture(RemoteSigningMode.PRE_SIGNED_INVESTOR);
        var session = await fixture.PrepareResidentSessionAsync();
        fixture.Storage.Delete(fixture.SourceKey);

        var error = await Assert.ThrowsAsync<RemoteContractException>(() => fixture.Service.VerifyAndFinalizeAsync(fixture.Resident, false, fixture.Contract.Id, new(session, "123456"), default));

        Assert.Equal("document_integrity_failed", error.Code);
        Assert.Equal(0, fixture.Stamper.Calls);
        Assert.Empty(fixture.Db.Documents.Where(x => x.IsFinal));
    }

    [Fact]
    public async Task Expired_signing_session_cannot_retry_or_consume_otp()
    {
        await using var fixture = new Fixture(RemoteSigningMode.PRE_SIGNED_INVESTOR);
        var session = await fixture.PrepareResidentSessionAsync();
        (await fixture.Db.SigningSessions.SingleAsync(x => x.Id == session)).ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await fixture.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<RemoteContractException>(() => fixture.Service.VerifyAndFinalizeAsync(fixture.Resident, false, fixture.Contract.Id, new(session, "123456"), default));

        Assert.Equal("signing_session_expired", error.Code);
        Assert.Equal(0, fixture.Otp.VerifyCalls);
        Assert.Equal(0, fixture.Stamper.Calls);
    }

    [Fact]
    public async Task Resident_losing_active_profile_after_otp_request_cannot_finalize()
    {
        var active = true;
        await using var fixture = new Fixture(RemoteSigningMode.PRE_SIGNED_INVESTOR, activeResident: _ => active);
        var session = await fixture.PrepareResidentSessionAsync();
        active = false;

        var error = await Assert.ThrowsAsync<RemoteContractException>(() => fixture.Service.VerifyAndFinalizeAsync(fixture.Resident, false, fixture.Contract.Id, new(session, "123456"), default));

        Assert.Equal("resident_access_unavailable", error.Code);
        Assert.Equal(0, fixture.Otp.VerifyCalls);
        Assert.Equal(0, fixture.Stamper.Calls);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Guid Owner { get; } = Guid.NewGuid(); public Guid Resident { get; } = Guid.NewGuid();
        public RemoteOnlineSignDbContext Db { get; }
        public Store Storage { get; } = new(); public Otp Otp { get; } = new(); public CountingStamper Stamper { get; } = new();
        public RemoteContractService Service { get; }
        public RemoteContract Contract { get; } = new(); public string SourceKey { get; private set; } = "";
        private readonly RemoteSigningMode mode;
        public Fixture(RemoteSigningMode mode, Func<Guid, bool>? activeResident = null)
        {
            this.mode = mode;
            Db = new(new DbContextOptionsBuilder<RemoteOnlineSignDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
            Service = new(Db, Storage, Otp, Stamper, NullLogger<RemoteContractService>.Instance, new RemoteTestAccountAccess(), new RemoteTestAccountDirectory(), new RemoteTestResidentScope(activeResident),new RemoteContractCodeGenerator(Db));
            Contract.CreatorId = Owner; Contract.ResidentId = Resident; Contract.SigningMode = mode;
        }
        public async Task PrepareForSigningAsync()
        {
            Db.Contracts.Add(Contract); await Db.SaveChangesAsync();
            var original = Pdf("original"); SourceKey = "source/" + Guid.NewGuid().ToString("N"); Storage.Replace(SourceKey, original);
            Db.Documents.Add(new ContractDocument { ContractId = Contract.Id, StorageKey = SourceKey, Sha256 = Hash(original), Size = original.Length, PageCount = 1 });
            Db.SignatureFields.Add(new ContractSignatureField { ContractId = Contract.Id, Role = SignatureRole.RESIDENT, Page = 1, X = 10, Y = 10, Width = 100, Height = 30 });
            await Db.SaveChangesAsync();
            await Service.SendAsync(Owner, Contract.Id, new(), default);
        }
        public async Task<Guid> PrepareResidentSessionAsync()
        {
            await PrepareForSigningAsync();
            return (await Service.BeginSigningAsync(Resident, false, Contract.Id, new(SignatureMethod.TYPE, null, "Resident", "Arial"), default)).SessionId;
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class Otp : IPrivilegedActionOtp
    {
        public int RequestCalls; public int VerifyCalls;
        public Task<PrivilegedActionChallengeResult> RequestAsync(Guid userId, string purpose, string payloadHash, CancellationToken ct) { RequestCalls++; return Task.FromResult(new PrivilegedActionChallengeResult(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow, "***")); }
        public Task VerifyAsync(Guid userId, Guid challengeId, string purpose, string payloadHash, string code, CancellationToken ct) { VerifyCalls++; return Task.CompletedTask; }
    }
    private sealed class CountingStamper : IPdfSignatureStamper
    {
        public int Calls;
        public async Task<byte[]> StampAsync(Stream source, ContractSignatureField field, ContractSigningSession session, CancellationToken ct) { Calls++; using var result = new MemoryStream(); await source.CopyToAsync(result, ct); return result.ToArray(); }
    }
    private sealed class Store : IRemotePdfStorage
    {
        private readonly Dictionary<string, byte[]> data = new();
        public void Replace(string key, byte[] bytes) => data[key] = bytes;
        public void Delete(string key) => data.Remove(key);
        public Task<StoredPdf> PutOriginalAsync(Guid contractId, Stream content, long length, CancellationToken ct) => throw new NotSupportedException();
        public async Task<StoredPdf> PutDerivedAsync(Guid contractId, string purpose, Stream content, long length, CancellationToken ct) { using var copy = new MemoryStream(); await content.CopyToAsync(copy, ct); var bytes = copy.ToArray(); var key = "derived/" + Guid.NewGuid().ToString("N"); data[key] = bytes; return new(key, Hash(bytes), bytes.Length); }
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => data.TryGetValue(key, out var bytes) ? Task.FromResult<Stream>(new MemoryStream(bytes, writable: false)) : throw new FileNotFoundException();
        public Task<bool> ExistsWithHashAsync(string key, string hash, CancellationToken ct) => Task.FromResult(data.TryGetValue(key, out var bytes) && Hash(bytes) == hash);
        public Task DeleteIfUnreferencedAsync(string key, CancellationToken ct) { data.Remove(key); return Task.CompletedTask; }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static byte[] Pdf(string marker) { using var document = new PdfDocument(); document.Info.Title = marker; document.AddPage(); using var output = new MemoryStream(); document.Save(output, false); return output.ToArray(); }
}
