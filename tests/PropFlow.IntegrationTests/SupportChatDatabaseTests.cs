using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PropFlow.Modules.Communication.Application.SupportChat;
using PropFlow.Modules.Communication.Contracts;
using PropFlow.Modules.Communication.Infrastructure;
using PropFlow.Modules.Communication.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed class SupportChatDatabaseFixture : IAsyncLifetime
{
    private readonly string name = "propflow_fe13_chat_" + Guid.NewGuid().ToString("N");
    private string connection = "", adminConnection = "";
    public async Task InitializeAsync()
    {
        var source = new NpgsqlConnectionStringBuilder(TestDatabaseConfiguration.GetConnectionString());
        if (source.Database != "propflow_test") throw new InvalidOperationException("Unexpected test database source.");
        source.Database = "postgres"; adminConnection = source.ConnectionString;
        await using var admin = new NpgsqlConnection(adminConnection); await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin); await create.ExecuteNonQueryAsync();
        source.Database = name; connection = source.ConnectionString;
        await using var db = CreateDb(); await db.Database.MigrateAsync();
    }
    public CommunicationDbContext CreateDb() => new(new DbContextOptionsBuilder<CommunicationDbContext>()
        .UseNpgsql(connection, o => o.MigrationsHistoryTable("__EFMigrationsHistory", "communication")).Options);
    public async Task DisposeAsync()
    {
        if (!name.StartsWith("propflow_fe13_chat_", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(adminConnection)) return;
        await using var admin = new NpgsqlConnection(adminConnection); await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync();
    }
}

public sealed class SupportChatTestDirectory : ISupportChatActors
{
    public ConcurrentDictionary<Guid, SupportActor> Items = new();
    public Guid Resident() { var id = Guid.NewGuid(); Items[id] = new(id, "RESIDENT", "Cư dân kiểm thử", Guid.NewGuid()); return id; }
    public Guid Manager() { var id = Guid.NewGuid(); Items[id] = new(id, "MANAGER", "Manager kiểm thử"); return id; }
    public Task<SupportActor?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.GetValueOrDefault(id));
}
public sealed class SupportChatTestPush : ISupportChatRealtime
{
    public ConcurrentBag<(Guid Recipient, SupportDraft Draft)> Drafts = new();
    public Task ChangedAsync(Guid id, IReadOnlyCollection<Guid> users, CancellationToken ct) => Task.CompletedTask;
    public Task DraftAsync(Guid recipient, SupportDraft draft, CancellationToken ct) { Drafts.Add((recipient, draft)); return Task.CompletedTask; }
    public Task TypingAsync(Guid recipient, SupportTyping typing, CancellationToken ct) => Task.CompletedTask;
}

[Trait("Feature", "FE-13-ScopeExtension-SupportChat")]
public sealed class SupportChatDatabaseTests(SupportChatDatabaseFixture database) : IClassFixture<SupportChatDatabaseFixture>
{
    private readonly SupportChatTestDirectory directory = new();
    private readonly SupportChatTestPush push = new();
    private SupportChatService Service(CommunicationDbContext db) => new(db, directory, push, TimeProvider.System, NullLogger<SupportChatService>.Instance);
    private async Task Ready(Guid user, string connection)
    { await using var db = database.CreateDb(); var service = Service(db); await service.ConnectedAsync(user, connection, default); await service.SetAvailabilityAsync(user, true, default); }
    private async Task<SupportConversationView> Start(Guid user, Guid key)
    { await using var db = database.CreateDb(); return await Service(db).StartAsync(user, new(key, "Cần hỗ trợ từ Ban quản lý"), default); }

    [Fact]
    public async Task ConcurrentResidentsCannotTakeTheSameManagerAndClosingAssignsTheNextWaitingOne()
    {
        var manager = directory.Manager(); var first = directory.Resident(); var second = directory.Resident();
        await Ready(manager, Guid.NewGuid().ToString());
        var results = await Task.WhenAll(Start(first, Guid.NewGuid()), Start(second, Guid.NewGuid()));
        Assert.Single(results.Where(x => x.Status == "ACTIVE")); Assert.Single(results.Where(x => x.Status == "WAITING"));
        var active = results.Single(x => x.Status == "ACTIVE"); var waiting = results.Single(x => x.Status == "WAITING");
        await using var db = database.CreateDb();
        await Service(db).CloseAsync(manager, active.Id, default);
        Assert.Equal("ACTIVE", (await Service(db).GetAsync(manager, waiting.Id, default)).Status);
        Assert.Equal(1, await db.SupportConversations.CountAsync(x => x.ManagerUserId == manager && x.Status == "ACTIVE"));
    }

    [Fact]
    public async Task TwoManagersBecomingReadyCannotDoubleAssignOneQueuedConversation()
    {
        var resident = directory.Resident(); var one = directory.Manager(); var two = directory.Manager();
        var queued = await Start(resident, Guid.NewGuid()); Assert.Equal("WAITING", queued.Status);
        await Task.WhenAll(Ready(one, Guid.NewGuid().ToString()), Ready(two, Guid.NewGuid().ToString()));
        await using var db = database.CreateDb();
        var saved = await db.SupportConversations.AsNoTracking().SingleAsync(x => x.Id == queued.Id);
        Assert.Equal("ACTIVE", saved.Status); Assert.Contains(saved.ManagerUserId!.Value, new[] { one, two });
        Assert.Equal(1, await db.SupportMessages.CountAsync(x => x.ConversationId == queued.Id && x.Kind == "SYSTEM" && x.Sequence > 2));
    }

    [Fact]
    public async Task ParallelRetriesOfInitialMessageCreateOneConversationAndOneUserMessage()
    {
        var resident = directory.Resident(); var key = Guid.NewGuid();
        var result = await Task.WhenAll(Start(resident, key), Start(resident, key));
        Assert.Equal(result[0].Id, result[1].Id);
        await using var db = database.CreateDb();
        Assert.Equal(1, await db.SupportMessages.CountAsync(x => x.SenderUserId == resident && x.ClientMessageId == key));
        Assert.Equal(1, await db.SupportConversations.CountAsync(x => x.ResidentUserId == resident));
    }

    [Fact]
    public async Task HistoryUsesStableCursorAndOtherParticipantsCannotReadIt()
    {
        var resident = directory.Resident(); var stranger = directory.Resident();
        var chat = await Start(resident, Guid.NewGuid());
        await using var db = database.CreateDb(); var service = Service(db);
        for (var i = 0; i < 53; i++) await service.SendAsync(resident, chat.Id, new(Guid.NewGuid(), "Tin nhắn " + i), default);
        var latest = await service.MessagesAsync(resident, chat.Id, null, default);
        Assert.Equal(50, latest.Items.Count); Assert.True(latest.HasMore);
        var older = await service.MessagesAsync(resident, chat.Id, latest.Items[0].Sequence, default);
        Assert.False(older.HasMore);
        var combined = older.Items.Concat(latest.Items).ToArray();
        Assert.Equal(55, combined.Length); Assert.Equal(55, combined.Select(x => x.Sequence).Distinct().Count());
        Assert.Equal(404, (await Assert.ThrowsAsync<SupportChatFailure>(() => service.MessagesAsync(stranger, chat.Id, null, default))).Status);
    }

    [Fact]
    public async Task DraftsStayOutOfSqlAndStopWhenRecipientLosesPermission()
    {
        var resident = directory.Resident(); var manager = directory.Manager(); await Ready(manager, Guid.NewGuid().ToString());
        var chat = await Start(resident, Guid.NewGuid());
        await using var db = database.CreateDb(); var service = Service(db);
        var before = await db.SupportMessages.CountAsync(x => x.ConversationId == chat.Id);
        await service.DraftAsync(resident, chat.Id, "private-stream", 1, "CHUA_GUI_KHONG_DUOC_LUU", default);
        Assert.Equal(manager, Assert.Single(push.Drafts).Recipient);
        Assert.Equal(before, await db.SupportMessages.CountAsync(x => x.ConversationId == chat.Id));
        directory.Items.TryRemove(manager, out _);
        await service.DraftAsync(resident, chat.Id, "private-stream", 2, "SAU_KHI_THU_HOI_QUYEN", default);
        Assert.Single(push.Drafts);
        Assert.DoesNotContain(await db.SupportMessages.Where(x => x.ConversationId == chat.Id).ToArrayAsync(), x => x.Text.Contains("CHUA_GUI"));
    }
}
