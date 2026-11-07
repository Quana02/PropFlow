using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR;
using PropFlow.Modules.Communication.Contracts;
using PropFlow.Web.Client.Services.Authentication;

namespace PropFlow.Web.Client.Features.Communication.SupportChat;

public sealed class SupportChatConnection : ISupportChatConnection, IAsyncDisposable
{
    private readonly IConfiguration configuration;
    private readonly NavigationManager navigation;
    private readonly AuthSession session;
    private readonly ILogger<SupportChatConnection> logger;
    private readonly SemaphoreSlim gate = new(1, 1), draftGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private HubConnection? connection;
    private (Guid Id, string Role)? identity;
    private Task? heartbeat;
    private long draftSequence;
    private bool disposed;
    private bool wanted;
    private readonly Dictionary<(Guid Id, string Stream), (long Sequence, string Text, DateTimeOffset Until)> drafts = [];
    private readonly Dictionary<Guid, DateTimeOffset> typing = [];
    public SupportChatConnection(IConfiguration configuration, NavigationManager navigation, AuthSession session, ILogger<SupportChatConnection> logger)
    { this.configuration = configuration; this.navigation = navigation; this.session = session; this.logger = logger; session.AuthenticationStateChanged += AuthenticationChanged; }
    public bool Connected => !disposed && connection?.State == HubConnectionState.Connected && identity == Identity();
    public event Func<Guid, Task>? Changed;
    public event Action? TransientChanged;
    public event Action? StateChanged;
    private (Guid Id, string Role)? Identity() => session.User is { } user && user.Role is "MANAGER" or "RESIDENT" ? (user.Id, user.Role) : null;
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        if (disposed) return;
        wanted = true;
        await session.InitializeAsync();
        heartbeat ??= HeartbeatLoop();
        await gate.WaitAsync(ct);
        try
        {
            if (disposed) return;
            var current = Identity();
            if (identity != current) await StopConnection();
            if (current is null) return;
            if (connection?.State is HubConnectionState.Connecting or HubConnectionState.Reconnecting or HubConnectionState.Connected) return;
            identity = current;
            connection ??= Build();
            await connection.StartAsync(ct); StateChanged?.Invoke();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is HttpRequestException or HubException or InvalidOperationException or OperationCanceledException)
        { logger.LogWarning("Support chat connection unavailable ({Kind}).", e.GetType().Name); StateChanged?.Invoke(); }
        finally { gate.Release(); }
    }
    private HubConnection Build()
    {
        var apiBase = new Uri(new Uri(navigation.BaseUri), configuration["Api:BaseUrl"] ?? throw new InvalidOperationException("Thiếu cấu hình API."));
        var source = new HubConnectionBuilder().WithUrl(new Uri(apiBase, "hubs/support-chat"), o => o.AccessTokenProvider = session.GetValidAccessTokenAsync)
            .WithAutomaticReconnect(new Reconnect()).Build();
        source.On<Guid>("SupportChatChanged", async id => {
            if (!Current(source)) return;
            if (Changed is not null) foreach (var handler in Changed.GetInvocationList().Cast<Func<Guid, Task>>()) await handler(id);
        });
        source.On<SupportDraft>("SupportChatDraft", draft => {
            if (!Current(source) || identity?.Role != "MANAGER") return;
            lock (drafts)
            {
                var key = (draft.ConversationId, draft.StreamId);
                if (drafts.TryGetValue(key, out var old) && old.Sequence >= draft.Sequence) return;
                if (drafts.Count >= 32 && !drafts.ContainsKey(key)) drafts.Remove(drafts.OrderBy(x => x.Value.Until).First().Key);
                drafts[key] = (draft.Sequence, draft.Text, DateTimeOffset.UtcNow.AddSeconds(10));
            }
            TransientChanged?.Invoke();
        });
        source.On<SupportTyping>("SupportChatTyping", value => {
            if (!Current(source) || identity?.Role != "RESIDENT") return;
            lock (typing) { if (value.Typing) typing[value.ConversationId] = DateTimeOffset.UtcNow.AddSeconds(8); else typing.Remove(value.ConversationId); }
            TransientChanged?.Invoke();
        });
        source.Reconnecting += _ => { ClearTransient(); StateChanged?.Invoke(); return Task.CompletedTask; };
        source.Reconnected += async _ => { if (Current(source)) { await source.InvokeAsync("Heartbeat"); StateChanged?.Invoke(); if (Changed is not null) foreach (var h in Changed.GetInvocationList().Cast<Func<Guid, Task>>()) await h(Guid.Empty); } };
        source.Closed += _ => { ClearTransient(); StateChanged?.Invoke(); return Task.CompletedTask; };
        return source;
    }
    private bool Current(HubConnection source) => !disposed && ReferenceEquals(connection, source) && identity == Identity();
    public string? Draft(Guid id)
    {
        lock (drafts) return drafts.Where(x => x.Key.Id == id && x.Value.Until > DateTimeOffset.UtcNow && x.Value.Text.Length > 0)
            .OrderByDescending(x => x.Value.Until).Select(x => x.Value.Text).FirstOrDefault();
    }
    public void ClearDraft(Guid id)
    { lock (drafts) foreach (var key in drafts.Keys.Where(x => x.Id == id).ToArray()) drafts[key] = (drafts[key].Sequence, "", DateTimeOffset.MinValue); TransientChanged?.Invoke(); }
    public bool IsTyping(Guid id) { lock (typing) return typing.TryGetValue(id, out var until) && until > DateTimeOffset.UtcNow; }
    public async Task PublishDraft(Guid id, string text)
    {
        if (!Connected || identity?.Role != "RESIDENT") return;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); deadline.CancelAfter(TimeSpan.FromSeconds(2));
        var acquired = false;
        try { await draftGate.WaitAsync(deadline.Token); acquired = true; if (Connected) await connection!.InvokeAsync("ResidentDraft", id, Interlocked.Increment(ref draftSequence), text, deadline.Token); }
        catch (Exception e) when (e is HubException or HttpRequestException or InvalidOperationException or OperationCanceledException)
        { logger.LogDebug("Support draft delivery unavailable ({Kind}).", e.GetType().Name); }
        finally { if (acquired) draftGate.Release(); }
    }
    public async Task PublishTyping(Guid id, bool value)
    {
        if (!Connected || identity?.Role != "MANAGER") return;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try { await connection!.InvokeAsync("ManagerTyping", id, value, deadline.Token); }
        catch (Exception e) when (e is HubException or HttpRequestException or InvalidOperationException or OperationCanceledException)
        { logger.LogDebug("Support typing delivery unavailable ({Kind}).", e.GetType().Name); }
    }
    private async Task HeartbeatLoop()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token))
            {
                if (disposed) return;
                if (!wanted) continue;
                if (!Connected) await EnsureAsync(lifetime.Token);
                else
                    try { await connection!.InvokeAsync("Heartbeat", lifetime.Token); }
                    catch (Exception e) when (e is HubException or HttpRequestException or InvalidOperationException)
                    { await gate.WaitAsync(lifetime.Token); try { await StopConnection(); } finally { gate.Release(); } StateChanged?.Invoke(); }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }
    private void AuthenticationChanged(Task<AuthenticationState> state) => _ = ResetForAuthentication();
    private async Task ResetForAuthentication()
    {
        if (disposed || identity == Identity()) return;
        wanted = false;
        await gate.WaitAsync(); try { await StopConnection(); } finally { gate.Release(); }
        StateChanged?.Invoke();
    }
    public async Task ReleaseResidentAsync()
    {
        if (disposed || identity?.Role != "RESIDENT") return;
        wanted = false;
        await gate.WaitAsync(); try { await StopConnection(); } finally { gate.Release(); }
    }
    private async Task StopConnection()
    {
        var old = connection; connection = null; identity = null; ClearTransient();
        if (old is not null) await old.DisposeAsync();
    }
    private void ClearTransient() { lock (drafts) drafts.Clear(); lock (typing) typing.Clear(); TransientChanged?.Invoke(); }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true; session.AuthenticationStateChanged -= AuthenticationChanged; lifetime.Cancel();
        if (heartbeat is not null) await heartbeat;
        await gate.WaitAsync(); try { await StopConnection(); } finally { gate.Release(); }
        lifetime.Dispose();
    }
    private sealed class Reconnect : IRetryPolicy { public TimeSpan? NextRetryDelay(RetryContext retry) => TimeSpan.FromSeconds(Math.Min(30, 2 + retry.PreviousRetryCount * 2)); }
}
