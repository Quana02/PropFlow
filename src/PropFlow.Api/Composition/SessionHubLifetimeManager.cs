using Microsoft.AspNetCore.SignalR;

namespace PropFlow.Api.Composition;

internal sealed class SessionHubLifetimeManager<THub>(
    ILogger<DefaultHubLifetimeManager<THub>> logger,
    SessionConnections connections) : DefaultHubLifetimeManager<THub>(logger) where THub : Hub
{
    private static readonly Type HubType = typeof(THub);

    public override async Task OnConnectedAsync(HubConnectionContext connection)
    {
        await base.OnConnectedAsync(connection);
        connections.Track(HubType, connection);
    }

    public override Task OnDisconnectedAsync(HubConnectionContext connection)
    {
        connections.Remove(HubType, connection.ConnectionId);
        return base.OnDisconnectedAsync(connection);
    }

    public override Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        if (connections.Find(HubType, connectionId) is { IsOpen: true } connection)
            connection.Groups.TryAdd(groupName, 0);
        return Task.CompletedTask;
    }

    public override Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        connections.Find(HubType, connectionId)?.Groups.TryRemove(groupName, out _);
        return Task.CompletedTask;
    }

    public override Task SendAllAsync(string methodName, object?[] args, CancellationToken cancellationToken = default) =>
        SendAsync(connections.ForHub(HubType), methodName, args, cancellationToken);

    public override Task SendAllExceptAsync(string methodName, object?[] args, IReadOnlyList<string> excludedConnectionIds,
        CancellationToken cancellationToken = default)
    {
        var excluded = excludedConnectionIds.ToHashSet(StringComparer.Ordinal);
        return SendAsync(connections.ForHub(HubType).Where(connection => !excluded.Contains(connection.Transport.ConnectionId)),
            methodName, args, cancellationToken);
    }

    public override Task SendConnectionAsync(string connectionId, string methodName, object?[] args,
        CancellationToken cancellationToken = default) =>
        SendConnectionsAsync([connectionId], methodName, args, cancellationToken);

    public override Task SendConnectionsAsync(IReadOnlyList<string> connectionIds, string methodName, object?[] args,
        CancellationToken cancellationToken = default) =>
        SendAsync(connectionIds.Distinct(StringComparer.Ordinal).Select(id => connections.Find(HubType, id))
            .OfType<SessionConnections.Connection>(), methodName, args, cancellationToken);

    public override Task SendUserAsync(string userId, string methodName, object?[] args,
        CancellationToken cancellationToken = default) =>
        SendUsersAsync([userId], methodName, args, cancellationToken);

    public override Task SendUsersAsync(IReadOnlyList<string> userIds, string methodName, object?[] args,
        CancellationToken cancellationToken = default)
    {
        var recipients = userIds.ToHashSet(StringComparer.Ordinal);
        return SendAsync(connections.ForHub(HubType)
            .Where(connection => connection.Transport.UserIdentifier is { } id && recipients.Contains(id)),
            methodName, args, cancellationToken);
    }

    public override Task SendGroupAsync(string groupName, string methodName, object?[] args,
        CancellationToken cancellationToken = default) =>
        SendGroupsAsync([groupName], methodName, args, cancellationToken);

    public override Task SendGroupsAsync(IReadOnlyList<string> groupNames, string methodName, object?[] args,
        CancellationToken cancellationToken = default) =>
        SendAsync(connections.ForHub(HubType).Where(connection => groupNames.Any(connection.Groups.ContainsKey)),
            methodName, args, cancellationToken,
            connection => groupNames.Any(connection.Groups.ContainsKey));

    public override Task SendGroupExceptAsync(string groupName, string methodName, object?[] args,
        IReadOnlyList<string> excludedConnectionIds, CancellationToken cancellationToken = default)
    {
        var excluded = excludedConnectionIds.ToHashSet(StringComparer.Ordinal);
        return SendAsync(connections.ForHub(HubType).Where(connection =>
                connection.Groups.ContainsKey(groupName) && !excluded.Contains(connection.Transport.ConnectionId)),
            methodName, args, cancellationToken, connection => connection.Groups.ContainsKey(groupName));
    }

    public override async Task<T> InvokeConnectionAsync<T>(string connectionId, string methodName, object?[] args,
        CancellationToken cancellationToken)
    {
        var connection = connections.Find(HubType, connectionId);
        if (connection is null || (await connections.KeepCurrentAsync([connection], cancellationToken)).Count == 0
            || !connection.IsOpen)
            throw new HubException("Kết nối không còn hiệu lực.");
        return await base.InvokeConnectionAsync<T>(connectionId, methodName, args, cancellationToken);
    }

    private async Task SendAsync(IEnumerable<SessionConnections.Connection> candidates, string method, object?[] args,
        CancellationToken ct, Func<SessionConnections.Connection, bool>? stillInGroup = null)
    {
        var current = await connections.KeepCurrentAsync(candidates, ct);
        var ids = current.Where(connection => connection.IsOpen && (stillInGroup?.Invoke(connection) ?? true))
            .Select(connection => connection.Transport.ConnectionId).ToArray();
        if (ids.Length > 0) await base.SendConnectionsAsync(ids, method, args, ct);
    }
}
