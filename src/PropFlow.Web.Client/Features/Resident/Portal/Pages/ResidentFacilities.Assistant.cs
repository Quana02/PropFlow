using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PropFlow.Modules.AiChatbot.Contracts;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Web.Client.Features.AiChatbot.Services;
using PropFlow.Web.Client.Features.Resident.Portal.Models;
using PropFlow.Web.Client.Features.Resident.Portal.Services;

namespace PropFlow.Web.Client.Features.Resident.Portal.Pages;

public partial class ResidentFacilities
{
    [Inject] private ResidentChatApiClient DocumentsApi { get; set; } = default!;
    [Inject] private IResidentServiceRequestApiClient RequestsApi { get; set; } = default!;
    [Inject] private ResidentAssistantHandoff Handoff { get; set; } = default!;
    [CascadingParameter] public ResidentPortalProfileContext? ProfileContext { get; set; }
    [SupplyParameterFromQuery(Name = "conversation")] public string? ConversationHandoff { get; set; }
    private string? loadedHandoff, pendingHandoff, assistantOperation;
    private ResidentChatReply? assistantDocuments;
    private PublishedKnowledgeSource? assistantSource;
    private bool loadingAssistantSource, requestPreparing, requestSubmitting;
    private Guid? draftId;
    private string draftDescription = "";
    private ResidentServiceRequestDraftReview? draftReview;
    private ResidentServiceRequestCreatedResponse? submittedRequest;
    private int userInputTurn, draftDisplayedAtTurn, draftEvidenceTurn;
    private string latestUserInput = "";
    private string draftEvidenceText = "";
    private bool HasAssistantContent => draftId is not null || assistantDocuments is not null || suggestions?.Suggestions.Length > 0;
    private bool DraftReady => !idleExiting && !IdleExpired && draftId is not null && draftDescription.Trim().Length is >= 10 and <= 3500 &&
        draftReview is { Available: true, MissingInformation.Count: 0 } && !requestPreparing && !requestSubmitting && submittedRequest is null;

    private async Task ReceiveHandoff()
    {
        if (ConversationHandoff is null || ConversationHandoff == loadedHandoff) return;
        loadedHandoff = ConversationHandoff; pendingHandoff = Handoff.Take();
        if (pendingHandoff is not null && liveMode && conversationActive && !liveConnecting) await SendPendingHandoff();
    }
    private async Task SendPendingHandoff()
    {
        if (pendingHandoff is not { } topic) return;
        pendingHandoff = null;
        await SendLiveText("Mình chuyển từ chatbot tài liệu sang để hỏi rõ hơn về câu này: " + topic);
    }
    private void SetAssistantPane(string pane, string operation)
    {
        assistantOperation = operation; assistantDocuments = null; assistantSource = null;
        if (pane != "request") { draftId = null; draftDescription = ""; draftReview = null; submittedRequest = null; }
        if (pane != "facilities") { suggestions = null; liveCandidates = null; liveLookupId = null; }
    }
    private async Task<object?> RunAssistantTool(int epoch, string id, Func<CancellationToken, Task<object?>> action)
    {
        object? response = null;
        await InvokeAsync(async () => {
            if (disposed || epoch != voiceEpoch || !conversationActive || idleExiting || IdleExpired || liveEnding || requestSubmitting || liveTools.ContainsKey(id) || liveTools.Count >= 2 || id.Length > 200) return;
            using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            liveTools.Add(id, request);
            try { response = await action(request.Token); }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { }
            finally { if (liveTools.TryGetValue(id, out var current) && ReferenceEquals(current, request)) liveTools.Remove(id); }
            if (!disposed) StateHasChanged();
        });
        return response;
    }
    [JSInvokable]
    public Task OnLiveUserTurn(int epoch, int turn, string text) => InvokeAsync(() => {
        if (disposed || epoch != voiceEpoch || !conversationActive || turn < userInputTurn || text.Length > 3500) return;
        if (!string.IsNullOrWhiteSpace(text)) MarkIdleActivity();
        if (IdleExpired || idleExiting) return;
        userInputTurn = turn; latestUserInput = text;
    });
    [JSInvokable]
    public Task<object?> OnLiveDocuments(int epoch, string id, string question) => RunAssistantTool(epoch, id, async ct => {
        if (question.Trim().Length is < 2 or > 1000) return new { error = "Câu hỏi cần từ 2 đến 1.000 ký tự." };
        SetAssistantPane("documents", id); StateHasChanged();
        var result = await DocumentsApi.LookupAsync(question, ct);
        if (disposed || epoch != voiceEpoch || assistantOperation != id || ct.IsCancellationRequested) return null;
        if (!result.IsSuccess || result.Data is null) { error = result.Message; return new { error = result.Message ?? "Chưa tra cứu được tài liệu. Không tự tạo nội quy." }; }
        assistantDocuments = result.Data; error = null;
        return new { documents = result.Data };
    });
    [JSInvokable]
    public Task<object?> OnLivePrepareRequest(int epoch, string id, string description, bool newRequest = false) => RunAssistantTool(epoch, id, async ct => {
        if (description.Trim().Length is < 10 or > 3500) return new { error = "Hãy thu thập mô tả rõ hơn, từ 10 đến 3500 ký tự, trước khi tạo bản nháp." };
        if (draftId is not null && newRequest && !AssistantSubmissionIntent.IsExplicitNewRequest(latestUserInput))
            return new { error = "Cư dân chưa yêu cầu tạo đơn khác. Dùng update_request để bổ sung đúng bản nháp hiện tại, không xóa các món đồ đã nêu.", draftId };
        if (draftId is not null && !newRequest)
        {
            if (draftDescription == description.Trim() && (draftReview is not null || submittedRequest is not null) &&
                (submittedRequest is not null || userInputTurn == draftEvidenceTurn && latestUserInput == draftEvidenceText || AssistantSubmissionIntent.IsExplicitSend(latestUserInput))) return DraftToolResult();
            return new { error = "Bản nháp đã tồn tại; chưa cập nhật nội dung. Gọi update_request với draftId đang có để đưa lời bổ sung mới của cư dân vào, không thay danh sách món đồ bằng mô tả chung chung.", draftId, residentDetails = latestUserInput };
        }
        SetAssistantPane("request", id);
        draftId = Guid.NewGuid(); draftDescription = description.Trim(); draftReview = null; submittedRequest = null;
        draftDisplayedAtTurn = draftEvidenceTurn = userInputTurn; draftEvidenceText = latestUserInput; error = null; StateHasChanged();
        await ReviewDraft(ct);
        if (disposed || epoch != voiceEpoch || assistantOperation != id || ct.IsCancellationRequested) return null;
        return DraftToolResult();
    });
    [JSInvokable]
    public Task<object?> OnLiveUpdateRequest(int epoch, string id, string currentDraftId) => RunAssistantTool(epoch, id, async ct => {
        if (!Guid.TryParse(currentDraftId, out var key) || key != draftId || submittedRequest is not null)
            return new { error = "Bản nháp không còn hiện hành hoặc đã gửi. Không sửa đơn đã gửi." };
        var details = latestUserInput.Trim();
        if (userInputTurn < draftEvidenceTurn || userInputTurn == draftEvidenceTurn && latestUserInput == draftEvidenceText || details.Length < 2 || AssistantSubmissionIntent.IsExplicitSend(details) ||
            System.Text.RegularExpressions.Regex.IsMatch(details, @"^(?:ok|okay|alo(?:\s+alo)?|cảm ơn|chưa gửi|đừng gửi|khoan|tạm biệt)[\s.!]*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return new { error = "Chưa có thông tin bổ sung mới. Không tự thêm nội dung, không coi xác nhận gửi là dữ kiện của đơn." };
        if (userInputTurn == draftEvidenceTurn && latestUserInput.StartsWith(draftEvidenceText, StringComparison.Ordinal))
            details = latestUserInput[draftEvidenceText.Length..].Trim();
        if (details.Length < 2) return new { error = "Chưa có thông tin bổ sung mới." };
        var updated = draftDescription + "\nBổ sung: " + details;
        if (updated.Length > 3500) return new { error = "Bản nháp đã dài. Mời cư dân sửa trực tiếp nội dung, không tự xóa các dữ kiện đã có." };
        SetAssistantPane("request", id);
        draftId = Guid.NewGuid(); draftDescription = updated; draftReview = null;
        draftDisplayedAtTurn = draftEvidenceTurn = userInputTurn; draftEvidenceText = latestUserInput; error = null; StateHasChanged();
        await ReviewDraft(ct);
        if (disposed || epoch != voiceEpoch || assistantOperation != id || ct.IsCancellationRequested) return null;
        return DraftToolResult();
    });
    private object DraftToolResult() => submittedRequest is { } receipt ? new { submitted = true, request = receipt } : (object)new {
        draftId, description = draftDescription, review = draftReview, ready = DraftReady,
        instruction = "Đây là nội dung thật đang hiển thị. Chỉ nói đã cập nhật khi mô tả này thực sự chứa lời bổ sung. Nếu còn thiếu thì chỉ hỏi phần thiếu, không bắt đổi tên món đồ sang nhóm đồ. Khi đầy đủ, mời cư dân đọc lại và xác nhận gửi trong lượt mới; không gửi ngay trong lượt tạo nháp." };
    private async Task ReviewDraft(CancellationToken ct)
    {
        var current = draftId; var text = draftDescription;
        requestPreparing = true;
        try
        {
            var result = await RequestsApi.CheckDraftAsync(new(text), ct);
            if (disposed || ct.IsCancellationRequested || draftId != current || draftDescription != text) return;
            draftReview = result.IsSuccess ? result.Data : null;
            draftDisplayedAtTurn = userInputTurn;
            error = result.IsSuccess ? null : result.Message;
        }
        finally { requestPreparing = false; }
    }
    private async Task ReviewEditedDraft()
    {
        if (requestPreparing || requestSubmitting || submittedRequest is not null || draftDescription.Trim().Length is < 10 or > 3500) return;
        try { await ReviewDraft(lifetime.Token); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }
    private void EditDraft(ChangeEventArgs input)
    {
        MarkIdleActivity();
        if (requestSubmitting || submittedRequest is not null || IdleExpired || idleExiting) return;
        draftDescription = input.Value?.ToString() ?? ""; draftId = Guid.NewGuid(); draftReview = null;
        draftDisplayedAtTurn = draftEvidenceTurn = userInputTurn; draftEvidenceText = latestUserInput;
    }
    [JSInvokable]
    public async Task<object?> OnLiveSubmitRequest(int epoch, string id)
    {
        object? result = null;
        await InvokeAsync(async () => {
            if (disposed || epoch != voiceEpoch || !conversationActive || idleExiting || IdleExpired || liveEnding || !Guid.TryParse(id, out var key) || key != draftId) return;
            if (submittedRequest is not null) { result = DraftToolResult(); return; }
            if (userInputTurn <= draftDisplayedAtTurn || !AssistantSubmissionIntent.IsExplicitSend(latestUserInput))
            { result = new { error = "Chưa có lời xác nhận gửi trong lượt mới sau khi hiện bản nháp. Hỏi cư dân có muốn gửi bản nháp đang xem; không tự gửi." }; return; }
            result = await SubmitDraft(key);
        });
        return result;
    }
    private async Task SendDraftByButton() { if (draftId is { } key) await SubmitDraft(key); }
    private async Task<object> SubmitDraft(Guid key)
    {
        if (submittedRequest is not null) return DraftToolResult();
        if (!DraftReady || draftId != key) return new { error = "Bản nháp chưa được kiểm tra đầy đủ hoặc đang gửi. Hãy bổ sung thông tin đang hiển thị." };
        requestSubmitting = true; error = null; StateHasChanged();
        try
        {
            var result = await RequestsApi.CreateAsync(new(null, null, draftDescription.Trim(), null, null,
                draftReview!.PreferredDate, draftReview.PreferredTimeCode, SubmissionId: key), lifetime.Token);
            if (!disposed && draftId == key)
            {
                if (result.IsSuccess && result.Data is not null) submittedRequest = result.Data;
                else error = result.Message ?? "Chưa xác nhận được kết quả gửi. Hãy thử lại bản nháp này; hệ thống sẽ kiểm tra trùng.";
            }
            return result.IsSuccess && result.Data is not null ? new { submitted = true, request = result.Data } : (object)new { error = result.Message ?? "Chưa gửi thành công. Không thông báo đã gửi." };
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return new { error = "Trang đã đóng. Không suy đoán kết quả gửi." }; }
        finally { requestSubmitting = false; if (!disposed) StateHasChanged(); }
    }
    private async Task ReadAssistantSource(ChatCitation citation)
    {
        var current = assistantDocuments; loadingAssistantSource = true; error = null;
        try
        {
            var result = await DocumentsApi.SourceAsync(citation.DocumentId, citation.Revision, lifetime.Token);
            if (disposed || !ReferenceEquals(assistantDocuments, current)) return;
            assistantSource = result.IsSuccess ? result.Data : null;
            if (!result.IsSuccess) { assistantDocuments = null; error = result.Message; }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { loadingAssistantSource = false; }
    }
}
