using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PropFlow.Modules.Communication.Application.SupportChat;
using PropFlow.Modules.Communication.Contracts;
using PropFlow.Modules.Communication.Domain.SupportChat;
using PropFlow.Modules.Communication.Infrastructure.Persistence;

namespace PropFlow.Modules.Communication.Infrastructure;

public sealed class SupportChatService(CommunicationDbContext db, ISupportChatActors actors, ISupportChatRealtime realtime,
    TimeProvider clock, ILogger<SupportChatService> logger) : ISupportChat
{
    public const string Greeting = "Xin chào! Bạn cần Ban quản lý hỗ trợ việc gì? Hãy mô tả để mình kết nối với Manager đang sẵn sàng.";
    private DateTimeOffset Now => clock.GetUtcNow();
    private async Task<SupportActor> Actor(Guid id, string? role, CancellationToken ct)
    {
        var actor = await actors.FindAsync(id, ct);
        if (actor is null || actor.Role is not ("RESIDENT" or "MANAGER") || (role is not null && actor.Role != role) ||
            (actor.Role == "RESIDENT" && actor.ResidentId is null))
            throw new SupportChatFailure(403, "support_chat_forbidden", "Tài khoản không có quyền hoặc liên kết cư trú hợp lệ để dùng chat BQL.");
        return actor;
    }
    private IQueryable<SupportConversation> Scoped(SupportActor actor) => actor.Role == "MANAGER"
        ? db.SupportConversations.Where(x => x.ManagerUserId == actor.UserId)
        : db.SupportConversations.Where(x => x.ResidentUserId == actor.UserId && x.ResidentId == actor.ResidentId);
    private async Task<SupportConversation> Conversation(SupportActor actor, Guid id, CancellationToken ct, bool tracked = false) =>
        await (tracked ? Scoped(actor) : Scoped(actor).AsNoTracking()).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
    public async Task<SupportConversationPage> ListAsync(Guid user, int page, CancellationToken ct)
    {
        var actor = await Actor(user, null, ct);
        if (page < 1 || page > 100000) throw Invalid("Trang danh sách không hợp lệ.");
        var query = Scoped(actor).AsNoTracking(); var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Status == "CLOSED").ThenByDescending(x => x.UpdatedAt).ThenBy(x => x.Id)
            .Skip((page - 1) * 12).Take(12).ToArrayAsync(ct);
        var result = new List<SupportConversationView>(); foreach (var row in rows) result.Add(await View(row, ct));
        return new(result, total, page, actor.TimeZoneId);
    }
    public async Task<SupportConversationView> GetAsync(Guid user, Guid id, CancellationToken ct) =>
        await View(await Conversation(await Actor(user, null, ct), id, ct), ct);
    public async Task<SupportMessagePage> MessagesAsync(Guid user, Guid id, long? before, CancellationToken ct)
    {
        var actor = await Actor(user, null, ct); await Conversation(actor, id, ct);
        if (before <= 0) throw Invalid("Mốc tin nhắn không hợp lệ.");
        var rows = await db.SupportMessages.AsNoTracking().Where(x => x.ConversationId == id && (before == null || x.Sequence < before))
            .OrderByDescending(x => x.Sequence).Take(51).ToArrayAsync(ct);
        return new(rows.Take(50).Reverse().Select(MessageView).ToArray(), rows.Length > 50, actor.TimeZoneId);
    }
    public async Task<SupportConversationView> StartAsync(Guid user, ChatSendRequest input, CancellationToken ct)
    {
        var actor = await Actor(user, "RESIDENT", ct); var text = Validate(input);
        return await Mutate(async changed => {
            var replay = await Replay(actor, null, input.ClientMessageId, text, ct);
            if (replay is not null) return await View(await Conversation(actor, replay.ConversationId, ct), ct);
            var chat = await db.SupportConversations.SingleOrDefaultAsync(x => x.ResidentUserId == user && x.Status != "CLOSED", ct);
            if (chat is not null && chat.ResidentId != actor.ResidentId) throw Conflict("Liên kết cư trú đã thay đổi. Vui lòng liên hệ Ban quản lý để kiểm tra phiên hỗ trợ cũ.");
            if (chat is null)
            {
                chat = new(user, actor.ResidentId!.Value, actor.Name, Now); db.SupportConversations.Add(chat);
                System(chat, Greeting);
            }
            db.SupportMessages.Add(chat.Message(user, actor.Name, "RESIDENT", input.ClientMessageId, text, Now));
            await db.SaveChangesAsync(ct);
            await DispatchLocked(changed, ct); changed.Add(chat);
            return await View(chat, ct);
        }, ct);
    }
    public async Task<SupportMessageView> SendAsync(Guid user, Guid id, ChatSendRequest input, CancellationToken ct)
    {
        var actor = await Actor(user, null, ct); var text = Validate(input);
        return await Mutate(async changed => {
            var chat = await Conversation(actor, id, ct, tracked: true);
            var replay = await Replay(actor, id, input.ClientMessageId, text, ct);
            if (replay is not null) return MessageView(replay);
            if (chat.Status == "CLOSED") throw Conflict("Cuộc trò chuyện đã kết thúc. Hãy bắt đầu phiên mới nếu cần hỗ trợ thêm.");
            var message = chat.Message(user, actor.Name, actor.Role, input.ClientMessageId, text, Now);
            db.SupportMessages.Add(message); changed.Add(chat); return MessageView(message);
        }, ct);
    }
    public async Task<SupportConversationView> CloseAsync(Guid user, Guid id, CancellationToken ct)
    {
        var actor = await Actor(user, null, ct);
        return await Mutate(async changed => {
            var chat = await Conversation(actor, id, ct, tracked: true);
            if (chat.Status != "CLOSED")
            {
                System(chat, actor.Role == "MANAGER" ? "Manager đã kết thúc cuộc trò chuyện." : "Cư dân đã kết thúc cuộc trò chuyện.");
                chat.Close(Now); changed.Add(chat); await db.SaveChangesAsync(ct); await DispatchLocked(changed, ct);
            }
            return await View(chat, ct);
        }, ct);
    }
    public async Task<SupportAvailabilityView> AvailabilityAsync(Guid user, CancellationToken ct)
    {
        await Actor(user, "MANAGER", ct);
        var presence = await db.SupportManagerPresence.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user, ct);
        var connected = await Online(user, ct);
        var active = await db.SupportConversations.AsNoTracking().Where(x => x.ManagerUserId == user && x.Status == "ACTIVE")
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        return new(presence?.Available == true && connected, connected, active);
    }
    public async Task<SupportAvailabilityView> SetAvailabilityAsync(Guid user, bool available, CancellationToken ct)
    {
        await Actor(user, "MANAGER", ct);
        await Mutate(async changed => {
            if (available && !await Online(user, ct)) throw Conflict("Kết nối chat chưa sẵn sàng. Vui lòng chờ kết nối lại rồi bật nhận chat.");
            var presence = await db.SupportManagerPresence.SingleOrDefaultAsync(x => x.UserId == user, ct);
            if (presence is null) { presence = new(user); db.SupportManagerPresence.Add(presence); }
            presence.SetAvailable(available); await db.SaveChangesAsync(ct); await DispatchLocked(changed, ct); return true;
        }, ct);
        return await AvailabilityAsync(user, ct);
    }
    public async Task ConnectedAsync(Guid user, string connection, CancellationToken ct)
    {
        var actor = await Actor(user, null, ct);
        if (actor.Role != "MANAGER") return;
        await Mutate(async changed => {
            var row = await db.SupportManagerConnections.SingleOrDefaultAsync(x => x.Id == connection, ct);
            if (row is null) db.SupportManagerConnections.Add(new(connection, user, Now));
            else if (row.ManagerUserId == user) row.Renew(Now);
            else throw Missing();
            if (row is null) changed.AddRange(await db.SupportConversations.Where(x => x.ManagerUserId == user && x.Status == "ACTIVE").ToArrayAsync(ct));
            return true;
        }, ct);
    }
    public Task HeartbeatAsync(Guid user, string connection, CancellationToken ct) => ConnectedAsync(user, connection, ct);
    public async Task DisconnectedAsync(string connection, CancellationToken ct)
    {
        await Mutate(async changed => {
            var row = await db.SupportManagerConnections.SingleOrDefaultAsync(x => x.Id == connection, ct);
            if (row is null) return true;
            var user = row.ManagerUserId; db.SupportManagerConnections.Remove(row); await db.SaveChangesAsync(ct);
            if (!await Online(user, ct))
            {
                var presence = await db.SupportManagerPresence.SingleOrDefaultAsync(x => x.UserId == user, ct); presence?.SetAvailable(false);
                changed.AddRange(await db.SupportConversations.Where(x => x.ManagerUserId == user && x.Status == "ACTIVE").ToArrayAsync(ct));
            }
            return true;
        }, ct);
    }
    public async Task DispatchAsync(CancellationToken ct) => await Mutate(async changed => { await DispatchLocked(changed, ct); return true; }, ct);
    private async Task DispatchLocked(List<SupportConversation> changed, CancellationToken ct)
    {
        var expired = await db.SupportManagerConnections.Where(x => x.ExpiresAt <= Now).ToArrayAsync(ct);
        db.SupportManagerConnections.RemoveRange(expired); await db.SaveChangesAsync(ct);
        for (var count = 0; count < 20; count++)
        {
            var chat = await db.SupportConversations.Where(x => x.Status == "WAITING").OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
            if (chat is null) break;
            var resident = await actors.FindAsync(chat.ResidentUserId, ct);
            if (resident is not { Role: "RESIDENT" } || resident.ResidentId != chat.ResidentId)
            { System(chat, "Phiên hỗ trợ kết thúc do liên kết cư trú hoặc quyền tài khoản đã thay đổi."); chat.Close(Now); changed.Add(chat); await db.SaveChangesAsync(ct); continue; }
            var presence = await db.SupportManagerPresence.Where(p => p.Available &&
                db.SupportManagerConnections.Any(c => c.ManagerUserId == p.UserId && c.ExpiresAt > Now) &&
                !db.SupportConversations.Any(c => c.ManagerUserId == p.UserId && c.Status == "ACTIVE"))
                .OrderBy(p => p.LastAssignedAt != null).ThenBy(p => p.LastAssignedAt).ThenBy(p => p.UserId).FirstOrDefaultAsync(ct);
            if (presence is null) break;
            var manager = await actors.FindAsync(presence.UserId, ct);
            if (manager is not { Role: "MANAGER" }) { presence.SetAvailable(false); await db.SaveChangesAsync(ct); continue; }
            chat.Assign(manager.UserId, manager.Name, Now); presence.Assigned(Now);
            System(chat, $"Đã kết nối với {manager.Name} — Ban quản lý. Bạn có thể trao đổi trực tiếp tại đây.");
            changed.Add(chat); await db.SaveChangesAsync(ct);
        }
    }
    public async Task DraftAsync(Guid user, Guid id, string stream, long sequence, string text, CancellationToken ct)
    {
        var actor = await Actor(user, "RESIDENT", ct); var chat = await Conversation(actor, id, ct);
        if (text is null || text.Length > 4000 || sequence < 0 || text.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t')) throw Invalid("Bản nháp không hợp lệ.");
        if (chat.Status != "ACTIVE" || chat.ManagerUserId is not Guid manager) return;
        if (!await Online(manager, ct) || (await actors.FindAsync(manager, ct))?.Role != "MANAGER") return;
        await realtime.DraftAsync(manager, new(id, stream, sequence, text, Now), ct);
    }
    public async Task TypingAsync(Guid user, Guid id, bool typing, CancellationToken ct)
    {
        var actor = await Actor(user, "MANAGER", ct); var chat = await Conversation(actor, id, ct);
        if (chat.Status != "ACTIVE") return;
        if ((await actors.FindAsync(chat.ResidentUserId, ct))?.ResidentId != chat.ResidentId) return;
        await realtime.TypingAsync(chat.ResidentUserId, new(id, typing, Now), ct);
    }
    private Task<bool> Online(Guid user, CancellationToken ct) => db.SupportManagerConnections.AnyAsync(x => x.ManagerUserId == user && x.ExpiresAt > Now, ct);
    private async Task<SupportConversationView> View(SupportConversation chat, CancellationToken ct)
    {
        var online = chat.Status == "ACTIVE" && chat.ManagerUserId is Guid manager && await Online(manager, ct) && (await actors.FindAsync(manager, ct))?.Role == "MANAGER";
        return new(chat.Id, chat.Status, chat.ResidentName, chat.ManagerName, online, chat.CreatedAt, chat.UpdatedAt);
    }
    private void System(SupportConversation chat, string text) => db.SupportMessages.Add(chat.Message(null, "Trợ lý tiếp nhận", "SYSTEM", Guid.NewGuid(), text, Now));
    private static SupportMessageView MessageView(SupportMessage m) => new(m.Id, m.Sequence, m.Kind, m.SenderName, m.Text, m.CreatedAt);
    private async Task<SupportMessage?> Replay(SupportActor actor, Guid? conversation, Guid key, string text, CancellationToken ct)
    {
        var message = await db.SupportMessages.AsNoTracking().SingleOrDefaultAsync(x => x.SenderUserId == actor.UserId && x.ClientMessageId == key, ct);
        if (message is not null && (message.Text != text || (conversation is not null && message.ConversationId != conversation)))
            throw Conflict("Mã gửi tin đã được dùng với nội dung khác. Vui lòng kiểm tra lại cuộc trò chuyện.");
        return message;
    }
    private async Task<T> Mutate<T>(Func<List<SupportConversation>, Task<T>> operation, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        if (tx is not null) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71241013)", ct);
        var changed = new List<SupportConversation>();
        var result = await operation(changed); await db.SaveChangesAsync(ct); if (tx is not null) await tx.CommitAsync(ct);
        foreach (var chat in changed.DistinctBy(x => x.Id))
        {
            var recipients = new List<Guid> { chat.ResidentUserId }; if (chat.ManagerUserId is Guid manager) recipients.Add(manager);
            try { await realtime.ChangedAsync(chat.Id, recipients, ct); }
            catch (Exception e) when (e is not OperationCanceledException) { logger.LogWarning("Support chat realtime hint unavailable ({Kind}).", e.GetType().Name); }
        }
        return result;
    }
    private static string Validate(ChatSendRequest request)
    {
        if (request.ClientMessageId == Guid.Empty) throw Invalid("Thiếu mã gửi tin nhắn.");
        try { return SupportConversation.ValidateText(request.Text); } catch (ArgumentException e) { throw Invalid(e.Message); }
    }
    private static SupportChatFailure Missing() => new(404, "support_chat_not_found", "Không tìm thấy cuộc trò chuyện hoặc bạn không phải người tham gia.");
    private static SupportChatFailure Invalid(string message) => new(400, "support_chat_invalid", message);
    private static SupportChatFailure Conflict(string message) => new(409, "support_chat_conflict", message);
}
