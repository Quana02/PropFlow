using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PropFlow.Web.Client.Features.Facility.Services;

namespace PropFlow.Web.Client.Features.Resident.Portal.Pages;

public partial class ResidentFacilities : IAsyncDisposable
{
    [Inject] private FacilityRecommendationsApi Api { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    private const string Welcome = "Chào bạn, mình là trợ lý tiện ích PropFlow. Hôm nay bạn muốn làm gì? Mình sẽ cùng bạn tìm hoạt động và thời gian phù hợp nhé.";
    private ResidentFacilityCatalogue? catalogue;
    private FacilityRecommendationView? suggestions;
    private string need = "", interimText = "", assistantText = Welcome, previousNeed = "", voiceBase = "";
    private string? lastSpokenNeed, voiceNotice, error;
    private bool loading, busy, disposed, listening, speaking, preparingSpeech, voiceSupported, speechSupported, heardSpeech, audioEnabled = true;
    private int voiceEpoch;
    private IJSObjectReference? module, voice;
    private DotNetObjectReference<ResidentFacilities>? reference;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? speechCancellation;
    private string lastSpeechKind = "WELCOME";
    private string VisualState => busy || preparingSpeech ? "thinking" : listening ? "listening" : speaking ? "speaking" : "ready";
    private string StatusText => busy ? "Đang tìm điều phù hợp với bạn…" : preparingSpeech ? (lastSpeechKind is "WELCOME" or "NEW_NEED" ? "Đang kết nối trợ lý…" : "Đang tạo giọng tiếng Việt…") : listening ? "Mình đang nghe bạn…" : speaking ? "PropFlow đang nói…" : "Mình ở đây để giúp bạn";

    protected override Task OnInitializedAsync() => Load();
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        try
        {
            module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/facility-assistant-voice.js?v=20261117-2");
            if (disposed) { await module.DisposeAsync(); return; }
            var capabilities = await module.InvokeAsync<VoiceCapabilities>("capabilities");
            voiceSupported = capabilities.Recognition; speechSupported = capabilities.Speech;
            reference = DotNetObjectReference.Create(this);
            voice = await module.InvokeAsync<IJSObjectReference>("create", reference);
            if (disposed) { await voice.InvokeVoidAsync("dispose"); await voice.DisposeAsync(); reference.Dispose(); return; }
            if (!voiceSupported) voiceNotice = "Bạn có thể nhập bên dưới. Micro cần trình duyệt hỗ trợ như Chrome/Edge trên HTTPS hoặc localhost.";
            if (!speechSupported) voiceNotice = "Trình duyệt chưa phát được âm thanh. Bạn vẫn có thể nhập hoặc nói để xem gợi ý.";
            await Speak("WELCOME");
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
    }

    private async Task ToggleMicrophone()
    {
        if (voice is null || !voiceSupported || busy || disposed || catalogue?.Facilities.Length is not > 0) return;
        try
        {
            if (listening) { await voice.InvokeVoidAsync("stop"); return; }
            ++voiceEpoch; speaking = false; interimText = ""; voiceNotice = null; voiceBase = need.Trim(); heardSpeech = false;
            CancelSpeechRequest();
            listening = await voice.InvokeAsync<bool>("start", voiceEpoch);
            if (!listening) voiceNotice = "Chưa mở được micro. Hãy kiểm tra quyền micro hoặc nhập bên dưới.";
        }
        catch (JSException) { listening = false; voiceNotice = "Chưa mở được micro. Bạn có thể nhập nhu cầu bên dưới."; }
    }

    [JSInvokable]
    public Task OnVoiceResult(int epoch, string finalText, string interim) => InvokeAsync(() =>
    {
        if (disposed || epoch != voiceEpoch || !listening) return;
        heardSpeech |= !string.IsNullOrWhiteSpace(finalText);
        var combined = string.Join(" ", new[] { voiceBase, finalText.Trim() }.Where(x => x.Length > 0));
        need = combined.Length <= 1200 ? combined : combined[..1200];
        interimText = interim.Length <= 1200 ? interim : interim[..1200];
        StateHasChanged();
    });

    [JSInvokable]
    public Task OnVoiceEnded(int epoch, string failure) => InvokeAsync(async () =>
    {
        if (disposed || epoch != voiceEpoch) return;
        listening = false; interimText = "";
        if (failure.Length > 0)
        {
            voiceNotice = failure switch
            {
                "not-allowed" or "service-not-allowed" => "Bạn cần cho phép micro trong trình duyệt. Nội dung đã nhận vẫn được giữ lại.",
                "audio-capture" => "Chưa tìm thấy micro. Kiểm tra thiết bị hoặc nhập bên dưới nhé.",
                "no-speech" => "Mình chưa nghe rõ. Bạn có thể nói lại hoặc nhập bên dưới.",
                _ => "Giọng nói vừa bị gián đoạn. Bạn có thể sửa phần chữ đã nhận và gửi lại."
            };
        }
        else if (heardSpeech && need.Trim().Length >= 2) await Recommend();
        else voiceNotice = "Bạn nói rõ hơn một chút nhé, ví dụ: muốn đọc sách yên tĩnh.";
        if (!disposed) StateHasChanged();
    });

    [JSInvokable]
    public Task OnAssistantSpeech(int epoch, string state) => InvokeAsync(() =>
    {
        if (disposed || epoch != voiceEpoch) return;
        speaking = audioEnabled && state == "speaking";
        if (state == "paused") { listening = false; interimText = ""; CancelSpeechRequest(); }
        if (state == "unavailable") voiceNotice = "Chưa phát được âm thanh tiếng Việt. Bạn nhấn nghe lại nhé.";
        if (state == "playback-blocked") voiceNotice = "Trình duyệt đang chặn tự phát âm thanh. Nhấn Nghe lại để nghe giọng tiếng Việt.";
        StateHasChanged();
    });

    private async Task Recommend()
    {
        if (busy || listening || need.Trim().Length < 2 || catalogue?.Facilities.Length is not > 0) return;
        var input = need.Trim();
        var combined = previousNeed.Length > 0
            ? previousNeed + "\nBổ sung: " + input : input;
        if (combined.Length > 1200) { error = "Nội dung trò chuyện đã dài. Hãy chọn tìm cho nhu cầu khác và mô tả gọn lại nhé."; return; }
        ++voiceEpoch; speaking = false;
        busy = true; error = null; lastSpokenNeed = input;
        StateHasChanged();
        try
        {
            await CancelVoice();
            var result = await Api.RecommendAsync(combined, null, lifetime.Token);
            if (disposed) return;
            if (!result.IsSuccess || result.Data is null)
            {
                error = result.Message ?? "Mình chưa kết nối được. Bạn thử lại một lát nhé.";
                assistantText = "Mình chưa tìm được kết quả lúc này. Nội dung bạn nói vẫn ở đây để thử lại.";
                suggestions = null;
                need = combined;
                return;
            }
            suggestions = result.Data; previousNeed = combined; need = "";
            assistantText = FacilityAssistantReply.Create(suggestions);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { if (!disposed) busy = false; }
        if (!disposed && suggestions is not null) { StateHasChanged(); await Speak("RESULT"); }
    }

    private async Task Example(string text)
    {
        suggestions = null; previousNeed = ""; need = text;
        await Recommend();
    }
    private async Task NewNeed()
    {
        ++voiceEpoch; speaking = listening = false;
        await CancelVoice();
        suggestions = null; previousNeed = need = ""; lastSpokenNeed = error = null;
        assistantText = "Mình nghe đây. Lần này bạn muốn tìm hoạt động gì?";
        await Speak("NEW_NEED");
    }
    private async Task ToggleAudio()
    {
        audioEnabled = !audioEnabled;
        if (voice is null) return;
        if (!audioEnabled)
        {
            CancelSpeechRequest();
            speaking = false;
            try { await voice.InvokeVoidAsync("stopSpeaking"); } catch (JSException) { }
        }
        else await Speak();
    }
    private Task Replay() { audioEnabled = true; return Speak(); }
    private async Task ShowResults()
    {
        if (module is null) return;
        try { await module.InvokeVoidAsync("showResults"); } catch (JSException) { }
    }
    private async Task CancelVoice()
    {
        CancelSpeechRequest();
        if (voice is null) return;
        try { await voice.InvokeVoidAsync("cancel"); } catch (JSException) { speaking = false; listening = false; }
    }
    private void CancelSpeechRequest()
    {
        speechCancellation?.Cancel(); speechCancellation = null; preparingSpeech = false;
    }
    private async Task Speak(string? kind = null)
    {
        lastSpeechKind = kind ?? lastSpeechKind;
        if (!audioEnabled || !speechSupported || voice is null || disposed || listening) return;
        CancelSpeechRequest();
        var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        speechCancellation = request; var epoch = voiceEpoch;
        preparingSpeech = true; StateHasChanged();
        try
        {
            if (lastSpeechKind is "WELCOME" or "NEW_NEED")
            {
                await voice.InvokeAsync<bool>("playGreeting", lastSpeechKind, epoch);
                return;
            }
            var result = await Api.SpeechAsync(lastSpeechKind == "RESULT" ? suggestions?.SpeechId : null, lastSpeechKind, request.Token);
            if (disposed || request.IsCancellationRequested || epoch != voiceEpoch || !audioEnabled || listening) return;
            if (!result.IsSuccess || result.Data is null) { voiceNotice = result.Message ?? "Chưa tải được giọng Việt. Bạn nhấn nghe lại nhé."; return; }
            voiceNotice = null;
            await voice.InvokeAsync<bool>("play", result.Data.Audio, result.Data.ContentType, epoch);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (JSException) { speaking = false; voiceNotice = "Chưa phát được giọng nói. Bạn có thể đọc phản hồi ở trên."; }
        finally
        {
            if (ReferenceEquals(speechCancellation, request)) { speechCancellation = null; preparingSpeech = false; }
            request.Dispose();
            if (!disposed) StateHasChanged();
        }
    }
    public async ValueTask DisposeAsync()
    {
        disposed = true; ++voiceEpoch; CancelSpeechRequest(); lifetime.Cancel();
        try
        {
            if (voice is not null) { await voice.InvokeVoidAsync("dispose"); await voice.DisposeAsync(); }
            if (module is not null) await module.DisposeAsync();
        }
        catch (Exception ex) when (ex is JSDisconnectedException or JSException or ObjectDisposedException) { }
        reference?.Dispose(); lifetime.Dispose();
    }
    private sealed record VoiceCapabilities(bool Recognition, bool Speech);
}
