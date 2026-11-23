using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PropFlow.Web.Client.Features.Facility.Services;

namespace PropFlow.Web.Client.Features.Resident.Portal.Pages;

public partial class ResidentFacilities : IAsyncDisposable
{
    [Inject] private FacilityRecommendationsApi Api { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [SupplyParameterFromQuery(Name = "facility")] public Guid? SuggestedFacility { get; set; }
    private Guid? loadedFacility;
    private const string Welcome = "Chào bạn, mình là trợ lý tiện ích PropFlow. Hôm nay bạn muốn làm gì? Mình sẽ cùng bạn tìm hoạt động và thời gian phù hợp nhé.";
    private ResidentFacilityCatalogue? catalogue;
    private FacilityRecommendationView? suggestions;
    private string need = "", interimText = "", assistantText = Welcome, previousNeed = "", voiceBase = "";
    private string? lastSpokenNeed, voiceNotice, error;
    private bool loading, busy, disposed, listening, speaking, preparingSpeech, voiceSupported, speechSupported, heardSpeech, audioEnabled = true;
    private bool conversationActive, startingMicrophone;
    private bool entryConversationPending = true;
    private int voiceEpoch;
    private IJSObjectReference? module, voice;
    private DotNetObjectReference<ResidentFacilities>? reference;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? speechCancellation;
    private CancellationTokenSource? recommendationCancellation;
    private string lastSpeechKind = "WELCOME";
    private string VisualState => busy || preparingSpeech || liveConnecting ? "thinking" : speaking ? "speaking" : listening ? "listening" : "ready";
    private string StatusText => liveMode ? LiveStatusText : busy ? "Đang tìm điều phù hợp với bạn…" : preparingSpeech ? (lastSpeechKind is "WELCOME" or "NEW_NEED" ? "Đang kết nối trợ lý…" : "Đang tạo giọng tiếng Việt…") : listening ? "Mình đang nghe bạn…" : speaking ? "PropFlow đang nói…" : conversationActive ? "Đang chờ bạn nói tiếp…" : "Mình ở đây để giúp bạn";

    protected override Task OnInitializedAsync() => Load();
    protected override async Task OnParametersSetAsync()
    {
        await ReceiveHandoff();
        if (SuggestedFacility is { } id && loadedFacility != id && catalogue is not null) await LoadSuggestedFacility(id);
    }
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        try
        {
            module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/facility-assistant-voice.js?v=20261122-1");
            if (disposed) { await module.DisposeAsync(); return; }
            var capabilities = await module.InvokeAsync<VoiceCapabilities>("capabilities");
            voiceSupported = capabilities.Recognition; speechSupported = capabilities.Speech;
            reference = DotNetObjectReference.Create(this);
            voice = await module.InvokeAsync<IJSObjectReference>("create", reference);
            if (disposed) { await voice.InvokeVoidAsync("dispose"); await voice.DisposeAsync(); reference.Dispose(); return; }
            if (!voiceSupported) voiceNotice = "Bạn có thể nhập bên dưới. Micro cần trình duyệt hỗ trợ như Chrome/Edge trên HTTPS hoặc localhost.";
            if (!speechSupported) voiceNotice = "Trình duyệt chưa phát được âm thanh. Bạn vẫn có thể nhập hoặc nói để xem gợi ý.";
            if (await InitializeLive()) { if (!disposed) StateHasChanged(); return; }
            BeginConversationOnEntry();
            await Speak(suggestions?.Mode == "TODAY" ? "RESULT" : "WELCOME");
        }
        catch (JSException) { voiceNotice = "Giọng nói chưa sẵn sàng. Bạn vẫn có thể nhập để trò chuyện."; }
        catch (ObjectDisposedException) when (disposed) { }
        if (!disposed) await InvokeAsync(StateHasChanged);
    }

    private async Task Load()
    {
        loading = true; error = null;
        try
        {
            var result = await Api.CatalogueAsync(lifetime.Token);
            if (disposed) return;
            catalogue = result.Data; error = result.IsSuccess ? null : result.Message;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { if (!disposed) loading = false; }
        if (!disposed && catalogue is not null && SuggestedFacility is { } id) await LoadSuggestedFacility(id);
        if (!disposed) { BeginConversationOnEntry(); await ListenAgain(); }
    }

    private void BeginConversationOnEntry()
    {
        if (!entryConversationPending || disposed || voice is null || !voiceSupported || loading || catalogue?.Facilities.Length is not > 0) return;
        entryConversationPending = false;
        conversationActive = true;
        StartIdleWatch();
    }

    private async Task LoadSuggestedFacility(Guid id)
    {
        if (liveMode && conversationActive) await StopLive();
        loadedFacility = id; error = null; var epoch = ++voiceEpoch;
        CancelRecommendation();
        var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        recommendationCancellation = request; busy = true;
        try
        {
            await CancelVoice();
            var result = await Api.TodayAsync(id, request.Token);
            if (disposed || request.IsCancellationRequested || epoch != voiceEpoch) return;
            if (!result.IsSuccess || result.Data is null) { error = result.Message ?? "Chưa tải được lịch tiện ích. Bạn thử lại nhé."; return; }
            suggestions = result.Data; previousNeed = need = ""; lastSpokenNeed = null;
            assistantText = FacilityAssistantReply.Create(suggestions);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(recommendationCancellation, request)) { recommendationCancellation = null; busy = false; }
            request.Dispose();
        }
        if (!disposed && epoch == voiceEpoch && suggestions is not null && voice is not null)
        { if (liveMode) await StartLive(); else await Speak("RESULT"); }
    }

    private async Task ToggleMicrophone()
    {
        if (idleExiting) return;
        if (liveMode) { if (conversationActive) await StopLive(); else await StartLive(); return; }
        if (conversationActive) { await StopConversation(); return; }
        if (voice is null || !voiceSupported || busy || disposed || catalogue?.Facilities.Length is not > 0) return;
        entryConversationPending = false;
        conversationActive = true; voiceNotice = error = null;
        StartIdleWatch();
        await CancelVoice();
        await ListenAgain();
    }

    private async Task ListenAgain()
    {
        if (liveMode) return;
        if (!conversationActive || disposed || busy || listening || speaking || preparingSpeech || startingMicrophone || voice is null) return;
        startingMicrophone = true;
        var epoch = ++voiceEpoch;
        try
        {
            interimText = ""; voiceBase = need.Trim(); heardSpeech = false;
            var started = await voice.InvokeAsync<bool>("start", epoch);
            if (disposed || epoch != voiceEpoch || !conversationActive) return;
            listening = started;
            if (!started) await StopConversation("Chưa mở được micro. Hãy kiểm tra quyền micro hoặc nhập bên dưới.");
        }
        catch (JSException) { await StopConversation("Chưa mở được micro. Bạn có thể nhập nhu cầu bên dưới."); }
        finally { startingMicrophone = false; }
        if (!disposed) StateHasChanged();
    }

    private async Task StopConversation(string? notice = null)
    {
        StopIdleWatch();
        if (liveMode) { await StopLive(); if (notice is not null) voiceNotice = notice; return; }
        PreserveInterim(); entryConversationPending = false; conversationActive = false; ++voiceEpoch;
        CancelRecommendation();
        await CancelVoice();
        if (notice is not null) voiceNotice = notice;
    }

    private async Task InterruptAndListen()
    {
        if (liveMode && liveVoice is not null) { await liveVoice.InvokeVoidAsync("interrupt"); return; }
        if (!conversationActive || disposed) return;
        ++voiceEpoch; CancelRecommendation();
        await CancelVoice();
        // The next utterance is a new turn, rather than an append to an interrupted draft.
        need = interimText = "";
        await ListenAgain();
    }

    private void PreserveInterim()
    {
        if (!string.IsNullOrWhiteSpace(interimText))
        {
            var text = string.Join(" ", new[] { need.Trim(), interimText.Trim() }.Where(x => x.Length > 0));
            need = text.Length <= 1200 ? text : text[..1200];
        }
        interimText = "";
    }

    private void CancelRecommendation()
    {
        var previous = recommendationCancellation; recommendationCancellation = null;
        previous?.Cancel(); busy = false;
    }

    [JSInvokable]
    public Task OnVoiceResult(int epoch, string finalText, string interim) => InvokeAsync(() =>
    {
        if (liveMode) return;
        if (disposed || epoch != voiceEpoch || !listening) return;
        if (!string.IsNullOrWhiteSpace(finalText) || !string.IsNullOrWhiteSpace(interim)) MarkIdleActivity();
        if (IdleExpired || idleExiting) return;
        heardSpeech |= !string.IsNullOrWhiteSpace(finalText) || !string.IsNullOrWhiteSpace(interim);
        var combined = string.Join(" ", new[] { voiceBase, finalText.Trim() }.Where(x => x.Length > 0));
        need = combined.Length <= 1200 ? combined : combined[..1200];
        interimText = interim.Length <= 1200 ? interim : interim[..1200];
        StateHasChanged();
    });

    [JSInvokable]
    public Task OnVoiceEnded(int epoch, string failure) => InvokeAsync(async () =>
    {
        if (liveMode) return;
        if (disposed || epoch != voiceEpoch || !listening) return;
        listening = false; PreserveInterim();
        if (failure.Length > 0 && failure != "no-speech")
        {
            var notice = failure switch
            {
                "not-allowed" or "service-not-allowed" => "Bạn cần cho phép micro trong trình duyệt. Nội dung đã nhận vẫn được giữ lại.",
                "audio-capture" => "Chưa tìm thấy micro. Kiểm tra thiết bị hoặc nhập bên dưới nhé.",
                _ => "Giọng nói vừa bị gián đoạn. Bạn có thể sửa phần chữ đã nhận và gửi lại."
            };
            await StopConversation(notice);
        }
        else if (heardSpeech && need.Trim().Length >= 2) { await Recommend(); }
        else if (conversationActive)
        {
            voiceNotice = "Mình vẫn đang nghe. Bạn có thể nói nhu cầu khi sẵn sàng.";
            // Yield before restarting a browser recognition session that ended without speech.
            try { await Task.Delay(400, lifetime.Token); if (epoch == voiceEpoch) await ListenAgain(); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        }
        if (!disposed) StateHasChanged();
    });

    [JSInvokable]
    public Task OnAssistantSpeech(int epoch, string state) => InvokeAsync(async () =>
    {
        if (disposed || epoch != voiceEpoch) return;
        speaking = audioEnabled && state == "speaking";
        if (state == "paused") await StopConversation("Cuộc trò chuyện đã tạm dừng khi bạn rời màn hình. Chạm để nói để tiếp tục.");
        if (state == "unavailable") voiceNotice = "Chưa phát được âm thanh tiếng Việt. Bạn nhấn nghe lại nhé.";
        if (state == "playback-blocked") await StopConversation("Trình duyệt đang chặn tự phát âm thanh. Nhấn Nghe lại để nghe giọng tiếng Việt.");
        if (state is "idle" or "unavailable") await ListenAgain();
        StateHasChanged();
    });

    private async Task Recommend()
    {
        if (liveMode && conversationActive) { await SendLiveText(need.Trim()); return; }
        if (busy || listening || need.Trim().Length < 2 || catalogue?.Facilities.Length is not > 0) return;
        var input = need.Trim();
        var combined = previousNeed.Length > 0
            ? previousNeed + "\nBổ sung: " + input : input;
        if (combined.Length > 1200) { error = "Nội dung trò chuyện đã dài. Hãy chọn tìm cho nhu cầu khác và mô tả gọn lại nhé."; await StopConversation(); return; }
        var epoch = ++voiceEpoch;
        CancelRecommendation();
        var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        recommendationCancellation = request; speaking = false;
        busy = true; error = null; lastSpokenNeed = input;
        StateHasChanged();
        try
        {
            await CancelVoice();
            var result = await Api.RecommendAsync(combined, null, request.Token);
            if (disposed || request.IsCancellationRequested || epoch != voiceEpoch) return;
            if (!result.IsSuccess || result.Data is null)
            {
                error = result.Message ?? "Mình chưa kết nối được. Bạn thử lại một lát nhé.";
                assistantText = "Mình chưa tìm được kết quả lúc này. Nội dung bạn nói vẫn ở đây để thử lại.";
                suggestions = null;
                // Keep the unsent current utterance, without duplicating already accepted context on retry.
                need = input;
                conversationActive = false;
                return;
            }
            suggestions = result.Data; previousNeed = combined; need = "";
            assistantText = FacilityAssistantReply.Create(suggestions);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(recommendationCancellation, request)) { recommendationCancellation = null; busy = false; }
            request.Dispose();
        }
        if (!disposed && epoch == voiceEpoch && suggestions is not null) { StateHasChanged(); await Speak("RESULT"); }
    }

    private async Task Example(string text)
    {
        suggestions = null; previousNeed = ""; need = text;
        await Recommend();
    }
    private async Task NewNeed()
    {
        if (requestSubmitting) return;
        if (liveMode && conversationActive)
        {
            SetAssistantPane("facilities", Guid.NewGuid().ToString("N"));
            suggestions = liveCandidates = null; liveLookupId = null;
            foreach (var tool in liveTools.Values.ToArray()) tool.Cancel();
            liveTools.Clear();
            await SendLiveText("Mình muốn tìm cho một nhu cầu mới, hãy hỏi mình muốn làm gì."); return;
        }
        ++voiceEpoch; CancelRecommendation(); speaking = listening = false;
        await CancelVoice();
        suggestions = null; previousNeed = need = ""; lastSpokenNeed = error = null;
        assistantText = "Mình nghe đây. Lần này bạn muốn tìm hoạt động gì?";
        await Speak("NEW_NEED");
    }
    private async Task ToggleAudio()
    {
        audioEnabled = !audioEnabled;
        if (liveMode && liveVoice is not null) { await liveVoice.InvokeVoidAsync("setMuted", !audioEnabled); return; }
        if (voice is null) return;
        if (!audioEnabled)
        {
            CancelSpeechRequest();
            speaking = false;
            try { await voice.InvokeVoidAsync("stopSpeaking"); } catch (JSException) { }
            await ListenAgain();
        }
        else await Speak();
    }
    private async Task Replay()
    {
        audioEnabled = true; voiceNotice = null;
        if (liveMode && conversationActive && liveVoice is not null) { await liveVoice.InvokeVoidAsync("setMuted", false); await SendLiveText("Bạn nhắc lại câu trả lời vừa rồi giúp mình."); }
        else await Speak();
    }
    private async Task ShowResults()
    {
        if (module is null) return;
        try { await module.InvokeVoidAsync("showResults"); } catch (JSException) { }
    }
    private async Task CancelVoice()
    {
        CancelSpeechRequest();
        speaking = listening = false;
        if (voice is null) return;
        try { await voice.InvokeVoidAsync("cancel", voiceEpoch); } catch (JSException) { speaking = false; listening = false; }
    }
    private void CancelSpeechRequest()
    {
        speechCancellation?.Cancel(); speechCancellation = null; preparingSpeech = false;
    }
    private async Task Speak(string? kind = null)
    {
        lastSpeechKind = kind ?? lastSpeechKind;
        if (!audioEnabled || !speechSupported || voice is null || disposed || listening) { await ListenAgain(); return; }
        CancelSpeechRequest();
        var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        speechCancellation = request; var epoch = voiceEpoch;
        preparingSpeech = true; StateHasChanged();
        try
        {
            if (lastSpeechKind is "WELCOME" or "NEW_NEED")
            {
                speaking = true;
                if (!await voice.InvokeAsync<bool>("playGreeting", lastSpeechKind, epoch) && epoch == voiceEpoch) speaking = false;
                return;
            }
            var result = await Api.SpeechAsync(lastSpeechKind == "RESULT" ? suggestions?.SpeechId : null, lastSpeechKind, request.Token);
            if (disposed || request.IsCancellationRequested || epoch != voiceEpoch || !audioEnabled || listening) return;
            if (!result.IsSuccess || result.Data is null) { voiceNotice = result.Message ?? "Chưa tải được giọng Việt. Bạn nhấn nghe lại nhé."; return; }
            voiceNotice = null;
            speaking = true;
            if (!await voice.InvokeAsync<bool>("play", result.Data.Audio, result.Data.ContentType, epoch) && epoch == voiceEpoch) speaking = false;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (JSException) { speaking = false; voiceNotice = "Chưa phát được giọng nói. Bạn có thể đọc phản hồi ở trên."; }
        finally
        {
            if (ReferenceEquals(speechCancellation, request)) { speechCancellation = null; preparingSpeech = false; }
            request.Dispose();
            if (!disposed && epoch == voiceEpoch) await ListenAgain();
            if (!disposed) StateHasChanged();
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        StopIdleWatch();
        if (liveMode) await StopLive();
        disposed = true; conversationActive = false; ++voiceEpoch; CancelRecommendation(); CancelSpeechRequest(); lifetime.Cancel();
        try
        {
            if (liveVoice is not null) { await liveVoice.InvokeVoidAsync("dispose"); await liveVoice.DisposeAsync(); }
            if (liveModule is not null) await liveModule.DisposeAsync();
            if (voice is not null) { await voice.InvokeVoidAsync("dispose"); await voice.DisposeAsync(); }
            if (module is not null) await module.DisposeAsync();
        }
        catch (Exception ex) when (ex is JSDisconnectedException or JSException or ObjectDisposedException) { }
        if (idleMonitor is not null) await idleMonitor;
        reference?.Dispose(); lifetime.Dispose();
    }
    private sealed record VoiceCapabilities(bool Recognition, bool Speech);
}
