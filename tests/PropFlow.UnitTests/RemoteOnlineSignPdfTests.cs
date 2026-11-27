using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharpCore.Pdf;
using PropFlow.Modules.RemoteOnlineSign.Application;
using PropFlow.Modules.RemoteOnlineSign.Domain;
using PropFlow.Modules.RemoteOnlineSign.Infrastructure.Persistence;
using PropFlow.Modules.RemoteOnlineSign.Infrastructure.Storage;
using PropFlow.Modules.Authentication.Contracts;

namespace PropFlow.UnitTests;

// These tests deliberately use EF InMemory only for service behaviour. PostgreSQL
// partial-index concurrency is covered separately when a dedicated test database exists.
public sealed class RemoteOnlineSignPdfTests
{
    [Fact]
    public async Task Owner_can_upload_a_valid_original_pdf_once()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddDraftAsync();

        var result = await fixture.Service.UploadOriginalAsync(fixture.Owner, contract.Id,
            new MemoryStream(CreatePdf()), CreatePdf().Length, CancellationToken.None);

        Assert.Equal(1, result.PageCount);
        Assert.Single(fixture.Db.Documents);
        Assert.Single(fixture.Storage.Stored);
    }

    [Fact]
    public async Task Different_admin_cannot_upload_another_admins_contract()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddDraftAsync();
        var ex = await Assert.ThrowsAsync<RemoteContractException>(() => fixture.Service.UploadOriginalAsync(
            Guid.NewGuid(), contract.Id, new MemoryStream(CreatePdf()), CreatePdf().Length, CancellationToken.None));
        Assert.Equal(404, ex.Status);
        Assert.Empty(fixture.Storage.Stored);
    }

    [Fact]
    public async Task Resident_cannot_upload_contract_even_when_assigned()
    {
        await using var fixture = new Fixture();
        var resident = Guid.NewGuid();
        var contract = await fixture.AddDraftAsync(resident);
        var ex = await Assert.ThrowsAsync<RemoteContractException>(() => fixture.Service.UploadOriginalAsync(
            resident, contract.Id, new MemoryStream(CreatePdf()), CreatePdf().Length, CancellationToken.None));
        Assert.Equal("contract_not_found", ex.Code);
    }

    [Fact]
    public async Task Assigned_resident_can_download_and_other_resident_gets_not_found()
    {
        await using var fixture = new Fixture();
        var resident = Guid.NewGuid();
        var contract = await fixture.AddDraftAsync(resident);
        var pdf = CreatePdf();
        await fixture.Service.UploadOriginalAsync(fixture.Owner, contract.Id, new MemoryStream(pdf), pdf.Length, CancellationToken.None);
        contract.SigningMode = RemoteSigningMode.PRE_SIGNED_INVESTOR;
        contract.Status = RemoteContractStatus.PENDING_RESIDENT_SIGNATURE;
        await fixture.Db.SaveChangesAsync();

        var download = await fixture.Service.DownloadOriginalAsync(resident, false, contract.Id, CancellationToken.None);
        await using (download.Content)
        using (var copy = new MemoryStream())
        {
            await download.Content.CopyToAsync(copy);
            Assert.Equal(pdf, copy.ToArray());
        }

        var ex = await Assert.ThrowsAsync<RemoteContractException>(() => fixture.Service.DownloadOriginalAsync(
            Guid.NewGuid(), false, contract.Id, CancellationToken.None));
        Assert.Equal(404, ex.Status);
    }

    [Theory]
    [InlineData("not-a-pdf")]
    [InlineData("%PDF-broken")]
    public async Task Invalid_or_malformed_pdf_is_rejected_before_storage(string content)
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddDraftAsync();
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var ex = await Assert.ThrowsAsync<RemoteContractException>(() => fixture.Service.UploadOriginalAsync(
            fixture.Owner, contract.Id, new MemoryStream(bytes), bytes.Length, CancellationToken.None));
        Assert.Equal("invalid_pdf", ex.Code);
        Assert.Empty(fixture.Storage.Stored);
    }

    [Fact]
    public async Task Pdf_larger_than_20_mb_is_rejected_before_storage()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddDraftAsync();
        var bytes = new byte[(20 * 1024 * 1024) + 1];
        var ex = await Assert.ThrowsAsync<RemoteContractException>(() => fixture.Service.UploadOriginalAsync(
            fixture.Owner, contract.Id, new MemoryStream(bytes), bytes.Length, CancellationToken.None));
        Assert.Equal("pdf_size_invalid", ex.Code);
        Assert.Empty(fixture.Storage.Stored);
    }

    [Fact]
    public async Task Pdf_with_more_than_100_pages_is_rejected_before_storage()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddDraftAsync();
        var pdf = CreatePdf(101);
        var ex = await Assert.ThrowsAsync<RemoteContractException>(() => fixture.Service.UploadOriginalAsync(
            fixture.Owner, contract.Id, new MemoryStream(pdf), pdf.Length, CancellationToken.None));
        Assert.Equal("pdf_page_limit", ex.Code);
        Assert.Empty(fixture.Storage.Stored);
    }

    [Fact]
    public async Task Password_protected_pdf_is_rejected_before_storage()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddDraftAsync();
        var pdf = CreatePasswordProtectedPdf();
        var ex = await Assert.ThrowsAsync<RemoteContractException>(() => fixture.Service.UploadOriginalAsync(
            fixture.Owner, contract.Id, new MemoryStream(pdf), pdf.Length, CancellationToken.None));
        Assert.Equal("invalid_pdf", ex.Code);
        Assert.Empty(fixture.Storage.Stored);
    }

    [Fact]
    public async Task Rotation_metadata_is_persisted_from_the_pdf()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddDraftAsync();
        var pdf = CreatePdf(rotation: 90);
        var result = await fixture.Service.UploadOriginalAsync(fixture.Owner, contract.Id,
            new MemoryStream(pdf), pdf.Length, CancellationToken.None);
        Assert.Contains("90", result.PageMetadata);
    }

    [Fact]
    public async Task Upload_failure_does_not_create_document_metadata()
    {
        await using var fixture = new Fixture { Storage = { FailPut = true } };
        var contract = await fixture.AddDraftAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.UploadOriginalAsync(
            fixture.Owner, contract.Id, new MemoryStream(CreatePdf()), CreatePdf().Length, CancellationToken.None));
        Assert.Empty(fixture.Db.Documents);
    }

    [Fact]
    public async Task Failed_upload_can_be_retried_without_overwriting_a_document()
    {
        await using var fixture = new Fixture { Storage = { FailPut = true } };
        var contract = await fixture.AddDraftAsync();
        var pdf = CreatePdf();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.UploadOriginalAsync(
            fixture.Owner, contract.Id, new MemoryStream(pdf), pdf.Length, CancellationToken.None));

        fixture.Storage.FailPut = false;
        await fixture.Service.UploadOriginalAsync(fixture.Owner, contract.Id, new MemoryStream(pdf), pdf.Length, CancellationToken.None);
        Assert.Single(fixture.Db.Documents);
        Assert.Single(fixture.Storage.Stored);
    }

    [Fact]
    public async Task Duplicate_original_upload_is_rejected_without_second_storage_write()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddDraftAsync();
        var pdf = CreatePdf();
        await fixture.Service.UploadOriginalAsync(fixture.Owner, contract.Id, new MemoryStream(pdf), pdf.Length, CancellationToken.None);
        var ex = await Assert.ThrowsAsync<RemoteContractException>(() => fixture.Service.UploadOriginalAsync(
            fixture.Owner, contract.Id, new MemoryStream(pdf), pdf.Length, CancellationToken.None));
        Assert.Equal("original_already_uploaded", ex.Code);
        Assert.Single(fixture.Storage.Stored);
    }

    [Fact]
    public async Task Hash_mismatch_on_download_is_reported_and_stream_is_not_returned()
    {
        await using var fixture = new Fixture();
        var resident = Guid.NewGuid();
        var contract = await fixture.AddDraftAsync(resident);
        var pdf = CreatePdf();
        await fixture.Service.UploadOriginalAsync(fixture.Owner, contract.Id, new MemoryStream(pdf), pdf.Length, CancellationToken.None);
        contract.SigningMode = RemoteSigningMode.PRE_SIGNED_INVESTOR;
        contract.Status = RemoteContractStatus.PENDING_RESIDENT_SIGNATURE;
        await fixture.Db.SaveChangesAsync();
        fixture.Storage.ReadOverride = [1, 2, 3];
        var ex = await Assert.ThrowsAsync<RemoteContractException>(() => fixture.Service.DownloadOriginalAsync(
            resident, false, contract.Id, CancellationToken.None));
        Assert.Equal("document_integrity_failed", ex.Code);
    }

    [Fact]
    public async Task Non_draft_contract_cannot_receive_an_original_pdf()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddDraftAsync();
        contract.Status = RemoteContractStatus.CANCELLED;
        await fixture.Db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<RemoteContractException>(() => fixture.Service.UploadOriginalAsync(
            fixture.Owner, contract.Id, new MemoryStream(CreatePdf()), CreatePdf().Length, CancellationToken.None));
        Assert.Equal("contract_not_draft", ex.Code);
    }

    [Fact]
    public async Task Database_save_failure_cleans_up_only_the_uploaded_artifact()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddDraftAsync();
        fixture.FailNextSave = true;
        var pdf = CreatePdf();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.UploadOriginalAsync(
            fixture.Owner, contract.Id, new MemoryStream(pdf), pdf.Length, CancellationToken.None));
        Assert.Empty(fixture.Storage.Stored);
        Assert.Empty(fixture.Db.Documents);
    }

    [Fact]
    public async Task Cleanup_failure_leaves_orphan_for_operational_reconciliation()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddDraftAsync();
        fixture.FailNextSave = true;
        fixture.Storage.FailDelete = true;
        var pdf = CreatePdf();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.UploadOriginalAsync(
            fixture.Owner, contract.Id, new MemoryStream(pdf), pdf.Length, CancellationToken.None));
        Assert.Single(fixture.Storage.Stored);
        Assert.Empty(fixture.Db.Documents);
    }

    [Fact]
    public async Task Mode_a_requires_investor_then_resident_and_creates_final_pdf()
    {
        await using var fixture = new Fixture();
        var resident = Guid.NewGuid(); var contract = await fixture.AddDraftAsync();
        var pdf = CreatePdf(); await fixture.Service.UploadOriginalAsync(fixture.Owner, contract.Id, new MemoryStream(pdf), pdf.Length, CancellationToken.None);
        await fixture.Service.SetFieldsAsync(fixture.Owner, contract.Id, [new(SignatureRole.INVESTOR, 1, 10, 10, 100, 30), new(SignatureRole.RESIDENT, 1, 10, 50, 100, 30)], CancellationToken.None);
        await Assert.ThrowsAsync<RemoteContractException>(() => fixture.Service.SendAsync(fixture.Owner, contract.Id, new(), CancellationToken.None));
        await Assert.ThrowsAsync<RemoteContractException>(() => fixture.Service.AssignAsync(fixture.Owner, contract.Id, new(resident), CancellationToken.None));
        await Assert.ThrowsAsync<RemoteContractException>(() => fixture.Service.GetAsync(resident, false, contract.Id, CancellationToken.None));
        var investor = await fixture.Service.BeginSigningAsync(fixture.Owner, true, contract.Id, new(SignatureMethod.TYPE, null, "Investor", "Arial"), CancellationToken.None);
        var investorSigned = await fixture.Service.VerifyAndFinalizeAsync(fixture.Owner, true, contract.Id, new(investor.SessionId, "000000"), CancellationToken.None);
        Assert.Equal("INVESTOR_SIGNED_DRAFT", investorSigned.Status);
        await fixture.Service.AssignAsync(fixture.Owner, contract.Id, new(resident), CancellationToken.None);
        await fixture.Service.SendAsync(fixture.Owner, contract.Id, new(), CancellationToken.None);
        var residentSigning = await fixture.Service.BeginSigningAsync(resident, false, contract.Id, new(SignatureMethod.TYPE, null, "Resident", "Arial"), CancellationToken.None);
        var done = await fixture.Service.VerifyAndFinalizeAsync(resident, false, contract.Id, new(residentSigning.SessionId, "000000"), CancellationToken.None);
        Assert.Equal("COMPLETED", done.Status); Assert.Single(fixture.Db.Documents.Where(x => x.IsFinal)); Assert.Equal(3, fixture.Storage.Stored.Count);
    }

    [Fact]
    public async Task Mode_b_skips_investor_without_second_confirmation()
    {
        await using var fixture = new Fixture(); var resident = Guid.NewGuid(); var contract = await fixture.AddDraftAsync(resident); contract.SigningMode = RemoteSigningMode.PRE_SIGNED_INVESTOR; await fixture.Db.SaveChangesAsync(); var pdf = CreatePdf(); await fixture.Service.UploadOriginalAsync(fixture.Owner, contract.Id, new MemoryStream(pdf), pdf.Length, CancellationToken.None);
        await fixture.Service.SetFieldsAsync(fixture.Owner, contract.Id, [new(SignatureRole.RESIDENT, 1, 10, 10, 100, 30)], CancellationToken.None);
        await fixture.Service.SendAsync(fixture.Owner, contract.Id, new(), CancellationToken.None);
        var signing = await fixture.Service.BeginSigningAsync(resident, false, contract.Id, new(SignatureMethod.TYPE, null, "Resident", "Arial"), CancellationToken.None);
        var done = await fixture.Service.VerifyAndFinalizeAsync(resident, false, contract.Id, new(signing.SessionId, "000000"), CancellationToken.None); Assert.Equal("COMPLETED", done.Status);
    }

    [Fact]
    public async Task Mode_b_assigns_resident_after_pdf_and_resident_field_without_second_confirmation()
    {
        await using var fixture = new Fixture(); var resident = Guid.NewGuid(); var contract = await fixture.AddDraftAsync(); contract.SigningMode = RemoteSigningMode.PRE_SIGNED_INVESTOR; await fixture.Db.SaveChangesAsync(); var pdf = CreatePdf();
        await fixture.Service.UploadOriginalAsync(fixture.Owner, contract.Id, new MemoryStream(pdf), pdf.Length, CancellationToken.None);
        await fixture.Service.SetFieldsAsync(fixture.Owner, contract.Id, [new(SignatureRole.RESIDENT, 1, 10, 10, 100, 30)], CancellationToken.None);
        var assigned = await fixture.Service.AssignAsync(fixture.Owner, contract.Id, new(resident), CancellationToken.None);
        Assert.Equal(resident, assigned.ResidentId);
    }

    [Fact]
    public async Task Mode_a_investor_signed_draft_without_working_pdf_is_not_fallen_back_to_original()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddDraftAsync();
        var pdf = CreatePdf();
        await fixture.Service.UploadOriginalAsync(fixture.Owner, contract.Id, new MemoryStream(pdf), pdf.Length, CancellationToken.None);
        contract.Status = RemoteContractStatus.INVESTOR_SIGNED_DRAFT;
        await fixture.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<RemoteContractException>(() => fixture.Service.DownloadOriginalAsync(
            fixture.Owner, true, contract.Id, CancellationToken.None));

        Assert.Equal(409, ex.Status);
        Assert.Equal("working_document_missing", ex.Code);
    }

    [Fact]
    public async Task Storage_timeout_is_reported_as_pdf_download_timeout_not_server_error()
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddDraftAsync();
        var pdf = CreatePdf();
        await fixture.Service.UploadOriginalAsync(fixture.Owner, contract.Id, new MemoryStream(pdf), pdf.Length, CancellationToken.None);
        fixture.Storage.ReadError = new TaskCanceledException("simulated storage timeout");

        var ex = await Assert.ThrowsAsync<RemoteContractException>(() => fixture.Service.DownloadOriginalAsync(
            fixture.Owner, true, contract.Id, CancellationToken.None));

        Assert.Equal(503, ex.Status);
        Assert.Equal("pdf_download_timeout", ex.Code);
    }

    [Fact]
    public async Task Legacy_pending_investor_signature_downloads_the_original_pdf()
    {
        await using var fixture = new Fixture();
        var resident = Guid.NewGuid();
        var contract = await fixture.AddDraftAsync(resident);
        var pdf = CreatePdf();
        await fixture.Service.UploadOriginalAsync(fixture.Owner, contract.Id, new MemoryStream(pdf), pdf.Length, CancellationToken.None);
        contract.Status = RemoteContractStatus.PENDING_INVESTOR_SIGNATURE;
        await fixture.Db.SaveChangesAsync();

        var download = await fixture.Service.DownloadOriginalAsync(fixture.Owner, true, contract.Id, CancellationToken.None);
        await using (download.Content)
        using (var copy = new MemoryStream())
        {
            await download.Content.CopyToAsync(copy);
            Assert.Equal(pdf, copy.ToArray());
        }
    }

    [Fact]
    public async Task Mode_b_pending_resident_signature_still_downloads_the_original_pdf()
    {
        await using var fixture = new Fixture();
        var resident = Guid.NewGuid();
        var contract = await fixture.AddDraftAsync(resident);
        contract.SigningMode = RemoteSigningMode.PRE_SIGNED_INVESTOR;
        await fixture.Db.SaveChangesAsync();
        var pdf = CreatePdf();
        await fixture.Service.UploadOriginalAsync(fixture.Owner, contract.Id, new MemoryStream(pdf), pdf.Length, CancellationToken.None);
        contract.Status = RemoteContractStatus.PENDING_RESIDENT_SIGNATURE;
        await fixture.Db.SaveChangesAsync();

        var download = await fixture.Service.DownloadOriginalAsync(fixture.Owner, true, contract.Id, CancellationToken.None);
        await using (download.Content)
        using (var copy = new MemoryStream())
        {
            await download.Content.CopyToAsync(copy);
            Assert.Equal(pdf, copy.ToArray());
        }
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("io")]
    public async Task Storage_upload_failures_are_mapped_to_actionable_errors(string kind)
    {
        await using var fixture = new Fixture();
        var contract = await fixture.AddDraftAsync();
        fixture.Storage.PutError = kind == "timeout" ? new TaskCanceledException("simulated storage timeout") : new IOException("simulated storage failure");

        var ex = await Assert.ThrowsAsync<RemoteContractException>(() => fixture.Service.UploadOriginalAsync(
            fixture.Owner, contract.Id, new MemoryStream(CreatePdf()), CreatePdf().Length, CancellationToken.None));

        Assert.Equal(503, ex.Status);
        Assert.Equal(kind == "timeout" ? "pdf_upload_timeout" : "pdf_upload_failed", ex.Code);
        Assert.Empty(fixture.Db.Documents);
    }

    private static byte[] CreatePdf(int pages = 1, int rotation = 0)
    {
        using var document = new PdfDocument();
        for (var i = 0; i < pages; i++)
        {
            var page = document.AddPage();
            page.Rotate = rotation;
        }
        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }

    private static byte[] CreatePasswordProtectedPdf()
    {
        using var document = new PdfDocument();
        document.AddPage();
        document.SecuritySettings.UserPassword = "test-password";
        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Guid Owner { get; } = Guid.NewGuid();
        public FakeStorage Storage { get; } = new();
        public RemoteOnlineSignDbContext Db { get; }
        public RemoteContractService Service { get; }
        public FakeOtp Otp { get; } = new();
        private readonly ThrowOnDemandSaveInterceptor saveInterceptor = new();
        public bool FailNextSave { set => saveInterceptor.FailNextSave = value; }

        public Fixture()
        {
            Db = new RemoteOnlineSignDbContext(new DbContextOptionsBuilder<RemoteOnlineSignDbContext>()
                .UseInMemoryDatabase("remote-online-sign-" + Guid.NewGuid())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .AddInterceptors(saveInterceptor).Options);
            Service = new RemoteContractService(Db, Storage, Otp, new PdfSignatureStamper(), NullLogger<RemoteContractService>.Instance,
                new RemoteTestAccountAccess(), new RemoteTestAccountDirectory(), new RemoteTestResidentScope(), new RemoteContractCodeGenerator(Db));
        }

        public async Task<RemoteContract> AddDraftAsync(Guid? resident = null)
        {
            var contract = new RemoteContract { CreatorId = Owner, ResidentId = resident };
            Db.Contracts.Add(contract);
            await Db.SaveChangesAsync();
            return contract;
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class ThrowOnDemandSaveInterceptor : SaveChangesInterceptor
    {
        public bool FailNextSave { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new InvalidOperationException("simulated database save failure");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class FakeStorage : IRemotePdfStorage
    {
        public bool FailPut { get; set; }
        public bool FailDelete { get; set; }
        public byte[]? ReadOverride { get; set; }
        public Exception? ReadError { get; set; }
        public Exception? PutError { get; set; }
        public Dictionary<string, byte[]> Stored { get; } = new(StringComparer.Ordinal);

        public async Task<StoredPdf> PutOriginalAsync(Guid contractId, Stream content, long length, CancellationToken ct)
        {
            if (PutError is not null) throw PutError;
            if (FailPut) throw new InvalidOperationException("storage failed");
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, ct);
            var bytes = copy.ToArray();
            var key = "test/" + Guid.NewGuid().ToString("N");
            Stored.Add(key, bytes);
            return new StoredPdf(key, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.Length);
        }
        public Task<StoredPdf> PutDerivedAsync(Guid contractId, string purpose, Stream content, long length, CancellationToken ct) => PutOriginalAsync(contractId, content, length, ct);

        public Task<Stream> OpenReadAsync(string key, CancellationToken ct)
        {
            if (ReadError is not null) throw ReadError;
            var bytes = ReadOverride ?? Stored[key];
            return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
        }

        public async Task<bool> ExistsWithHashAsync(string key, string hash, CancellationToken ct)
        {
            await using var stream = await OpenReadAsync(key, ct);
            return string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant(), hash, StringComparison.OrdinalIgnoreCase);
        }

        public Task DeleteIfUnreferencedAsync(string key, CancellationToken ct)
        {
            if (FailDelete) throw new InvalidOperationException("cleanup failed");
            Stored.Remove(key);
            return Task.CompletedTask;
        }
    }
    private sealed class FakeOtp : IPrivilegedActionOtp
    {
        public Task<PrivilegedActionChallengeResult> RequestAsync(Guid userId, string purpose, string payloadHash, CancellationToken ct) => Task.FromResult(new PrivilegedActionChallengeResult(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow, "***"));
        public Task VerifyAsync(Guid userId, Guid challengeId, string purpose, string payloadHash, string code, CancellationToken ct) => Task.CompletedTask;
    }
}
