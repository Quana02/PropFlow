using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.Communication.Contracts;
using PropFlow.Modules.Communication.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed partial class AuthenticationFlowTests
{
    public static IEnumerable<object[]> ActiveCommunicationRevocationCases() => RealtimeRevocationCases()
        .Where(row => (string)row[0] == "support-chat").Select(row => new[] { row[1], row[2] })
        .Concat(new object[][] { ["MANAGER", "effective-permission"], ["RESIDENT", "effective-permission"] });

    [Theory]
    [MemberData(nameof(ActiveCommunicationRevocationCases))]
    [Trait("Security", "PF-AUTHZ-04")]
    public async Task Active_chat_preserves_history_and_sends_new_draft_typing_and_message_updates_only_to_current_sessions(string role, string cause)
    {
        await using var permissionRecovery = new PermissionFixtureRecovery(this);
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommunicationDbContext>();
        await db.Database.MigrateAsync();
        var manager = await SeedRealtimeAccount("MANAGER");
        var resident = await SeedRealtimeAccount("RESIDENT");
        var outsider = await SeedRealtimeAccount("RESIDENT");
        using var managerApi = Client();
        using var residentApi = Client();
        using var outsiderApi = Client();
        var m = await SignIn(managerApi, manager.Username);
        var r = await SignIn(residentApi, resident.Username);
        var o = await SignIn(outsiderApi, outsider.Username);
        managerApi.DefaultRequestHeaders.Authorization = new("Bearer", m.AccessToken);
        residentApi.DefaultRequestHeaders.Authorization = new("Bearer", r.AccessToken);
        await using var managerHub = AuthenticationTestSessions.ConnectWebSocket(database.Factory, "support-chat", m.AccessToken, true);
        await using var residentHub = AuthenticationTestSessions.ConnectWebSocket(database.Factory, "support-chat", r.AccessToken, true);
        await using var outsiderHub = AuthenticationTestSessions.ConnectWebSocket(database.Factory, "support-chat", o.AccessToken, true);
        var drafts = Channel.CreateUnbounded<SupportDraft>();
        var typing = Channel.CreateUnbounded<SupportTyping>();
        var unrelated = Channel.CreateUnbounded<Guid>();
        using var draftSubscription = managerHub.On<SupportDraft>("SupportChatDraft", item => drafts.Writer.TryWrite(item));
        using var typingSubscription = residentHub.On<SupportTyping>("SupportChatTyping", item => typing.Writer.TryWrite(item));
        using var foreignDraft = outsiderHub.On<SupportDraft>("SupportChatDraft", item => unrelated.Writer.TryWrite(item.ConversationId));
        using var foreignTyping = outsiderHub.On<SupportTyping>("SupportChatTyping", item => unrelated.Writer.TryWrite(item.ConversationId));
        await managerHub.StartAsync(); await residentHub.StartAsync(); await outsiderHub.StartAsync();
        await managerHub.InvokeAsync("Heartbeat"); await residentHub.InvokeAsync("Heartbeat");
        using var ready = await managerApi.PutAsJsonAsync("api/v1/support-chat/availability", new ChatAvailabilityRequest(true));
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        var firstMessage = new ChatSendRequest(Guid.NewGuid(), "PF04 opening " + Guid.NewGuid());
        using var opened = await residentApi.PostAsJsonAsync("api/v1/support-chat/conversations", firstMessage);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        var chat = (await opened.Content.ReadFromJsonAsync<SupportConversationView>())!;
        Assert.Equal("ACTIVE", chat.Status);
        Assert.Equal(m.User.Id, await db.SupportConversations.Where(item => item.Id == chat.Id).Select(item => item.ManagerUserId).SingleAsync());
        var firstDraftAt = System.Diagnostics.Stopwatch.StartNew();
        await residentHub.InvokeAsync("ResidentDraft", chat.Id, 1L, "PF04 draft before");
        Assert.Equal("PF04 draft before", (await drafts.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3))).Text);
        await managerHub.InvokeAsync("ManagerTyping", chat.Id, true);
        Assert.Equal(chat.Id, (await typing.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3))).ConversationId);
        var affectedApi = role == "MANAGER" ? managerApi : residentApi;
        var affected = role == "MANAGER" ? m : r;
        var account = role == "MANAGER" ? manager : resident;
        var affectedHub = role == "MANAGER" ? managerHub : residentHub;
        var closed = AuthenticationTestSessions.WhenClosed(affectedHub);
        var password = await RevokeRealtimeAccount(affectedApi, affected, account.Email, cause);
        // An inbound call is the first boundary checked in the missed-invalidation case.
        if (cause is "missed-invalidation" or "effective-permission")
            await Assert.ThrowsAnyAsync<Exception>(() => affectedHub.InvokeAsync("Heartbeat"));
        await closed.WaitAsync(TimeSpan.FromSeconds(2));
        using var denied = await affectedApi.GetAsync($"api/v1/support-chat/conversations/{chat.Id}/messages");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        await RestoreRealtimeAccount(affected.User.Id, role, cause);
        using var newApi = Client();
        var fresh = await SignIn(newApi, account.Username, password);
        newApi.DefaultRequestHeaders.Authorization = new("Bearer", fresh.AccessToken);
        await using var freshHub = AuthenticationTestSessions.ConnectWebSocket(database.Factory, "support-chat", fresh.AccessToken, true);
        var freshDrafts = Channel.CreateUnbounded<SupportDraft>();
        var freshTyping = Channel.CreateUnbounded<SupportTyping>();
        var freshChanges = Channel.CreateUnbounded<Guid>();
        using var freshDraftSubscription = freshHub.On<SupportDraft>("SupportChatDraft", item => freshDrafts.Writer.TryWrite(item));
        using var freshTypingSubscription = freshHub.On<SupportTyping>("SupportChatTyping", item => freshTyping.Writer.TryWrite(item));
        using var freshChangesSubscription = freshHub.On<Guid>("SupportChatChanged", id => freshChanges.Writer.TryWrite(id));
        await freshHub.StartAsync(); await freshHub.InvokeAsync("Heartbeat");
        var liveResident = role == "RESIDENT" ? freshHub : residentHub;
        var liveManager = role == "MANAGER" ? freshHub : managerHub;
        var draftText = "PF04 new private draft " + Guid.NewGuid();
        output.WriteLine($"Draft interval before recovery frame: {firstDraftAt.Elapsed.TotalMilliseconds:F1} ms");
        // The unchanged sender connection is subject to the approved 200 ms frame gate.
        var remaining = TimeSpan.FromMilliseconds(220) - firstDraftAt.Elapsed;
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
        await liveResident.InvokeAsync("ResidentDraft", chat.Id, 2L, draftText);
        var deliveredDraft = await (role == "MANAGER" ? freshDrafts : drafts).Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(draftText, deliveredDraft.Text);
        // A clear frame is not throttled by the preceding positive control.
        await liveManager.InvokeAsync("ManagerTyping", chat.Id, false);
        Assert.Equal(chat.Id, (await (role == "RESIDENT" ? freshTyping : typing).Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3))).ConversationId);
        while (freshChanges.Reader.TryRead(out _)) { }
        var marker = "PF04 persisted reply " + Guid.NewGuid();
        using var sent = await (role == "RESIDENT" ? managerApi : residentApi).PostAsJsonAsync(
            $"api/v1/support-chat/conversations/{chat.Id}/messages", new ChatSendRequest(Guid.NewGuid(), marker));
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        Assert.Equal(chat.Id, await freshChanges.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.True(await db.SupportMessages.AnyAsync(item => item.ConversationId == chat.Id && item.Text == marker));
        Assert.True(await db.SupportMessages.AnyAsync(item => item.ConversationId == chat.Id && item.Text == firstMessage.Text));
        Assert.False(await db.SupportMessages.AnyAsync(item => item.ConversationId == chat.Id && item.Text == draftText));
        Assert.False((role == "MANAGER" ? drafts.Reader.TryRead(out _) : typing.Reader.TryRead(out _)));
        Assert.False(unrelated.Reader.TryRead(out _));
        Assert.Equal(HttpStatusCode.Unauthorized, await BearerStatus(affected.AccessToken));
    }
}
