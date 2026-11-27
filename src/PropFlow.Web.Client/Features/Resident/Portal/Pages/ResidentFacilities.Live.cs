using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;
using PropFlow.Web.Client.Features.Facility.Services;

namespace PropFlow.Web.Client.Features.Resident.Portal.Pages;

public partial class ResidentFacilities
{
    [Inject] private NavigationManager DashboardNavigation { get; set; } = default!;
    private bool liveMode, liveConnecting, liveEnding;
    private IJSObjectReference? liveModule, liveVoice;
    private CancellationTokenSource? liveConnection;
    private readonly Dictionary<string, CancellationTokenSource> liveTools = [];
    private FacilityRecommendationView? liveCandidates;
    private string? liveLookupId;
    private string LiveStatusText => liveConnecting ? "Đang kết nối hội thoại trực tiếp…" : liveEnding ? "Đang chào tạm biệt để về trang chủ…" : requestSubmitting ? "Đang gửi yêu cầu của bạn…" : requestPreparing ? "Đang kiểm tra bản nháp…" : busy ? "Đang tra cứu thông tin chung cư…" : speaking ? "PropFlow đang nói · Bạn có thể nói chen" : listening ? "Mình đang nghe bạn…" : "Hội thoại đã dừng";

    private async Task<bool> InitializeLive()
    {
        liveModule = await JS.InvokeAsync<IJSObjectReference>("import", "./js/facility-live-voice.js?v=20261123-5");
        if (disposed) { await liveModule.DisposeAsync(); return true; }
        if (!await liveModule.InvokeAsync<bool>("capabilities")) { voiceNotice = "Trình duyệt chưa hỗ trợ hội thoại trực tiếp; đang dùng chế độ nói từng lượt. Bạn cũng có thể nhập nhu cầu."; return false; }
        liveVoice = await liveModule.InvokeAsync<IJSObjectReference>("create", reference);
        if (disposed) { await liveVoice.InvokeVoidAsync("dispose"); await liveVoice.DisposeAsync(); return true; }
        liveMode = true; entryConversationPending = false; voiceSupported = speechSupported = true;
        assistantText = "Chào bạn, mình là trợ lý PropFlow. Bạn muốn khám phá tiện ích, hỏi nội quy hay cùng mình tạo yêu cầu cho Ban quản lý?";
        await StartLive();
        return true;
    }
    private async Task StartLive()
    {
        if (disposed || liveVoice is null || liveConnecting || idleExiting) return;
        StopIdleWatch();
        await CancelVoice();
        var epoch = ++voiceEpoch;
        liveConnection?.Cancel();
        var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        liveConnection = request; liveConnecting = conversationActive = true; liveEnding = false; voiceNotice = error = null;
        userInputTurn = draftDisplayedAtTurn = draftEvidenceTurn = 0; latestUserInput = draftEvidenceText = "";
        StateHasChanged();
        try
        {
            var response = await Api.LiveAsync(request.Token);
            if (disposed || epoch != voiceEpoch || request.IsCancellationRequested) return;
            if (!response.IsSuccess || response.Data is null) { voiceNotice = response.Message ?? "Hội thoại trực tiếp chưa sẵn sàng. Bạn có thể nhập nhu cầu bên dưới."; await StopLive(); return; }
            var connected = await liveVoice.InvokeAsync<bool>("start", response.Data, epoch, audioEnabled);
            if (!disposed && epoch == voiceEpoch && connected && conversationActive) StartIdleWatch();
            if (!disposed && epoch == voiceEpoch && connected && conversationActive && pendingHandoff is not null)
            { liveConnecting = false; await SendPendingHandoff(); }
            if (!disposed && epoch == voiceEpoch && !connected && conversationActive)
            { voiceNotice = "Chưa mở được hội thoại trực tiếp. Kiểm tra quyền micro và thử kết nối lại; bạn vẫn có thể nhập nhu cầu."; await StopLive(); }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (JSException) { if (!disposed && epoch == voiceEpoch) { voiceNotice = "Chưa kết nối được micro hoặc âm thanh. Bạn có thể nhập nhu cầu bên dưới."; await StopLive(); } }
        finally
        {
            if (ReferenceEquals(liveConnection, request)) { liveConnection = null; liveConnecting = false; }
            request.Dispose();
            if (!disposed) StateHasChanged();
        }
    }
    private async Task StopLive()
    {
        StopIdleWatch();
        ++voiceEpoch; conversationActive = listening = speaking = busy = liveConnecting = false;
        liveEnding = false; liveConnection?.Cancel();
        foreach (var tool in liveTools.Values.ToArray()) tool.Cancel();
        liveTools.Clear();
        liveCandidates = null; liveLookupId = null;
        if (liveVoice is not null) try { await liveVoice.InvokeVoidAsync("stop"); } catch (JSException) { }
    }
    [JSInvokable]
    public Task OnLiveState(int epoch, string state) => InvokeAsync(async () =>
    {
        if (disposed || epoch != voiceEpoch || !conversationActive) return;
        if (state is "connecting" or "listening" or "speaking" or "searching" or "ending")
        {
            if (state == "ending") liveEnding = true;
            else if (state == "listening") liveEnding = false;
            liveConnecting = state == "connecting"; listening = state == "listening";
            speaking = state == "speaking"; busy = state == "searching";
        }
        else
        {
            voiceNotice = state switch {
                "not-allowed" => "Bạn cần cho phép micro để trò chuyện trực tiếp. Bạn vẫn có thể nhập bên dưới.",
                "paused" => "Hội thoại đã dừng khi bạn rời trang Tiện ích AI. Bấm kết nối lại khi sẵn sàng.",
                "expired" => "Phiên hội thoại đã hết thời gian. Bạn có thể bắt đầu phiên mới.",
                "playback-blocked" => "Trình duyệt chưa cho phát âm thanh. Bấm kết nối lại để tiếp tục.",
                _ => "Kết nối hội thoại bị gián đoạn. Bạn có thể kết nối lại hoặc nhập nhu cầu để tìm tiện ích."
            };
            await StopLive();
        }
        StateHasChanged();
    });
    [JSInvokable]
    public Task OnLiveEnded(int epoch) => InvokeAsync(async () =>
    {
        if (disposed || epoch != voiceEpoch || !conversationActive || !liveMode || !liveEnding) return;
        await StopLive();
        DashboardNavigation.NavigateTo("/resident");
    });
    [JSInvokable]
    public Task OnLiveTranscript(int epoch, string role, string text) => InvokeAsync(() =>
    {
        if (disposed || epoch != voiceEpoch || !conversationActive) return;
        if (role == "user") lastSpokenNeed = text[..Math.Min(text.Length, 1200)];
        else if (role == "assistant") assistantText = text[..Math.Min(text.Length, 6000)];
        StateHasChanged();
    });
    [JSInvokable]
    public Task OnLiveToolCancelled(int epoch, string id) => InvokeAsync(() =>
    {
        if (epoch == voiceEpoch && liveTools.Remove(id, out var request)) request.Cancel();
        if (epoch == voiceEpoch && liveLookupId == id) { liveCandidates = null; liveLookupId = null; }
    });
    [JSInvokable]
    public async Task<object?> OnLiveTool(int epoch, string id, string query)
    {
        object? response = null;
        await InvokeAsync(async () => {
            if (disposed || epoch != voiceEpoch || !conversationActive || idleExiting || IdleExpired || requestSubmitting || liveTools.ContainsKey(id) || liveTools.Count >= 2 || id.Length > 200 || query.Trim().Length is < 2 or > 1200) return;
            var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            liveTools.Add(id, request);
            // Candidate retrieval is not a recommendation. Clear old cards until the model selects a verified subset.
            SetAssistantPane("facilities", id);
            liveLookupId = id; liveCandidates = null; suggestions = null; StateHasChanged();
            try
            {
                var result = await Api.LiveLookupAsync(query, request.Token);
                if (disposed || epoch != voiceEpoch || request.IsCancellationRequested || liveLookupId != id) return;
                if (!result.IsSuccess || result.Data is null) { response = new { error = result.Message ?? "Chưa tra cứu được tiện ích. Không suy đoán dữ liệu." }; return; }
                liveCandidates = result.Data;
                response = new { lookupId = id, candidates = result.Data };
                if (result.Data.Suggestions.Length == 0) suggestions = result.Data;
                StateHasChanged();
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { }
            finally { if (liveTools.TryGetValue(id, out var current) && ReferenceEquals(current, request)) liveTools.Remove(id); request.Dispose(); }
        });
        return response;
    }
    [JSInvokable]
    public async Task<object?> OnLiveSelection(int epoch, string lookupId, string[] facilityIds)
    {
        object? response = null;
        await InvokeAsync(() => { response = SelectLiveFacilities(epoch, lookupId, facilityIds); });
        return response;
    }
    private object SelectLiveFacilities(int epoch, string lookupId, string[] facilityIds)
    {
        if (disposed || epoch != voiceEpoch || !conversationActive || liveEnding || liveCandidates is null || lookupId != liveLookupId)
            return new { error = "Tra cứu đã hết hiệu lực. Hãy tra cứu lại trước khi gợi ý." };
        if (facilityIds is null || facilityIds.Length > 3 || facilityIds.Distinct().Count() != facilityIds.Length)
            return new { error = "Chỉ chọn tối đa ba tiện ích khác nhau từ kết quả mới nhất." };
        var selected = new List<FacilitySuggestion>();
        foreach (var value in facilityIds)
        {
            if (!Guid.TryParse(value, out var id) || liveCandidates.Suggestions.FirstOrDefault(x => x.Facility.Id == id) is not { } candidate || selected.Any(x => x.Facility.Id == id))
                return new { error = "Tiện ích không thuộc kết quả tra cứu mới nhất. Không suy đoán dữ liệu." };
            selected.Add(candidate);
        }
        suggestions = liveCandidates.Suggestions.Length == 0 ? liveCandidates : liveCandidates with { Suggestions = selected.ToArray(), Mode = selected.Count > 0 ? "LOOKUP" : "NO_MATCH",
            Message = selected.Count > 0 ? liveCandidates.Message : "Chưa tìm thấy tiện ích phù hợp nhu cầu. Bạn có thể nói rõ hơn hoặc liên hệ BQL." };
        StateHasChanged();
        return new { displayed = suggestions };
    }
    private async Task SendLiveText(string text)
    {
        if (liveVoice is null || !conversationActive || liveConnecting) return;
        try { if (await liveVoice.InvokeAsync<bool>("sendText", text)) { lastSpokenNeed = text; need = ""; } }
        catch (JSException) { voiceNotice = "Chưa gửi được nội dung. Bạn thử kết nối lại nhé."; }
    }
}
