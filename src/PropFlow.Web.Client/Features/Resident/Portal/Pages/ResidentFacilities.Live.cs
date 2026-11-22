using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;

namespace PropFlow.Web.Client.Features.Resident.Portal.Pages;

public partial class ResidentFacilities
{
    [Inject] private NavigationManager DashboardNavigation { get; set; } = default!;
    private bool liveMode, liveConnecting, liveEnding;
    private IJSObjectReference? liveModule, liveVoice;
    private CancellationTokenSource? liveConnection;
    private readonly Dictionary<string, CancellationTokenSource> liveTools = [];
    private string LiveStatusText => liveConnecting ? "Đang kết nối hội thoại trực tiếp…" : liveEnding ? "Đang chào tạm biệt để về trang chủ…" : busy ? "Đang tra cứu tiện ích chung cư…" : speaking ? "PropFlow đang nói · Bạn có thể nói chen" : listening ? "Mình đang nghe bạn…" : "Hội thoại đã dừng";

    private async Task<bool> InitializeLive()
    {
        liveModule = await JS.InvokeAsync<IJSObjectReference>("import", "./js/facility-live-voice.js?v=20261122-3");
        if (disposed) { await liveModule.DisposeAsync(); return true; }
        if (!await liveModule.InvokeAsync<bool>("capabilities")) { voiceNotice = "Trình duyệt chưa hỗ trợ hội thoại trực tiếp; đang dùng chế độ nói từng lượt. Bạn cũng có thể nhập nhu cầu."; return false; }
        liveVoice = await liveModule.InvokeAsync<IJSObjectReference>("create", reference);
        if (disposed) { await liveVoice.InvokeVoidAsync("dispose"); await liveVoice.DisposeAsync(); return true; }
        liveMode = true; entryConversationPending = false; voiceSupported = speechSupported = true;
        await StartLive();
        return true;
    }
    private async Task StartLive()
    {
        if (disposed || liveVoice is null || liveConnecting) return;
        await CancelVoice();
        var epoch = ++voiceEpoch;
        liveConnection?.Cancel();
        var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        liveConnection = request; liveConnecting = conversationActive = true; liveEnding = false; voiceNotice = error = null;
        StateHasChanged();
        try
        {
            var response = await Api.LiveAsync(request.Token);
            if (disposed || epoch != voiceEpoch || request.IsCancellationRequested) return;
            if (!response.IsSuccess || response.Data is null) { voiceNotice = response.Message ?? "Hội thoại trực tiếp chưa sẵn sàng. Bạn có thể nhập nhu cầu bên dưới."; await StopLive(); return; }
            var connected = await liveVoice.InvokeAsync<bool>("start", response.Data, epoch, audioEnabled);
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
        ++voiceEpoch; conversationActive = listening = speaking = busy = liveConnecting = false;
        liveEnding = false; liveConnection?.Cancel();
        foreach (var tool in liveTools.Values.ToArray()) tool.Cancel();
        liveTools.Clear();
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
    });
    [JSInvokable]
    public async Task<object?> OnLiveTool(int epoch, string id, string query)
    {
        object? response = null;
        await InvokeAsync(async () => {
            if (disposed || epoch != voiceEpoch || !conversationActive || liveTools.ContainsKey(id) || liveTools.Count >= 2 || id.Length > 200 || query.Trim().Length is < 2 or > 1200) return;
            var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            liveTools.Add(id, request);
            try
            {
                var result = await Api.LiveLookupAsync(query, request.Token);
                if (disposed || epoch != voiceEpoch || request.IsCancellationRequested) return;
                if (!result.IsSuccess || result.Data is null) { response = new { error = result.Message ?? "Chưa tra cứu được tiện ích. Không suy đoán dữ liệu." }; return; }
                suggestions = result.Data; response = result.Data; StateHasChanged();
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { }
            finally { if (liveTools.TryGetValue(id, out var current) && ReferenceEquals(current, request)) liveTools.Remove(id); request.Dispose(); }
        });
        return response;
    }
    private async Task SendLiveText(string text)
    {
        if (liveVoice is null || !conversationActive || liveConnecting) return;
        try { if (await liveVoice.InvokeAsync<bool>("sendText", text)) { lastSpokenNeed = text; need = ""; } }
        catch (JSException) { voiceNotice = "Chưa gửi được nội dung. Bạn thử kết nối lại nhé."; }
    }
}
