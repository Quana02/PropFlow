using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PropFlow.Modules.Communication.Application.EmergencyCalls;
using PropFlow.Modules.Communication.Application.SupportChat;
using PropFlow.Modules.Communication.Contracts;

namespace PropFlow.Modules.Communication.Presentation;

public sealed class EmergencyFrameGate
{
    private readonly ConcurrentDictionary<string, (long Window, int Count)> windows = new();
    private readonly ConcurrentDictionary<string, long> heartbeats = new();
    public bool Allow(string connection)
    {
        var now = Environment.TickCount64 / 1000;
        var value = windows.AddOrUpdate(connection, (now, 1), (_, old) => old.Window == now ? (now, old.Count + 1) : (now, 1));
        return value.Count <= 40;
    }
    public bool Heartbeat(string connection)
    {
        var now = Environment.TickCount64;
        if (heartbeats.TryGetValue(connection, out var previous) && now - previous < 5000) return false;
        heartbeats[connection] = now; return true;
    }
    public void Remove(string connection) { windows.TryRemove(connection, out _); heartbeats.TryRemove(connection, out _); }
}

[Authorize(Policy = SupportChatPolicies.Use)]
public sealed class EmergencyCallHub(IEmergencyCalls calls, EmergencyFrameGate gate) : Hub
{
    private Guid Actor => Guid.TryParse(Context.User?.FindFirstValue("sub"), out var user) ? user : Guid.Empty;
    public override async Task OnConnectedAsync()
    {
        try { await calls.ConnectedAsync(Actor, Context.ConnectionId, Context.ConnectionAborted); }
        catch (SupportChatFailure) { Context.Abort(); return; }
        await base.OnConnectedAsync();
    }
    public override async Task OnDisconnectedAsync(Exception? exception)
    { gate.Remove(Context.ConnectionId); await calls.DisconnectedAsync(Context.ConnectionId, CancellationToken.None); await base.OnDisconnectedAsync(exception); }
    public Task Heartbeat() => gate.Heartbeat(Context.ConnectionId) ? Execute(() => calls.HeartbeatAsync(Actor, Context.ConnectionId, Context.ConnectionAborted)) : Task.CompletedTask;
    public Task Signal(EmergencySignal signal) => gate.Allow(Context.ConnectionId)
        ? Execute(() => calls.SignalAsync(Actor, Context.ConnectionId, signal, Context.ConnectionAborted)) : Task.CompletedTask;
    public Task MediaConnected(Guid id, int revision) => gate.Allow(Context.ConnectionId)
        ? Execute(() => calls.MediaConnectedAsync(Actor, Context.ConnectionId, id, revision, Context.ConnectionAborted)) : Task.CompletedTask;
    public Task MediaDisconnected(Guid id, int revision) => gate.Allow(Context.ConnectionId)
        ? Execute(() => calls.MediaDisconnectedAsync(Actor, Context.ConnectionId, id, revision, Context.ConnectionAborted)) : Task.CompletedTask;
    private static async Task Execute(Func<Task> action)
    { try { await action(); } catch (SupportChatFailure failure) { throw new HubException(failure.Message); } }
}
public sealed class EmergencyCallRealtime(IHubContext<EmergencyCallHub> hub) : IEmergencyCallRealtime
{
    public Task ChangedAsync(IReadOnlyCollection<Guid> users, CancellationToken ct) =>
        hub.Clients.Users(users.Select(x => x.ToString()).ToArray()).SendAsync("EmergencyChanged", ct);
    public Task SignalAsync(string connection, EmergencySignal signal, CancellationToken ct) =>
        hub.Clients.Client(connection).SendAsync("EmergencySignal", signal, ct);
}
