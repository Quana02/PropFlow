using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.SignalR.Client;
using PropFlow.Web.Client.Services.Authentication;

namespace PropFlow.Web.Client.Features.RemoteOnlineSign.Services;

public sealed record RemoteContractStatusChanged(Guid ContractId, string PreviousStatus, string NewStatus, int Revision, DateTimeOffset UpdatedAtUtc);

public sealed class RemoteOnlineSignRealtimeClient : IAsyncDisposable
{
    private readonly IConfiguration configuration;
    private readonly NavigationManager navigation;
    private readonly AuthSession session;
    private readonly ILogger<RemoteOnlineSignRealtimeClient> logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private HubConnection? connection;
    private SessionIdentity? identity;
    private bool disposed, requested;
    public event Func<RemoteContractStatusChanged, Task>? Changed;

    public RemoteOnlineSignRealtimeClient(IConfiguration configuration, NavigationManager navigation, AuthSession session, ILogger<RemoteOnlineSignRealtimeClient> logger)
    {
        this.configuration = configuration;
        this.navigation = navigation;
        this.session = session;
        this.logger = logger;
        session.AuthenticationStateChanged += HandleAuthenticationStateChanged;
    }

    public async Task EnsureStartedAsync(CancellationToken ct = default)
    {
        if (disposed) return;
        requested = true;
        await session.InitializeAsync();
        if (CurrentIdentity() is null) return;
        await gate.WaitAsync(ct);
        try
        {
            if (disposed || CurrentIdentity() is null || connection?.State is HubConnectionState.Connected or HubConnectionState.Connecting or HubConnectionState.Reconnecting) return;
            if (connection is null || identity != CurrentIdentity())
            {
                await DisposeConnectionAsync();
                connection = CreateConnection();
                identity = CurrentIdentity();
            }
            await connection.StartAsync(ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Remote contract realtime connection is unavailable.");
        }
        finally { gate.Release(); }
    }

    private HubConnection CreateConnection()
    {
        var configuredApiBase = configuration["Api:BaseUrl"] ?? throw new InvalidOperationException("Thiếu cấu hình Api:BaseUrl.");
        var apiBase = new Uri(new Uri(navigation.BaseUri), configuredApiBase);
        var created = new HubConnectionBuilder()
            .WithUrl(new Uri(apiBase, "hubs/remote-online-sign"), options => options.AccessTokenProvider = session.GetValidAccessTokenAsync)
            .WithAutomaticReconnect()
            .Build();
        session.ObserveTransport(created);
        created.On<RemoteContractStatusChanged>("ContractStatusChanged", DispatchAsync);
        created.Reconnected += async _ => await ReconcileAsync();
        created.Closed += async _ => { if (!disposed && requested) await EnsureStartedAsync(lifetime.Token); };
        return created;
    }

    private async Task DispatchAsync(RemoteContractStatusChanged change)
    {
        var handlers = Changed;
        if (handlers is null || disposed) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Func<RemoteContractStatusChanged, Task>>())
        {
            try { await handler(change); }
            catch (Exception exception) { logger.LogWarning(exception, "Remote contract realtime refresh failed."); }
        }
    }

    private Task ReconcileAsync() => DispatchAsync(new(Guid.Empty, string.Empty, string.Empty, 0, DateTimeOffset.UtcNow));
    private void HandleAuthenticationStateChanged(Task<AuthenticationState> authenticationState)
    {
        _ = ResetForSessionAsync();
    }
    private async Task ResetForSessionAsync()
    {
        try
        {
            await gate.WaitAsync(lifetime.Token);
            try { await DisposeConnectionAsync(); }
            finally { gate.Release(); }
            if (!disposed && requested && CurrentIdentity() is not null)
                await EnsureStartedAsync(lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception) { logger.LogWarning(exception, "Remote contract realtime session reset failed."); }
    }
    private SessionIdentity? CurrentIdentity()
    {
        var user = session.User;
        return user is not null && user.Role is "ADMIN" or "RESIDENT" ? new(user.Id, user.Role) : null;
    }

    private async Task DisposeConnectionAsync()
    {
        var current = connection; connection = null; identity = null;
        if (current is null) return;
        try { if (current.State != HubConnectionState.Disconnected) await current.StopAsync(); }
        catch (Exception exception) { logger.LogDebug(exception, "Remote contract realtime connection could not stop cleanly."); }
        await current.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        session.AuthenticationStateChanged -= HandleAuthenticationStateChanged;
        await lifetime.CancelAsync();
        await gate.WaitAsync();
        try { await DisposeConnectionAsync(); }
        finally { gate.Release(); gate.Dispose(); lifetime.Dispose(); Changed = null; }
    }

    private readonly record struct SessionIdentity(Guid UserId, string Role);
}
