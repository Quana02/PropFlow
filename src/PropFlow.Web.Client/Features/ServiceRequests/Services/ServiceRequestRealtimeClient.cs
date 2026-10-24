using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.SignalR.Client;
using PropFlow.Web.Client.Services.Authentication;

namespace PropFlow.Web.Client.Features.ServiceRequests.Services;

public enum ServiceRequestRealtimeState
{
    Inactive,
    Connecting,
    Connected,
    Reconnecting,
    Disconnected
}

public sealed class ServiceRequestRealtimeClient : IAsyncDisposable
{
    private const string UpdatedEvent = "ServiceRequestUpdated";
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    ];
    private readonly IConfiguration _configuration;
    private readonly NavigationManager _navigation;
    private readonly AuthSession _session;
    private readonly ILogger<ServiceRequestRealtimeClient> _logger;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly RealtimeReconciliationPulse _reconciliation;
    private HubConnection? _connection;
    private SessionIdentity? _connectionIdentity;
    private bool _disposed;
    private int _retryScheduled;
    private bool _startRequested;

    public ServiceRequestRealtimeState State { get; private set; } = ServiceRequestRealtimeState.Inactive;
    public event Action<ServiceRequestRealtimeState>? ConnectionStateChanged;

    public ServiceRequestRealtimeClient(
        IConfiguration configuration,
        NavigationManager navigation,
        AuthSession session,
        ILogger<ServiceRequestRealtimeClient> logger)
    {
        _configuration = configuration;
        _navigation = navigation;
        _session = session;
        _logger = logger;
        _reconciliation = new(ReconcileAsync);
        _session.AuthenticationStateChanged += HandleAuthenticationStateChanged;
    }

    public event Func<Guid, Task>? Changed;

    public async Task EnsureStartedAsync(CancellationToken ct = default)
    {
        if (_disposed) return;
        _startRequested = true;
        _reconciliation.Start();
        await _session.InitializeAsync();
        if (CurrentSessionIdentity() is null)
        {
            SetState(ServiceRequestRealtimeState.Inactive);
            return;
        }

        await _connectionGate.WaitAsync(ct);
        try
        {
            if (_disposed) return;
            var identity = CurrentSessionIdentity();
            if (identity is null) return;
            if (_connection?.State == HubConnectionState.Connected && _connectionIdentity == identity)
            {
                SetState(ServiceRequestRealtimeState.Connected);
                return;
            }
            if (_connectionIdentity == identity &&
                _connection?.State is HubConnectionState.Connecting or HubConnectionState.Reconnecting) return;

            if (_connection is not null && _connectionIdentity != identity)
                await DisposeConnectionAsync();

            _connection ??= CreateConnection();
            _connectionIdentity = identity;
            SetState(ServiceRequestRealtimeState.Connecting);
            await _connection.StartAsync(ct);
            SetState(ServiceRequestRealtimeState.Connected);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetState(ServiceRequestRealtimeState.Disconnected);
            _logger.LogWarning(exception, "Service Request realtime connection is unavailable.");
            ScheduleRetry();
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private void ScheduleRetry()
    {
        if (_disposed || !_startRequested || CurrentSessionIdentity() is null ||
            Interlocked.CompareExchange(ref _retryScheduled, 1, 0) != 0) return;
        _ = RetryUntilConnectedAsync();
    }

    private async Task RetryUntilConnectedAsync()
    {
        try
        {
            var attempt = 0;
            while (!_disposed && CurrentSessionIdentity() is not null &&
                   _connection?.State != HubConnectionState.Connected)
            {
                if (_connection?.State is HubConnectionState.Connecting or HubConnectionState.Reconnecting)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), _lifetime.Token);
                    continue;
                }

                var delay = RetryDelays[Math.Min(attempt, RetryDelays.Length - 1)];
                await Task.Delay(delay, _lifetime.Token);
                if (_disposed || CurrentSessionIdentity() is null) return;

                await EnsureStartedAsync(_lifetime.Token);
                attempt++;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            Volatile.Write(ref _retryScheduled, 0);
            if (!_disposed && _startRequested && CurrentSessionIdentity() is not null &&
                (_connection is null || _connection.State == HubConnectionState.Disconnected))
                ScheduleRetry();
        }
    }

    private HubConnection CreateConnection()
    {
        var configuredApiBase = _configuration["Api:BaseUrl"]
            ?? throw new InvalidOperationException("Thiếu cấu hình Api:BaseUrl.");
        var apiBase = new Uri(new Uri(_navigation.BaseUri), configuredApiBase);
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(apiBase, "hubs/service-requests"), options =>
                options.AccessTokenProvider = GetAccessTokenAsync)
            .WithAutomaticReconnect()
            .Build();

        connection.On<Guid>(UpdatedEvent, DispatchAsync);
        connection.Reconnecting += exception => HandleReconnectingAsync(connection, exception);
        connection.Reconnected += connectionId => HandleReconnectedAsync(connection, connectionId);
        connection.Closed += exception => HandleClosedAsync(connection, exception);
        return connection;
    }

    private Task HandleReconnectingAsync(HubConnection source, Exception? exception)
    {
        if (!_disposed && ReferenceEquals(_connection, source))
        {
            SetState(ServiceRequestRealtimeState.Reconnecting);
            _logger.LogWarning(exception, "Service Request realtime connection is reconnecting.");
        }
        return Task.CompletedTask;
    }

    private async Task HandleReconnectedAsync(HubConnection source, string? connectionId)
    {
        if (_disposed || !ReferenceEquals(_connection, source)) return;
        SetState(ServiceRequestRealtimeState.Connected);
        _logger.LogInformation("Service Request realtime connection recovered as {ConnectionId}.", connectionId);
        await DispatchAsync(Guid.Empty);
    }

    private Task HandleClosedAsync(HubConnection source, Exception? exception)
    {
        if (_disposed || !ReferenceEquals(_connection, source)) return Task.CompletedTask;
        SetState(ServiceRequestRealtimeState.Disconnected);
        _logger.LogWarning(exception, "Service Request realtime connection closed; background recovery was scheduled.");
        ScheduleRetry();
        return Task.CompletedTask;
    }

    private async Task<string?> GetAccessTokenAsync()
    {
        return await _session.GetValidAccessTokenAsync();
    }

    private void HandleAuthenticationStateChanged(Task<AuthenticationState> authenticationState) =>
        _ = ResetConnectionForSessionAsync();

    private async Task ResetConnectionForSessionAsync()
    {
        try
        {
            var restart = false;
            await _connectionGate.WaitAsync(_lifetime.Token);
            try
            {
                if (_disposed) return;
                var identity = CurrentSessionIdentity();
                if (_connectionIdentity == identity) return;

                await DisposeConnectionAsync();
                restart = identity is not null && _startRequested;
                SetState(identity is null
                    ? ServiceRequestRealtimeState.Inactive
                    : ServiceRequestRealtimeState.Disconnected);
            }
            finally
            {
                _connectionGate.Release();
            }

            if (restart)
                await EnsureStartedAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not reset the Service Request realtime connection after the session changed.");
        }
    }

    private SessionIdentity? CurrentSessionIdentity()
    {
        var user = _session.User;
        return user is not null && user.Role is "MANAGER" or "STAFF" or "RESIDENT"
            ? new SessionIdentity(user.Id, user.Role)
            : null;
    }

    private async Task DisposeConnectionAsync()
    {
        var connection = _connection;
        _connection = null;
        _connectionIdentity = null;
        if (connection is null) return;

        try
        {
            if (connection.State != HubConnectionState.Disconnected)
                await connection.StopAsync();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Service Request realtime connection could not stop cleanly.");
        }

        try
        {
            await connection.DisposeAsync();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Service Request realtime connection could not be disposed cleanly.");
        }
    }

    private async Task DispatchAsync(Guid serviceRequestId)
    {
        var handlers = Changed;
        if (handlers is null) return;

        foreach (var handler in handlers.GetInvocationList().Cast<Func<Guid, Task>>())
        {
            try { await handler(serviceRequestId); }
            catch (Exception exception) { _logger.LogWarning(exception, "A Service Request realtime refresh failed."); }
        }
    }

    private Task ReconcileAsync() =>
        !_disposed && _startRequested && CurrentSessionIdentity() is not null && Changed is not null
            ? DispatchAsync(Guid.Empty)
            : Task.CompletedTask;

    private void SetState(ServiceRequestRealtimeState state)
    {
        if (State == state) return;
        State = state;

        var handlers = ConnectionStateChanged;
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Action<ServiceRequestRealtimeState>>())
        {
            try { handler(state); }
            catch (Exception exception) { _logger.LogWarning(exception, "A Service Request realtime state observer failed."); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _session.AuthenticationStateChanged -= HandleAuthenticationStateChanged;
        await _lifetime.CancelAsync();
        await _reconciliation.DisposeAsync();
        SetState(ServiceRequestRealtimeState.Inactive);
        ConnectionStateChanged = null;
        Changed = null;
        await _connectionGate.WaitAsync();
        try
        {
            await DisposeConnectionAsync();
        }
        finally
        {
            _connectionGate.Release();
        }
        _lifetime.Dispose();
        _connectionGate.Dispose();
    }

    private readonly record struct SessionIdentity(Guid UserId, string Role);
}
