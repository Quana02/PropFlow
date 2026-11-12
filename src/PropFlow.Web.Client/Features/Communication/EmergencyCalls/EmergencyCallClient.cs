using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using PropFlow.Modules.Communication.Contracts;
using PropFlow.Web.Client.Services.Authentication;

namespace PropFlow.Web.Client.Features.Communication.EmergencyCalls;

public sealed class EmergencyCallClient(EmergencyCallApi api, AuthSession session, IConfiguration configuration,
    NavigationManager navigation, IJSRuntime js, ILogger<EmergencyCallClient> logger) : IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim setupGate = new(1, 1), refreshGate = new(1, 1);
    private readonly Queue<EmergencySignal> inbox = new();
    private HubConnection? hub;
    private IJSObjectReference? module, media;
    private DotNetObjectReference<EmergencyCallClient>? reference;
    private EmergencyRtcConfiguration? rtc;
    private string? peerKey;
    private Guid? owner;
    private string? ownerRole;
    private Task? poll;
    private bool active, disposed, preparing, mediaReady, refreshAgain, mediaPaused;
    public EmergencyCallView? Current { get; private set; }
    public bool PanelOpen { get; private set; }
    public bool Busy { get; private set; }
    public bool Connected => hub?.State == HubConnectionState.Connected;
    public bool ForManager => session.User?.Role == "MANAGER";
    public bool Muted { get; private set; }
    public bool AudioBlocked { get; private set; }
    public string? Error { get; private set; }
    public string MediaState { get; private set; } = "idle";
    public event Action? Changed;

    public async Task ActivateAsync(ElementReference audio)
    {
        if (disposed || active) return;
        active = true;
        await session.InitializeAsync(); owner = session.User?.Id; ownerRole = session.User?.Role;
        session.AuthenticationStateChanged += AuthenticationChanged;
        try
        {
            module = await js.InvokeAsync<IJSObjectReference>("import", "./js/emergency-call.js?v=20261112-1");
            reference = DotNetObjectReference.Create(this);
            media = await module.InvokeAsync<IJSObjectReference>("create", reference, audio);
            await EnsureAsync();
            await RefreshAsync();
            poll = PollAsync();
        }
        catch (JSException) { Error = "Không khởi tạo được phần gọi thoại. Khi có nguy hiểm, hãy gọi trực tiếp 114."; Notify(); }
    }
    private async Task EnsureAsync()
    {
        if (disposed || !active || owner != session.User?.Id || session.User?.Role is not ("MANAGER" or "RESIDENT")) return;
        await setupGate.WaitAsync(lifetime.Token);
        try
        {
            if (hub?.State is HubConnectionState.Connected or HubConnectionState.Connecting or HubConnectionState.Reconnecting) return;
            if (hub is null)
            {
                var apiBase = new Uri(new Uri(navigation.BaseUri), configuration["Api:BaseUrl"] ?? throw new InvalidOperationException("Thiếu cấu hình API."));
                hub = new HubConnectionBuilder().WithUrl(new Uri(apiBase, "hubs/emergency-calls"), options => options.AccessTokenProvider = session.GetValidAccessTokenAsync)
                    .WithAutomaticReconnect().Build();
                hub.On("EmergencyChanged", RefreshAsync);
                hub.On<EmergencySignal>("EmergencySignal", async value =>
                {
                    if (disposed || !active || owner != session.User?.Id) return;
                    lock (inbox) { if (inbox.Count < 128) inbox.Enqueue(value); }
                    await RefreshAsync();
                });
                hub.Reconnecting += _ => { MediaState = "disconnected"; Notify(); return Task.CompletedTask; };
                hub.Reconnected += async _ => { peerKey = null; await RefreshAsync(); };
                hub.Closed += _ => { Notify(); return Task.CompletedTask; };
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); deadline.CancelAfter(TimeSpan.FromSeconds(5));
            await hub.StartAsync(deadline.Token); Notify();
        }
        catch (Exception e) when (e is HubException or HttpRequestException or InvalidOperationException or OperationCanceledException)
        { if (!disposed) { logger.LogDebug("Emergency connection pending ({Kind}).", e.GetType().Name); Notify(); } }
        finally { setupGate.Release(); }
    }

    public async Task StartAsync()
    {
        if (disposed || Busy || ForManager) return;
        if (Current is { Status: not "ENDED" }) { PanelOpen = true; Notify(); return; }
        Busy = true; PanelOpen = true; Error = null; mediaPaused = false; Notify();
        var capture = PrepareMediaAsync();
        try
        {
            var result = await api.Start(new(Guid.NewGuid(), Connected ? hub!.ConnectionId : null), lifetime.Token);
            if (!result.IsSuccess || result.Data is null) { Error = result.Message ?? "Chưa gửi được báo khẩn cấp. Hãy gọi trực tiếp để được hỗ trợ."; await StopMediaAsync(); return; }
            Current = result.Data; Busy = false; Notify();
            await capture;
            await EnsureAsync(); await RefreshAsync();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { Busy = false; Notify(); }
    }

    private async Task<bool> PrepareMediaAsync()
    {
        if (mediaReady) return true;
        if (media is null || preparing || disposed) return false;
        preparing = true; MediaState = "permission"; Notify();
        try { mediaReady = await media.InvokeAsync<bool>("prepare", lifetime.Token); return mediaReady; }
        catch (JSException) { Error = "Chưa truy cập được micro. Kiểm tra quyền micro rồi thử lại."; return false; }
        finally { preparing = false; Notify(); }
    }
    public async Task EnableMicrophoneAsync()
    {
        Error = null; mediaReady = false; mediaPaused = false;
        if (await PrepareMediaAsync()) { peerKey = null; await RefreshAsync(); }
    }
    public async Task RetryAsync()
    {
        if (Busy || Current is not { Status: not "ENDED" } call) return;
        Busy = true; Error = null; mediaPaused = false; Notify();
        try
        {
            mediaReady = false; await PrepareMediaAsync();
            var result = await api.Retry(call.Id, lifetime.Token);
            if (result.IsSuccess) { Current = result.Data; peerKey = null; await RefreshAsync(); }
            else Error = result.Message;
        }
        finally { Busy = false; Notify(); }
    }
    public async Task EndAsync()
    {
        if (Busy || Current is not { Status: not "ENDED" } call) return;
        Busy = true; mediaPaused = true; Notify();
        try
        {
            await StopMediaAsync(); MediaState = "paused"; Notify();
            var result = await api.End(call.Id, lifetime.Token);
            if (result.IsSuccess) Current = result.Data;
            else Error = "Đã ngắt âm thanh tại thiết bị này nhưng chưa xác nhận kết thúc với máy chủ. " + result.Message;
        }
        finally { Busy = false; Notify(); }
    }
    public async Task ToggleMuteAsync()
    { if (media is null) return; Muted = !Muted; await media.InvokeVoidAsync("mute", Muted); Notify(); }
    public async Task PlayAudioAsync()
    {
        if (media is null || Current is null) return;
        AudioBlocked = !await media.InvokeAsync<bool>("play", Current.Id.ToString(), Current.Revision); Notify();
    }
    public async Task DismissAsync()
    { if (Current is null || Current.Status == "ENDED") { await StopMediaAsync(); PanelOpen = false; Current = null; Error = null; Notify(); } }

    private async Task RefreshAsync()
    {
        if (disposed || !active || owner != session.User?.Id || !Connected || hub!.ConnectionId is not { } connection) return;
        if (!await refreshGate.WaitAsync(0)) { refreshAgain = true; return; }
        try
        {
            do
            {
                refreshAgain = false;
                var result = await api.Active(connection, lifetime.Token);
                if (!result.IsSuccess || result.Data is null)
                {
                    Error = result.Message;
                    if (result.StatusCode is 401 or 403) { active = false; Current = null; PanelOpen = false; await StopMediaAsync(); if (hub is not null) await hub.StopAsync(); }
                    Notify(); return;
                }
                var next = result.Data.Call;
                if (next is null)
                {
                    if (Current is { Status: not "ENDED" })
                    { Current = null; Error = "Cuộc gọi không còn hoạt động tại phiên này: người bên kia đã kết thúc hoặc cuộc gọi đã chuyển sang phiên khác."; await StopMediaAsync(); }
                }
                else
                {
                    Current = next; PanelOpen = true;
                    if (next.Status == "WAITING" && peerKey is not null) await StopMediaAsync();
                    Notify();
                    if (!mediaPaused && next.Status is "CONNECTING" or "CONNECTED" && next.ResidentOnline && next.ManagerOnline)
                    {
                        var key = $"{next.Id}:{next.Revision}";
                        if (peerKey != key && media is not null)
                        {
                            if (rtc is null)
                            {
                                var config = await api.Configuration(lifetime.Token);
                                if (!config.IsSuccess || config.Data is null) { Error = config.Message; Notify(); return; }
                                rtc = config.Data;
                            }
                            if (await PrepareMediaAsync())
                            {
                                peerKey = key;
                                if (!await media.InvokeAsync<bool>("connect", lifetime.Token, next.Id.ToString(), next.Revision, !ForManager, rtc.IceServers)) peerKey = null;
                            }
                        }
                        await DrainSignalsAsync(next);
                    }
                }
                Notify();
            } while (refreshAgain && !disposed);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (JSException) { Error = "Đường truyền thoại chưa thiết lập được. Bạn có thể thử kết nối lại hoặc gọi trực tiếp."; peerKey = null; Notify(); }
        finally { refreshGate.Release(); }
    }
    private async Task DrainSignalsAsync(EmergencyCallView call)
    {
        if (media is null || peerKey != $"{call.Id}:{call.Revision}") return;
        EmergencySignal[] frames;
        lock (inbox) { frames = inbox.ToArray(); inbox.Clear(); }
        foreach (var frame in frames)
            if (frame.CallId == call.Id && frame.Revision == call.Revision)
                await media.InvokeAsync<bool>("receive", lifetime.Token, frame.CallId.ToString(), frame.Revision, frame.Kind, frame.Data);
    }
    [JSInvokable]
    public async Task OnRtcSignal(string id, int revision, string kind, string data)
    {
        if (disposed || mediaPaused || owner != session.User?.Id || !Connected || Current is not { Status: "CONNECTING" or "CONNECTED" } call || call.Id.ToString() != id || call.Revision != revision) return;
        try { await hub!.InvokeAsync("Signal", new EmergencySignal(call.Id, revision, kind, data), lifetime.Token); }
        catch (Exception e) when (e is HubException or HttpRequestException or InvalidOperationException)
        { Error = "Tín hiệu cuộc gọi bị gián đoạn. Hãy thử kết nối lại."; Notify(); }
    }
    [JSInvokable]
    public async Task OnRtcState(string id, int revision, string state)
    {
        if (disposed || mediaPaused || Current is not { Status: "CONNECTING" or "CONNECTED" } call || call.Id.ToString() != id || call.Revision != revision) return;
        if (state is "audio-blocked" or "audio-playing") AudioBlocked = state == "audio-blocked";
        else MediaState = state;
        if (state == "connected" && Connected)
            try { await hub!.InvokeAsync("MediaConnected", call.Id, revision, lifetime.Token); }
            catch (Exception e) when (e is HubException or HttpRequestException or InvalidOperationException) { logger.LogDebug("Emergency media acknowledgement deferred ({Kind}).", e.GetType().Name); }
        if (state is "disconnected" or "failed" && Connected)
            try { await hub!.InvokeAsync("MediaDisconnected", call.Id, revision, lifetime.Token); }
            catch (Exception e) when (e is HubException or HttpRequestException or InvalidOperationException) { logger.LogDebug("Emergency disconnect acknowledgement deferred ({Kind}).", e.GetType().Name); }
        Notify();
    }
    [JSInvokable]
    public Task OnMediaError(string code)
    {
        if (disposed) return Task.CompletedTask;
        mediaReady = false; MediaState = "microphone";
        Error = code switch
        {
            "NotAllowedError" or "PermissionDeniedError" => "Micro chưa được cho phép. Hãy cấp quyền micro để nói với người bên kia.",
            "NotFoundError" or "microphone-lost" => "Chưa có micro hoạt động hoặc micro đã bị ngắt.",
            "not-supported" => "Trình duyệt chưa hỗ trợ cuộc gọi tại địa chỉ này. Hãy dùng HTTPS hoặc localhost và trình duyệt có hỗ trợ gọi thoại.",
            _ => "Chưa mở được micro. Bạn vẫn có thể gọi trực tiếp 114 khi cần cứu nạn."
        };
        Notify(); return Task.CompletedTask;
    }
    private async Task StopMediaAsync()
    {
        peerKey = null; mediaReady = false; MediaState = "idle"; Muted = AudioBlocked = false;
        lock (inbox) inbox.Clear();
        if (media is not null) await media.InvokeVoidAsync("stop");
    }
    private async Task PollAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3)); var ticks = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token))
            {
                if (!active || disposed) return;
                await EnsureAsync();
                if (Connected && ++ticks % 5 == 0)
                    try { await hub!.InvokeAsync("Heartbeat", lifetime.Token); }
                    catch (Exception e) when (e is HubException or HttpRequestException or InvalidOperationException) { logger.LogDebug("Emergency heartbeat deferred ({Kind}).", e.GetType().Name); }
                if (ForManager || Current is not null) await RefreshAsync();
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }
    private void AuthenticationChanged(Task<AuthenticationState> state) => _ = AuthenticationChangedAsync();
    private async Task AuthenticationChangedAsync()
    {
        if (disposed) return;
        if (owner == session.User?.Id && ownerRole == session.User?.Role) return;
        active = true; Current = null; PanelOpen = false; Error = null; rtc = null; mediaPaused = false;
        await StopMediaAsync();
        await setupGate.WaitAsync();
        try { if (hub is not null) await hub.DisposeAsync(); hub = null; owner = session.User?.Id; ownerRole = session.User?.Role; }
        finally { setupGate.Release(); }
        await EnsureAsync(); Notify();
    }
    private void Notify() { if (!disposed) Changed?.Invoke(); }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true; active = false; lifetime.Cancel();
        session.AuthenticationStateChanged -= AuthenticationChanged;
        if (poll is not null) await poll;
        if (hub is not null) await hub.DisposeAsync();
        if (media is not null) { await media.InvokeVoidAsync("dispose"); await media.DisposeAsync(); }
        if (module is not null) await module.DisposeAsync();
        reference?.Dispose(); lifetime.Dispose();
    }
}
