using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Modules.Communication.Application.SupportChat;
using PropFlow.Modules.Communication.Contracts;
using PropFlow.Modules.Communication.Infrastructure;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Presentation;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-13-ScopeExtension-SupportChat")]
public sealed class SupportChatTests
{
    [Fact]
    public async Task GreetsResidentAndWaitsUntilAnOnlineManagerExplicitlyBecomesReady()
    {
        await using var f = new Fixture();
        var opened = await f.Chat.StartAsync(f.Resident, new(Guid.NewGuid(), "Cần hỗ trợ sửa vòi nước"), default);
        Assert.Equal("WAITING", opened.Status);
        Assert.Equal(SupportChatService.Greeting, (await f.Chat.MessagesAsync(f.Resident, opened.Id, null, default)).Items[0].Text);
        await f.Chat.ConnectedAsync(f.Manager, "manager-tab", default);
        Assert.Equal("WAITING", (await f.Chat.GetAsync(f.Resident, opened.Id, default)).Status);
        await f.Chat.SetAvailabilityAsync(f.Manager, true, default);
        var assigned = await f.Chat.GetAsync(f.Resident, opened.Id, default);
        Assert.Equal("ACTIVE", assigned.Status); Assert.Equal("Manager 1", assigned.ManagerName); Assert.True(assigned.ManagerOnline);
    }

    [Fact]
    public async Task ClosingOneConversationReleasesManagerToTheNextWaitingResident()
    {
        await using var f = new Fixture(); await f.Ready();
        var first = await f.Chat.StartAsync(f.Resident, new(Guid.NewGuid(), "Hỗ trợ một"), default);
        var second = await f.Chat.StartAsync(f.OtherResident, new(Guid.NewGuid(), "Hỗ trợ hai"), default);
        Assert.Equal("ACTIVE", first.Status); Assert.Equal("WAITING", second.Status);
        await f.Chat.CloseAsync(f.Manager, first.Id, default);
        Assert.Equal("ACTIVE", (await f.Chat.GetAsync(f.OtherResident, second.Id, default)).Status);
        Assert.Equal("CLOSED", (await f.Chat.GetAsync(f.Resident, first.Id, default)).Status);
    }

    [Fact]
    public async Task DraftOnlyReachesAssignedManagerAndIsNeverSavedOrSentAfterClose()
    {
        await using var f = new Fixture(); await f.Ready();
        var chat = await f.Chat.StartAsync(f.Resident, new(Guid.NewGuid(), "Cần hỗ trợ"), default);
        var before = await f.Db.SupportMessages.CountAsync();
        await f.Chat.DraftAsync(f.Resident, chat.Id, "stream", 1, "UNSENT_PRIVATE_DRAFT", default);
        Assert.Equal(f.Manager, Assert.Single(f.Push.Drafts).Recipient);
        Assert.Equal(before, await f.Db.SupportMessages.CountAsync());
        Assert.DoesNotContain(await f.Db.SupportMessages.ToArrayAsync(), x => x.Text.Contains("UNSENT_PRIVATE_DRAFT"));
        await f.Chat.CloseAsync(f.Resident, chat.Id, default);
        await f.Chat.DraftAsync(f.Resident, chat.Id, "stream", 2, "AFTER_CLOSE", default);
        Assert.Single(f.Push.Drafts);
    }

    [Fact]
    public async Task GuessingAnIdDoesNotLetOtherResidentsOrManagersReadSendOrSeeDrafts()
    {
        await using var f = new Fixture(); await f.Ready();
        var chat = await f.Chat.StartAsync(f.Resident, new(Guid.NewGuid(), "Nội dung riêng"), default);
        foreach (var actor in new[] { f.OtherResident, f.OtherManager })
        {
            Assert.Equal(404, (await Assert.ThrowsAsync<SupportChatFailure>(() => f.Chat.GetAsync(actor, chat.Id, default))).Status);
            Assert.Equal(404, (await Assert.ThrowsAsync<SupportChatFailure>(() => f.Chat.SendAsync(actor, chat.Id, new(Guid.NewGuid(), "Không được gửi"), default))).Status);
        }
        Assert.Equal(404, (await Assert.ThrowsAsync<SupportChatFailure>(() => f.Chat.DraftAsync(f.OtherResident, chat.Id, "other", 1, "Không được xem", default))).Status);
        Assert.Empty(f.Push.Drafts);
        f.Actors.Items.Remove(f.Manager);
        await f.Chat.DraftAsync(f.Resident, chat.Id, "owner", 1, "Không gửi cho tài khoản mất quyền", default);
        Assert.Empty(f.Push.Drafts);
    }

    [Fact]
    public async Task RetryingCommittedMessageIsIdempotentButChangedPayloadConflicts()
    {
        await using var f = new Fixture(); await f.Ready();
        var key = Guid.NewGuid(); var chat = await f.Chat.StartAsync(f.Resident, new(key, "Cần hỗ trợ"), default);
        Assert.Equal(chat.Id, (await f.Chat.StartAsync(f.Resident, new(key, "Cần hỗ trợ"), default)).Id);
        var sendKey = Guid.NewGuid(); var message = await f.Chat.SendAsync(f.Resident, chat.Id, new(sendKey, "Thông tin thêm"), default);
        Assert.Equal(message.Id, (await f.Chat.SendAsync(f.Resident, chat.Id, new(sendKey, "Thông tin thêm"), default)).Id);
        Assert.Equal(409, (await Assert.ThrowsAsync<SupportChatFailure>(() => f.Chat.SendAsync(f.Resident, chat.Id, new(sendKey, "Nội dung khác"), default))).Status);
        await f.Chat.CloseAsync(f.Resident, chat.Id, default);
        Assert.Equal(message.Id, (await f.Chat.SendAsync(f.Resident, chat.Id, new(sendKey, "Thông tin thêm"), default)).Id);
        Assert.Equal(409, (await Assert.ThrowsAsync<SupportChatFailure>(() => f.Chat.SendAsync(f.Resident, chat.Id, new(Guid.NewGuid(), "Phiên đã đóng"), default))).Status);
    }

    [Fact]
    public async Task DisconnectingOneManagerTabKeepsOtherTabsOnlineAndFreshResidencyIsRequired()
    {
        await using var f = new Fixture(); await f.Ready(); await f.Chat.ConnectedAsync(f.Manager, "second-tab", default);
        await f.Chat.DisconnectedAsync("manager-tab", default);
        Assert.True((await f.Chat.AvailabilityAsync(f.Manager, default)).Connected);
        await f.Chat.DisconnectedAsync("second-tab", default);
        Assert.False((await f.Chat.AvailabilityAsync(f.Manager, default)).Available);
        f.Actors.Items.Remove(f.Resident);
        Assert.Equal(403, (await Assert.ThrowsAsync<SupportChatFailure>(() => f.Chat.StartAsync(f.Resident, new(Guid.NewGuid(), "Không có cư trú"), default))).Status);
    }

    [Fact]
    public async Task RealtimeFailureDoesNotUndoPersistedMessages()
    {
        await using var f = new Fixture(); f.Push.FailHints = true;
        var chat = await f.Chat.StartAsync(f.Resident, new(Guid.NewGuid(), "Lưu thật dù push lỗi"), default);
        Assert.Contains((await f.Chat.MessagesAsync(f.Resident, chat.Id, null, default)).Items, x => x.Text == "Lưu thật dù push lỗi");
    }

    [Fact]
    public void DraftGateRejectsOldFramesAndFloodingButAlwaysAllowsANewerClear()
    {
        var gate = new SupportChatFrameGate();
        Assert.True(gate.Draft("connection", 1, false)); Assert.False(gate.Draft("connection", 1, true));
        Assert.False(gate.Draft("connection", 2, false)); Assert.True(gate.Draft("connection", 3, true));
        Assert.True(gate.Heartbeat("connection")); Assert.False(gate.Heartbeat("connection"));
        gate.Remove("connection"); Assert.True(gate.Draft("connection", 1, false));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Guid Resident = Guid.NewGuid(), OtherResident = Guid.NewGuid(), Manager = Guid.NewGuid(), OtherManager = Guid.NewGuid();
        public CommunicationDbContext Db = new(new DbContextOptionsBuilder<CommunicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public Directory Actors = new(); public Pushes Push = new(); public SupportChatService Chat;
        public Fixture()
        {
            Actors.Items[Resident] = new(Resident, "RESIDENT", "Cư dân 1", Guid.NewGuid());
            Actors.Items[OtherResident] = new(OtherResident, "RESIDENT", "Cư dân 2", Guid.NewGuid());
            Actors.Items[Manager] = new(Manager, "MANAGER", "Manager 1"); Actors.Items[OtherManager] = new(OtherManager, "MANAGER", "Manager 2");
            Chat = new(Db, Actors, Push, TimeProvider.System, NullLogger<SupportChatService>.Instance);
        }
        public async Task Ready() { await Chat.ConnectedAsync(Manager, "manager-tab", default); await Chat.SetAvailabilityAsync(Manager, true, default); }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class Directory : ISupportChatActors
    {
        public Dictionary<Guid, SupportActor> Items = [];
        public Task<SupportActor?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.GetValueOrDefault(id));
    }
    private sealed class Pushes : ISupportChatRealtime
    {
        public bool FailHints;
        public List<(Guid Recipient, SupportDraft Draft)> Drafts = [];
        public Task ChangedAsync(Guid id, IReadOnlyCollection<Guid> users, CancellationToken ct) => FailHints ? throw new IOException("Test hint failure") : Task.CompletedTask;
        public Task DraftAsync(Guid recipient, SupportDraft draft, CancellationToken ct) { Drafts.Add((recipient, draft)); return Task.CompletedTask; }
        public Task TypingAsync(Guid recipient, SupportTyping typing, CancellationToken ct) => Task.CompletedTask;
    }
}
