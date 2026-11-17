using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Infrastructure;

namespace PropFlow.Api.Composition;

public interface IRealtimeSessionValidation
{
    Task<HashSet<ClaimsPrincipal>> CurrentAsync(IReadOnlyCollection<ClaimsPrincipal> principals, CancellationToken ct);
}

internal sealed class RealtimeSessionValidation(IServiceScopeFactory scopeFactory) : IRealtimeSessionValidation
{
    public async Task<HashSet<ClaimsPrincipal>> CurrentAsync(IReadOnlyCollection<ClaimsPrincipal> principals, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AccessTokenStateValidator>().CurrentAsync(principals, ct);
    }
}

internal sealed class SessionConnections(
    IRealtimeSessionValidation validation,
    ILogger<SessionConnections> logger) : IAccountSessionInvalidation
{
    private static readonly Meter Meter = new("PropFlow.Authorization");
    private readonly ConcurrentDictionary<(Type Hub, string Id), Connection> connections = new();
    private static readonly UpDownCounter<int> ActiveConnections = Meter.CreateUpDownCounter<int>("propflow.signalr.connections");
    private static readonly Counter<int> RevokedConnections = Meter.CreateCounter<int>("propflow.signalr.revocations");

    internal sealed class Connection(HubConnectionContext transport)
    {
        private int revoked;
        internal HubConnectionContext Transport { get; } = transport;
        internal ClaimsPrincipal Principal => Transport.User;
        internal AccessSessionKey? Session { get; } = AccessTokenStateValidator.GetKey(transport.User);
        internal DateTimeOffset ConnectedAt { get; } = DateTimeOffset.UtcNow;
        internal long? ExpiresAtUnixSeconds { get; } = long.TryParse(transport.User.FindFirst("exp")?.Value, out var expiration)
            ? expiration : null;
        internal ConcurrentDictionary<string, byte> Groups { get; } = new(StringComparer.Ordinal);
        internal bool IsOpen => Volatile.Read(ref revoked) == 0 && !Transport.ConnectionAborted.IsCancellationRequested;

        internal void Revoke()
        {
            if (Interlocked.Exchange(ref revoked, 1) != 0) return;
            Groups.Clear();
            RevokedConnections.Add(1);
            Transport.Abort();
        }
    }

    internal void Track(Type hub, HubConnectionContext transport)
    {
        if (connections.TryAdd((hub, transport.ConnectionId), new(transport))) ActiveConnections.Add(1);
    }

    internal void Remove(Type hub, string id)
    {
        if (connections.TryRemove((hub, id), out _)) ActiveConnections.Add(-1);
    }

    internal Connection? Find(Type hub, string id) => connections.GetValueOrDefault((hub, id));

    internal IEnumerable<Connection> ForHub(Type hub) => connections
        .Where(pair => pair.Key.Hub == hub).Select(pair => pair.Value);

    public void Invalidate(Guid userId, Guid sessionId)
    {
        foreach (var connection in connections.Values)
            if (connection.Session is { } key && key.UserId == userId && key.SessionId == sessionId)
                connection.Revoke();
    }

    public Task RecheckAsync(Guid userId, CancellationToken ct) =>
        KeepCurrentAsync(connections.Values.Where(connection => connection.Session?.UserId == userId), ct);

    internal async Task<IReadOnlyList<Connection>> KeepCurrentAsync(IEnumerable<Connection> candidates, CancellationToken ct)
    {
        var valid = new List<Connection>();
        foreach (var batch in candidates.Where(connection => connection.IsOpen).Chunk(256))
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var connection in batch)
                if (connection.ExpiresAtUnixSeconds is long expiry && expiry <= now)
                    connection.Revoke();
            var open = batch.Where(connection => connection.IsOpen).ToArray();
            if (open.Length == 0) continue;
            HashSet<ClaimsPrincipal> current;
            try
            {
                current = await validation.CurrentAsync(open.Select(connection => connection.Principal).ToArray(), ct);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogWarning("Không thể xác minh quyền nhận SignalR ({ErrorType}).", error.GetType().Name);
                continue;
            }

            foreach (var connection in open)
            {
                if (!current.Contains(connection.Principal)) connection.Revoke();
                else if (connection.IsOpen) valid.Add(connection);
            }
        }
        return valid;
    }

    internal Task ReconcileAsync(CancellationToken ct) => KeepCurrentAsync(connections.Values, ct);
}

internal sealed class SessionConnectionReconciliation(SessionConnections connections) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await connections.ReconcileAsync(stoppingToken);
    }
}

internal sealed class SessionHubFilter(SessionConnections connections) : IHubFilter
{
    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        if (await IsCurrentAsync(context.Hub.GetType(), context.Context)) await next(context);
    }

    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (!await IsCurrentAsync(context.Hub.GetType(), context.Context))
            throw new HubException("Phiên đăng nhập không còn hiệu lực.");
        return await next(context);
    }

    private async Task<bool> IsCurrentAsync(Type hub, HubCallerContext caller)
    {
        var connection = connections.Find(hub, caller.ConnectionId);
        if (connection is not null
            && (await connections.KeepCurrentAsync([connection], caller.ConnectionAborted)).Count == 1)
            return true;
        caller.Abort();
        return false;
    }
}
