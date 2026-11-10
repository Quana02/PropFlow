using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Communication.Application.SupportChat;
using PropFlow.Modules.Communication.Contracts;
using PropFlow.Modules.Communication.Domain.SupportChat;

namespace PropFlow.Modules.Communication.Infrastructure;

public sealed partial class SupportChatService
{
    private const string RecalledText = "Tin nhắn đã được thu hồi";

    private IQueryable<SupportMessage> VisibleMessages(SupportActor actor) => actor.Role == "MANAGER"
        ? db.SupportMessages.Where(message => !message.HiddenForManager)
        : db.SupportMessages.Where(message => !message.HiddenForResident);

    private static bool IsVisible(SupportMessage message, SupportActor actor) =>
        actor.Role == "MANAGER" ? !message.HiddenForManager : !message.HiddenForResident;

    private async Task<SupportMessageView[]> MessageViews(SupportActor actor, Guid id, SupportMessage[] messages, CancellationToken ct)
    {
        var replyIds = messages.Where(m => m.RecalledAt is null && m.ReplyToId.HasValue)
            .Select(m => m.ReplyToId!.Value).Distinct().ToArray();
        var replies = replyIds.Length == 0 ? new Dictionary<Guid, SupportMessage>() :
            await VisibleMessages(actor).AsNoTracking().Where(m => m.ConversationId == id && replyIds.Contains(m.Id) && m.RecalledAt == null)
                .ToDictionaryAsync(m => m.Id, ct);
        return messages.Select(message =>
        {
            SupportReplyPreview? reply = null;
            if (message.RecalledAt is null && message.ReplyToId is Guid replyId)
                reply = replies.TryGetValue(replyId, out var target)
                    ? new(replyId, target.SenderName, PreviewText(target.Text)) : new(replyId, null, null);
            return new SupportMessageView(message.Id, message.Sequence, message.Kind, message.SenderName,
                message.RecalledAt is null ? message.Text : RecalledText, message.CreatedAt,
                reply, message.RecalledAt is null && message.ForwardedFromId is not null,
                message.RecalledAt, message.DeliveredAt, message.SeenAt);
        }).ToArray();
    }

    private static string PreviewText(string text) => text.Length <= PreviewLength ? text :
        text[..(char.IsHighSurrogate(text[PreviewLength - 1]) ? PreviewLength - 1 : PreviewLength)] + "…";

    public async Task<SupportMessageView> MessageAsync(Guid user, Guid id, Guid messageId, CancellationToken ct)
    {
        var actor = await Actor(user, null, ct);
        await Conversation(actor, id, ct);
        var message = await VisibleMessages(actor).AsNoTracking().SingleOrDefaultAsync(m => m.Id == messageId && m.ConversationId == id, ct)
            ?? throw Missing();
        return (await MessageViews(actor, id, [message], ct))[0];
    }

    public Task<bool> RecallAsync(Guid user, Guid id, Guid messageId, CancellationToken ct) =>
        RecallMessagesAsync(user, id, [messageId], ct);

    public Task<bool> RecallManyAsync(Guid user, Guid id, ChatMessageSelection request, CancellationToken ct) =>
        RecallMessagesAsync(user, id, Selection(request.MessageIds, 50), ct);

    private async Task<bool> RecallMessagesAsync(Guid user, Guid id, Guid[] ids, CancellationToken ct)
    {
        var actor = await Actor(user, null, ct);
        return await Mutate(async changed =>
        {
            var chat = await Conversation(actor, id, ct);
            var messages = await VisibleMessages(actor).Where(m => m.ConversationId == id &&
                m.SenderUserId == user && ids.Contains(m.Id)).ToArrayAsync(ct);
            if (messages.Length != ids.Length) throw Missing();
            var pending = messages.Where(m => m.RecalledAt is null).ToArray();
            var now = Now;
            foreach (var message in pending) message.Recall(now);
            if (pending.Length > 0) changed.Add(chat);
            return true;
        }, ct);
    }

    public async Task<bool> HideAsync(Guid user, Guid id, ChatMessageSelection request, CancellationToken ct)
    {
        var actor = await Actor(user, null, ct);
        var ids = Selection(request.MessageIds, 50);
        return await Mutate(async changed =>
        {
            var chat = await Conversation(actor, id, ct);
            var messages = await db.SupportMessages.Where(m => m.ConversationId == id && ids.Contains(m.Id) && m.Kind != "SYSTEM").ToArrayAsync(ct);
            if (messages.Length != ids.Length) throw Missing();
            foreach (var message in messages) message.HideFor(actor.Role);
            changed.Add(chat);
            return true;
        }, ct);
    }

    public async Task<bool> AcknowledgeAsync(Guid user, Guid id, ChatMessageReceipt request, CancellationToken ct)
    {
        var actor = await Actor(user, null, ct);
        var ids = Selection(request.MessageIds, 50);
        return await Mutate(async changed =>
        {
            var chat = await Conversation(actor, id, ct);
            var sender = actor.Role == "MANAGER" ? (Guid?)chat.ResidentUserId : chat.ManagerUserId;
            if (sender is null) return true;
            var messages = await VisibleMessages(actor).Where(m => m.ConversationId == id && ids.Contains(m.Id) &&
                m.SenderUserId == sender && m.RecalledAt == null && (request.Seen ? m.SeenAt == null : m.DeliveredAt == null)).ToArrayAsync(ct);
            var now = Now;
            foreach (var message in messages) message.Acknowledge(request.Seen, now);
            if (messages.Length > 0) changed.Add(chat);
            return true;
        }, ct);
    }

    public async Task<int> ForwardAsync(Guid user, Guid id, ChatForwardRequest request, CancellationToken ct)
    {
        var actor = await Actor(user, null, ct);
        if (request.Items is not { Length: > 0 and <= 20 } || request.Items.Any(item => item is null))
            throw Invalid("Chọn từ 1 đến 20 tin nhắn để chuyển tiếp.");
        var sources = Selection(request.Items.Select(item => item.MessageId).ToArray(), 20);
        var keys = Selection(request.Items.Select(item => item.ClientMessageId).ToArray(), 20);
        var requested = request.Items.ToDictionary(item => item.MessageId, item => item.ClientMessageId);
        return await Mutate(async changed =>
        {
            await Conversation(actor, id, ct);
            var destination = await Conversation(actor, request.DestinationId, ct, tracked: true);
            var existing = await db.SupportMessages.AsNoTracking().Where(m => m.SenderUserId == user && keys.Contains(m.ClientMessageId))
                .ToDictionaryAsync(m => m.ClientMessageId, ct);
            var replayedSources = existing.Values.Select(m => m.ForwardedFromId).ToArray();
            foreach (var item in request.Items)
                if (existing.TryGetValue(item.ClientMessageId, out var copy) &&
                    (copy.ConversationId != destination.Id || copy.ForwardedFromId != item.MessageId))
                    throw Conflict("Mã chuyển tiếp đã được dùng cho tin nhắn khác.");
            if (replayedSources.Length > 0 && await db.SupportMessages.CountAsync(m =>
                    m.ConversationId == id && replayedSources.Contains(m.Id), ct) != replayedSources.Length)
                throw Missing();
            if (existing.Count == keys.Length) return keys.Length;
            if (destination.Status == "CLOSED") throw Conflict("Hội thoại nhận đã kết thúc.");

            var remaining = sources.Where(source => !existing.ContainsKey(requested[source])).ToArray();
            var originals = await VisibleMessages(actor).AsNoTracking().Where(m => m.ConversationId == id && remaining.Contains(m.Id) &&
                m.RecalledAt == null && m.Kind != "SYSTEM").OrderBy(m => m.Sequence).ToArrayAsync(ct);
            if (originals.Length != remaining.Length) throw Missing();
            foreach (var original in originals)
                db.SupportMessages.Add(destination.Message(user, actor.Name, actor.Role, requested[original.Id], original.Text, Now,
                    forwardedFromId: original.Id));
            changed.Add(destination);
            return keys.Length;
        }, ct);
    }

    private static Guid[] Selection(Guid[]? ids, int limit)
    {
        if (ids is null || ids.Length < 1 || ids.Length > limit || ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            throw Invalid($"Chọn từ 1 đến {limit} tin nhắn khác nhau.");
        return ids;
    }
}
