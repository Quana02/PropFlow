using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.Communication.Contracts;
using PropFlow.Modules.Communication.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed partial class CommunicationAuthorizationHttpTests
{
    private static ChatSendRequest Message(string label = "Tin nhắn thử B5") => new(Guid.NewGuid(), label);
    private static string Conversation(Guid id) => $"{Chat}/conversations/{id}";

    [Fact]
    public async Task Chat_availability_open_and_close_follow_the_current_manager_and_resident()
    {
        var ready = new ChatAvailabilityRequest(true);
        var open = Message();
        var availability = Chat + "/availability";
        var start = Chat + "/conversations";
        await Deny(HttpMethod.Put, availability, ready);
        await Reject(owner, HttpMethod.Put, availability, HttpStatusCode.Forbidden, ready);
        await Reject(manager, HttpMethod.Put, availability, HttpStatusCode.Conflict, ready);
        await Deny(HttpMethod.Post, start, open);
        await Reject(manager, HttpMethod.Post, start, HttpStatusCode.Forbidden, open);
        await Reject(unlinked, HttpMethod.Post, start, HttpStatusCode.Forbidden, open);
        var opens = await Task.WhenAll(
            Call<SupportConversationView>(owner, HttpMethod.Post, start, open),
            Call<SupportConversationView>(owner, HttpMethod.Post, start, open));
        var waiting = opens[0];
        Assert.Equal(waiting.Id, opens[1].Id);
        Assert.Equal("WAITING", waiting.Status);
        Assert.Equal("WAITING", (await Open(other)).Status);
        await Connect(manager, "support-chat");
        var assigned = await Call<SupportAvailabilityView>(manager, HttpMethod.Put, availability,
            new { Available = true, UserId = unassigned.Id });
        Assert.True(assigned.Available);
        Assert.Equal(waiting.Id, assigned.ActiveConversationId);
        var outgoing = await Call<SupportAvailabilityView>(manager, HttpMethod.Put, availability, new ChatAvailabilityRequest(false));
        Assert.False(outgoing.Available);
        Assert.Equal(waiting.Id, outgoing.ActiveConversationId);
        await Call<SupportAvailabilityView>(manager, HttpMethod.Put, availability, ready);
        var route = Conversation(waiting.Id) + "/close";
        await Deny(HttpMethod.Post, route);
        await Reject(other, HttpMethod.Post, route, HttpStatusCode.NotFound);
        await Reject(unassigned, HttpMethod.Post, route, HttpStatusCode.NotFound);
        var closes = await Task.WhenAll(Call<SupportConversationView>(owner, HttpMethod.Post, route),
            Call<SupportConversationView>(manager, HttpMethod.Post, route));
        Assert.All(closes, row => Assert.Equal("CLOSED", row.Status));
        using var scope = database.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommunicationDbContext>();
        var history = await db.SupportMessages.Where(row => row.ConversationId == waiting.Id).ToArrayAsync();
        Assert.Equal(1, history.Count(row => row.SenderUserId == owner.Id));
        Assert.Single(history, row => row.Kind == "SYSTEM" && row.Text.Contains("kết thúc"));
        Assert.Equal(manager.Id, (await db.SupportConversations.SingleAsync(row => row.ResidentUserId == other.Id)).ManagerUserId);
        Assert.Null(await db.SupportManagerPresence.SingleOrDefaultAsync(row => row.UserId == unassigned.Id));
    }

    [Fact]
    public async Task Chat_send_and_reply_require_participation_and_preserve_message_identity()
    {
        var (active, foreign) = await ChatPair();
        var route = Conversation(active.Id) + "/messages";
        var message = Message();
        await Deny(HttpMethod.Post, route, message);
        await Reject(other, HttpMethod.Post, route, HttpStatusCode.NotFound, message);
        await Reject(unassigned, HttpMethod.Post, route, HttpStatusCode.NotFound, message);
        var originals = await Task.WhenAll(
            Call<SupportMessageView>(owner, HttpMethod.Post, route, message),
            Call<SupportMessageView>(owner, HttpMethod.Post, route, message));
        Assert.Equal(originals[0].Id, originals[1].Id);
        await Reject(owner, HttpMethod.Post, route, HttpStatusCode.Conflict, message with { Text = "Nội dung khác" });
        var reply = Message() with { ReplyToId = originals[0].Id };
        var foreignMessage = await SendMessage(other, foreign.Id);
        await Reject(manager, HttpMethod.Post, route, HttpStatusCode.BadRequest, reply with { ReplyToId = foreignMessage.Id });
        var saved = await Call<SupportMessageView>(manager, HttpMethod.Post, route,
            new { reply.ClientMessageId, reply.Text, reply.ReplyToId, SenderUserId = owner.Id, Role = "RESIDENT" });
        Assert.Equal(originals[0].Id, saved.Reply!.Id);
        using var scope = database.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommunicationDbContext>();
        Assert.Equal(manager.Id, (await db.SupportMessages.SingleAsync(row => row.Id == saved.Id)).SenderUserId);
        var last = Message("Lượt gửi cạnh tranh với đóng");
        var send = Send(owner.Client, HttpMethod.Post, route, last);
        var close = Send(manager.Client, HttpMethod.Post, Conversation(active.Id) + "/close");
        using var sent = await send;
        using var closed = await close;
        AssertStatus(HttpStatusCode.OK, closed);
        Assert.Contains(sent.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        var terminal = await db.SupportMessages.SingleAsync(row => row.ConversationId == active.Id && row.Kind == "SYSTEM" && row.Text.Contains("kết thúc"));
        var raced = await db.SupportMessages.SingleOrDefaultAsync(row => row.ClientMessageId == last.ClientMessageId);
        Assert.Equal(sent.IsSuccessStatusCode, raced is not null);
        if (raced is not null) Assert.True(raced.Sequence < terminal.Sequence);
        await Reject(owner, HttpMethod.Post, route, HttpStatusCode.Conflict, Message());
    }

    [Fact]
    public async Task Single_and_batch_recall_hide_and_receipts_respect_message_scope()
    {
        var (active, foreign) = await ChatPair();
        var one = await SendMessage(owner, active.Id);
        var two = await SendMessage(owner, active.Id);
        var three = await SendMessage(owner, active.Id);
        var four = await SendMessage(owner, active.Id);
        var foreignMessage = await SendMessage(other, foreign.Id);
        var path = Conversation(active.Id) + "/messages";
        var recallOne = $"{path}/{one.Id}/recall";
        var recallMany = path + "/recall";
        var hide = path + "/hide";
        var receipt = path + "/receipts";
        var select = new ChatMessageSelection([two.Id, three.Id]);
        var ack = new ChatMessageReceipt([four.Id], true);
        foreach (var (method, route, payload) in new (HttpMethod, string, object?)[]
        {
            (HttpMethod.Put, recallOne, null), (HttpMethod.Put, recallMany, select),
            (HttpMethod.Post, hide, select), (HttpMethod.Post, receipt, ack)
        })
        {
            await Deny(method, route, payload);
            await Reject(other, method, route, HttpStatusCode.NotFound, payload);
            await Reject(unassigned, method, route, HttpStatusCode.NotFound, payload);
        }
        await Reject(manager, HttpMethod.Put, recallOne, HttpStatusCode.NotFound);
        await Reject(owner, HttpMethod.Put, $"{path}/{foreignMessage.Id}/recall", HttpStatusCode.NotFound);
        await Reject(manager, HttpMethod.Put, recallMany, HttpStatusCode.NotFound, select);
        await Reject(owner, HttpMethod.Put, recallMany, HttpStatusCode.NotFound, new ChatMessageSelection([two.Id, foreignMessage.Id]));
        var recalls = await Task.WhenAll(
            Call<bool>(owner, HttpMethod.Put, recallOne),
            Call<bool>(owner, HttpMethod.Put, recallOne));
        Assert.All(recalls, value => Assert.True(value));
        Assert.True(await Call<bool>(owner, HttpMethod.Put, recallMany, select));
        var afterRecall = await Snapshot();
        Assert.True(await Call<bool>(owner, HttpMethod.Put, recallMany, select));
        Assert.Equal(afterRecall, await Snapshot());
        Assert.NotNull((await Call<SupportMessageView>(manager, HttpMethod.Get, $"{path}/{one.Id}")).RecalledAt);
        Assert.NotNull((await Call<SupportMessageView>(manager, HttpMethod.Get, $"{path}/{three.Id}")).RecalledAt);

        await Reject(manager, HttpMethod.Post, hide, HttpStatusCode.NotFound, new ChatMessageSelection([four.Id, foreignMessage.Id]));
        var noReceiptChange = await Snapshot();
        Assert.True(await Call<bool>(owner, HttpMethod.Post, receipt, ack));
        Assert.True(await Call<bool>(manager, HttpMethod.Post, receipt, new ChatMessageReceipt([foreignMessage.Id], true)));
        Assert.Equal(noReceiptChange, await Snapshot());
        Assert.True(await Call<bool>(manager, HttpMethod.Post, receipt, new { ack.MessageIds, ack.Seen, ReaderId = other.Id, SeenAt = DateTimeOffset.UnixEpoch }));
        var recorded = await Call<SupportMessageView>(owner, HttpMethod.Get, $"{path}/{four.Id}");
        Assert.NotNull(recorded.SeenAt);
        Assert.NotNull(recorded.DeliveredAt);
        Assert.True(recorded.SeenAt > DateTimeOffset.UnixEpoch);
        var afterReceipt = await Snapshot();
        Assert.True(await Call<bool>(manager, HttpMethod.Post, receipt, ack with { Seen = false }));
        Assert.True(await Call<bool>(manager, HttpMethod.Post, receipt, ack));
        Assert.Equal(afterReceipt, await Snapshot());
        Assert.True(await Call<bool>(manager, HttpMethod.Post, hide, new { MessageIds = new[] { four.Id }, UserId = owner.Id }));
        Assert.True(await Call<bool>(manager, HttpMethod.Post, hide, new ChatMessageSelection([four.Id])));
        await Reject(manager, HttpMethod.Get, $"{path}/{four.Id}", HttpStatusCode.NotFound);
        var stillVisible = await Call<SupportMessageView>(owner, HttpMethod.Get, $"{path}/{four.Id}");
        Assert.Equal(recorded.SeenAt, stillVisible.SeenAt);
        Assert.Null(stillVisible.RecalledAt);
    }

    [Fact]
    public async Task Forwarded_messages_require_visible_source_and_destination_participation()
    {
        var (active, foreign) = await ChatPair();
        var original = await SendMessage(owner, active.Id);
        var otherMessage = await SendMessage(other, foreign.Id);
        var path = Conversation(active.Id) + "/messages/forward";
        var forward = new ChatForwardRequest(active.Id, [new(original.Id, Guid.NewGuid())]);
        await Deny(HttpMethod.Post, path, forward);
        await Reject(other, HttpMethod.Post, path, HttpStatusCode.NotFound, forward);
        await Reject(unassigned, HttpMethod.Post, path, HttpStatusCode.NotFound, forward);
        await Reject(owner, HttpMethod.Post, path, HttpStatusCode.NotFound, forward with { DestinationId = foreign.Id });
        await Reject(owner, HttpMethod.Post, path, HttpStatusCode.NotFound,
            forward with { Items = [forward.Items[0], new(otherMessage.Id, Guid.NewGuid())] });
        Assert.Equal(1, await Call<int>(owner, HttpMethod.Post, path, forward));
        var after = await Snapshot();
        Assert.Equal(1, await Call<int>(owner, HttpMethod.Post, path, forward));
        Assert.Equal(after, await Snapshot());
        Assert.True(await Call<bool>(owner, HttpMethod.Put, Conversation(active.Id) + $"/messages/{original.Id}/recall"));
        await Reject(owner, HttpMethod.Post, path, HttpStatusCode.NotFound,
            forward with { Items = [new(original.Id, Guid.NewGuid())] });
        var visible = await Call<SupportMessagePage>(manager, HttpMethod.Get, Conversation(active.Id) + "/messages");
        Assert.Equal(original.Text, Assert.Single(visible.Items, row => row.Forwarded).Text);
        await Call<SupportConversationView>(manager, HttpMethod.Post, Conversation(active.Id) + "/close");
        await Reject(owner, HttpMethod.Post, path, HttpStatusCode.Conflict,
            forward with { Items = [new(original.Id, Guid.NewGuid())] });
    }

    private Task<SupportConversationView> Open(Actor actor) =>
        Call<SupportConversationView>(actor, HttpMethod.Post, Chat + "/conversations", Message());

    private Task<SupportMessageView> SendMessage(Actor actor, Guid chat) =>
        Call<SupportMessageView>(actor, HttpMethod.Post, Conversation(chat) + "/messages", Message());

    private async Task<(SupportConversationView Active, SupportConversationView Foreign)> ChatPair()
    {
        await Connect(manager, "support-chat");
        await Call<SupportAvailabilityView>(manager, HttpMethod.Put, Chat + "/availability", new ChatAvailabilityRequest(true));
        var active = await Open(owner);
        Assert.Equal("ACTIVE", active.Status);
        return (active, await Open(other));
    }
}
