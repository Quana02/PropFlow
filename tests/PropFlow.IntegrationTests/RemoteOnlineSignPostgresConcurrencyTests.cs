using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PdfSharpCore.Pdf;
using PropFlow.Modules.RemoteOnlineSign.Application;
using PropFlow.Modules.RemoteOnlineSign.Domain;
using PropFlow.Modules.RemoteOnlineSign.Infrastructure.Persistence;
using PropFlow.Modules.RemoteOnlineSign.Infrastructure.Storage;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Residents.Contracts;

namespace PropFlow.IntegrationTests;

public sealed class RemoteOnlineSignPostgresConcurrencyTests : IAsyncLifetime
{
    private readonly string databaseName = "propflow_remote_online_sign_" + Guid.NewGuid().ToString("N");
    private string? connectionString;

    public async Task InitializeAsync()
    {
        var configured = TestDatabaseSafety.Dedicated();
        var admin = new NpgsqlConnectionStringBuilder(configured.ConnectionString) { Database = "postgres" };
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync();
            await TestDatabaseSafety.CreateAsync(connection, databaseName);
        }

        var target = new NpgsqlConnectionStringBuilder(configured.ConnectionString) { Database = databaseName };
        connectionString = target.ConnectionString;
        await using var db = CreateDb();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        var configured = TestDatabaseSafety.Dedicated();
        var admin = new NpgsqlConnectionStringBuilder(configured.ConnectionString) { Database = "postgres" };
        await using var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync();
        await TestDatabaseSafety.DropAsync(connection, databaseName);
    }

    [RemotePostgresFact]
    public async Task Concurrent_original_uploads_commit_one_row_and_cleanup_loser_asset()
    {
        var owner = Guid.NewGuid();
        var contract = new RemoteContract { CreatorId = owner };
        await using (var seed = CreateDb())
        {
            seed.Contracts.Add(contract);
            await seed.SaveChangesAsync();
        }

        var storage = new RacingStorage();
        var pdf = CreatePdf();
        await using var firstDb = CreateDb();
        await using var secondDb = CreateDb();
        var first = CreateService(firstDb, storage);
        var second = CreateService(secondDb, storage);

        var attempts = await Task.WhenAll(
            Capture(() => first.UploadOriginalAsync(owner, contract.Id, new MemoryStream(pdf), pdf.Length, CancellationToken.None)),
            Capture(() => second.UploadOriginalAsync(owner, contract.Id, new MemoryStream(pdf), pdf.Length, CancellationToken.None)));

        Assert.Single(attempts, x => x.Error is null);
        var loser = Assert.Single(attempts, x => x.Error is not null).Error;
        var conflict = Assert.IsType<RemoteContractException>(loser);
        Assert.Equal("original_already_uploaded", conflict.Code);

        await using var verify = CreateDb();
        Assert.Single(await verify.Documents.Where(x => x.ContractId == contract.Id && !x.IsFinal).ToListAsync());
        Assert.Single(storage.Stored);
    }

    private RemoteOnlineSignDbContext CreateDb() => new(new DbContextOptionsBuilder<RemoteOnlineSignDbContext>()
        .UseNpgsql(connectionString!, x => x.MigrationsHistoryTable("__EFMigrationsHistory", "remote_online_sign")).Options);

    private static RemoteContractService CreateService(RemoteOnlineSignDbContext db, RacingStorage storage) =>
        new(db, storage, new FakeOtp(), new PdfSignatureStamper(), NullLogger<RemoteContractService>.Instance,
            new ResidentAccess(), new Accounts(), new Residents(), new RemoteContractCodeGenerator(db));

    private static async Task<(RemoteDocumentMetadata? Result, Exception? Error)> Capture(Func<Task<RemoteDocumentMetadata>> action)
    {
        try { return (await action(), null); }
        catch (Exception ex) { return (null, ex); }
    }

    private static byte[] CreatePdf()
    {
        using var document = new PdfDocument();
        document.AddPage();
        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }

    [AttributeUsage(AttributeTargets.Method)]
    private sealed class RemotePostgresFactAttribute : FactAttribute
    {
        public RemotePostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PROPFLOW_TEST_DATABASE")))
                Skip = "Requires an explicitly owned PROPFLOW_TEST_DATABASE PostgreSQL fixture; no development or team database is used.";
        }
    }

    private sealed class RacingStorage : IRemotePdfStorage
    {
        private readonly TaskCompletionSource secondUploadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int uploads;
        public ConcurrentDictionary<string, byte[]> Stored { get; } = new(StringComparer.Ordinal);

        public async Task<StoredPdf> PutOriginalAsync(Guid contractId, Stream content, long length, CancellationToken ct)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, ct);
            var bytes = copy.ToArray();
            if (Interlocked.Increment(ref uploads) == 2) secondUploadStarted.TrySetResult();
            await secondUploadStarted.Task.WaitAsync(ct);
            var key = "test/" + Guid.NewGuid().ToString("N");
            Stored[key] = bytes;
            return new StoredPdf(key, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.Length);
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream(Stored[key], writable: false));
        public Task<StoredPdf> PutDerivedAsync(Guid contractId, string purpose, Stream content, long length, CancellationToken ct) => PutOriginalAsync(contractId, content, length, ct);
        public Task<bool> ExistsWithHashAsync(string key, string hash, CancellationToken ct) => Task.FromResult(Stored.ContainsKey(key));
        public Task DeleteIfUnreferencedAsync(string key, CancellationToken ct) { Stored.TryRemove(key, out _); return Task.CompletedTask; }
    }
    private sealed class FakeOtp : IPrivilegedActionOtp
    {
        public Task<PrivilegedActionChallengeResult> RequestAsync(Guid userId, string purpose, string payloadHash, CancellationToken ct) => Task.FromResult(new PrivilegedActionChallengeResult(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow, "***"));
        public Task VerifyAsync(Guid userId, Guid challengeId, string purpose, string payloadHash, string code, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class ResidentAccess : IAccountAccess
    {
        public Task<AccountAccess?> GetAsync(Guid userId, CancellationToken ct) => Task.FromResult<AccountAccess?>(new("RESIDENT", []));
        public Task<Guid[]> GetResidentUserIdsAsync(Guid[] userIds, CancellationToken ct) => Task.FromResult(userIds);
        public Task GrantResidentAsync(Guid userId, DateTimeOffset now, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class Accounts : IInternalAccountDirectory
    {
        public Task<IReadOnlyList<InternalAccountRecord>> GetAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct) => Task.FromResult<IReadOnlyList<InternalAccountRecord>>(userIds.Select(id => new InternalAccountRecord(id, "test", "Test", null, "ACTIVE")).ToArray());
        public Task<PagedInternalAccountRecords> SearchAsync(IReadOnlyCollection<Guid> userIds, string? search, string? status, int page, int pageSize, CancellationToken ct) => throw new NotSupportedException();
        public Task<InternalAccountRecord> CreateAsync(CreateInternalAccount request, Guid actorUserId, CancellationToken ct) => throw new NotSupportedException();
        public Task<InternalAccountRecord?> SetStatusAsync(Guid userId, string status, Guid actorUserId, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Residents : IResidentAccountScope
    {
        public Task<bool> IsActiveAccountAsync(Guid userId, CancellationToken ct) => Task.FromResult(true);
        public Task<IReadOnlyDictionary<Guid, ResidentAccountScopeItem>> GetByAccountIdsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ResidentAccountScopeItem>>(userIds.ToDictionary(userId => userId, userId => new ResidentAccountScopeItem(Guid.NewGuid(), userId, "TEST", "Test resident", null, true, [])));
        public Task<ResidentAccountScopePage> SearchAsync(string? search, int pageIndex, int pageSize, CancellationToken ct) => throw new NotSupportedException();
        public Task<Guid?> FindActiveAccountAsync(Guid residentId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> LockActiveLinkAsync(Guid residentId, Guid userId, CancellationToken ct) => throw new NotSupportedException();
    }
}
