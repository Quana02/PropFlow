using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PropFlow.Modules.Communication.Application.SupportChat;
using PropFlow.Modules.Communication.Contracts;

namespace PropFlow.Modules.Communication.Presentation;

// Transient metadata only. Draft text is never cached or logged on the server.
public sealed class SupportChatFrameGate
{
    private sealed class Frame { public long Sequence = -1; public long At; public long TypingAt; public long HeartbeatAt; }
    private readonly ConcurrentDictionary<string, Frame> frames = new();
    public bool Draft(string connection, long sequence, bool clear)
    {
        var frame = frames.GetOrAdd(connection, _ => new());
        lock (frame)
        {
            if (sequence < 0 || sequence <= frame.Sequence) return false;
            frame.Sequence = sequence; var now = Environment.TickCount64;
            if (!clear && frame.At != 0 && now - frame.At < 200) return false;
            frame.At = now; return true;
        }
    }
    public bool Typing(string connection, bool clear = false)
    {
        var frame = frames.GetOrAdd(connection, _ => new());
        lock (frame) { var now = Environment.TickCount64; if (!clear && frame.TypingAt != 0 && now - frame.TypingAt < 300) return false; frame.TypingAt = now; return true; }
    }
    public bool Heartbeat(string connection)
    { var frame = frames.GetOrAdd(connection, _ => new()); lock (frame) { var now = Environment.TickCount64; if (frame.HeartbeatAt != 0 && now - frame.HeartbeatAt < 5000) return false; frame.HeartbeatAt = now; return true; } }
    public void Remove(string connection) => frames.TryRemove(connection, out _);
}

[Authorize(Policy = SupportChatPolicies.Use)]
public sealed class SupportChatHub(ISupportChat chat, SupportChatFrameGate gate) : Hub
{
    private Guid Actor => Guid.TryParse(Context.User?.FindFirstValue("sub"), out var user) ? user : Guid.Empty;
    public override async Task OnConnectedAsync()
    {
        try { await chat.ConnectedAsync(Actor, Context.ConnectionId, Context.ConnectionAborted); }
        catch (SupportChatFailure) { Context.Abort(); return; }
        await base.OnConnectedAsync();
    }
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        gate.Remove(Context.ConnectionId);
        await chat.DisconnectedAsync(Context.ConnectionId, CancellationToken.None);
        await base.OnDisconnectedAsync(exception);
    }
    public Task Heartbeat() => gate.Heartbeat(Context.ConnectionId) ? Execute(() => chat.HeartbeatAsync(Actor, Context.ConnectionId, Context.ConnectionAborted)) : Task.CompletedTask;
    [Authorize(Policy = SupportChatPolicies.Resident)]
    public Task ResidentDraft(Guid conversationId, long sequence, string text)
    {
        if (text is null || text.Length > 4000) throw new HubException("Bản nháp không hợp lệ.");
        return gate.Draft(Context.ConnectionId, sequence, text.Length == 0)
            ? Execute(() => chat.DraftAsync(Actor, conversationId, Context.ConnectionId, sequence, text, Context.ConnectionAborted)) : Task.CompletedTask;
    }
    [Authorize(Policy = SupportChatPolicies.Manager)]
    public Task ManagerTyping(Guid conversationId, bool typing) => gate.Typing(Context.ConnectionId, !typing)
        ? Execute(() => chat.TypingAsync(Actor, conversationId, typing, Context.ConnectionAborted)) : Task.CompletedTask;
    private static async Task Execute(Func<Task> action)
    { try { await action(); } catch (SupportChatFailure failure) { throw new HubException(failure.Message); } }
}

public sealed class SupportChatRealtime(IHubContext<SupportChatHub> hub) : ISupportChatRealtime
{
    public Task ChangedAsync(Guid conversationId, IReadOnlyCollection<Guid> users, CancellationToken ct) =>
        hub.Clients.Users(users.Select(x => x.ToString()).ToArray()).SendAsync("SupportChatChanged", conversationId, ct);
    public Task DraftAsync(Guid recipient, SupportDraft draft, CancellationToken ct) =>
        hub.Clients.User(recipient.ToString()).SendAsync("SupportChatDraft", draft, ct);
    public Task TypingAsync(Guid recipient, SupportTyping typing, CancellationToken ct) =>
        hub.Clients.User(recipient.ToString()).SendAsync("SupportChatTyping", typing, ct);
}
