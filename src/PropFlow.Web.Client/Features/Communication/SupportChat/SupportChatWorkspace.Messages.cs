using Microsoft.JSInterop;
using PropFlow.Modules.Communication.Contracts;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.Communication.SupportChat;

public partial class SupportChatWorkspace
{
    private enum MessageActionKind { Recall, Hide, Forward }
    private sealed record PendingMessageAction(Guid ConversationId, MessageActionKind Kind, Guid[] Ids,
        string Title, string Message, string ConfirmText, ChatForwardRequest? Forward = null);

    private readonly HashSet<Guid> selectedMessages = [];
    private readonly HashSet<Guid> delivered = [];
    private readonly HashSet<Guid> seen = [];
    private Guid? pendingReplyId, focusSelectionId;
    private SupportMessageView? replyTo, messageDetails;
    private PendingMessageAction? messageAction;
    private bool selecting, sending, sendFailed, focusReply, openDetails;
    private DateTimeOffset nextReceiptAttempt;

    private bool HasSelectedContent => selectedMessages.Count > 0 &&
        messages.Where(m => selectedMessages.Contains(m.Id)).All(m => m.RecalledAt is null);
    private bool CanForwardSelection => selectedMessages.Count <= 20 && HasSelectedContent;
    private bool CanRecallSelection => HasSelectedContent && messages.Where(m => selectedMessages.Contains(m.Id)).All(IsMine);

    private bool IsMine(SupportMessageView message) => message.Kind == (ForManager ? "MANAGER" : "RESIDENT");

    private static string ReceiptLabel(SupportMessageView message) => message.SeenAt is not null ? "Đã xem"
        : message.DeliveredAt is not null ? "Đã nhận" : "Đã gửi";

    private void ReplyTo(SupportMessageView message)
    {
        replyTo = message;
        sendFailed = false;
        focusReply = true;
    }

    private void CancelReply() { replyTo = null; sendFailed = false; }

    private void BeginSelection(Guid id)
    {
        selecting = true;
        selectedMessages.Add(id);
        focusSelectionId = id;
    }

    private void ToggleMessageSelection(Guid id)
    {
        if (!selectedMessages.Remove(id) && selectedMessages.Count < 50) selectedMessages.Add(id);
    }

    private void CancelSelection()
    {
        selecting = false;
        selectedMessages.Clear();
        focusSelectionId = null;
    }

    private void HideSelection() => PrepareHide(selectedMessages.ToArray());
    private Task ForwardSelection() => PrepareForward(selectedMessages.ToArray());
    private void RecallSelection()
    {
        if (CanRecallSelection) PrepareRecall(selectedMessages.ToArray());
    }

    private Task CopySelection() => HasSelectedContent
        ? CopyMessageText(string.Join("\n\n", messages.Where(m => selectedMessages.Contains(m.Id)).Select(m => m.Text)))
        : Task.CompletedTask;

    private async Task CopyMessageText(string content)
    {
        if (busy || disposed || identityChanged || workspaceModule is null) return;
        var feedback = Toasts.BeginOperation(toastLifetime);
        try
        {
            await workspaceModule.InvokeVoidAsync("copyMessageText", lifetime.Token, content);
            feedback.Publish(() => Toasts.Success("Đã sao chép tin nhắn."));
        }
        catch (JSException)
        {
            feedback.Publish(() => Toasts.Error("Không thể sao chép tin nhắn. Vui lòng thử lại."));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    private void PrepareHide(Guid[] ids)
    {
        if (selected is null || ids.Length == 0) return;
        messageAction = new(selected.Id, MessageActionKind.Hide, ids, "Xóa ở phía tôi?",
            $"Ẩn {ids.Length} tin nhắn khỏi lịch sử của bạn. Người bên kia vẫn xem được tin nhắn.", "Xóa ở phía tôi");
    }

    private void PrepareRecall(Guid[] ids)
    {
        if (selected is null) return;
        var title = ids.Length == 1 ? "Thu hồi tin nhắn?" : $"Thu hồi {ids.Length} tin nhắn?";
        messageAction = new(selected.Id, MessageActionKind.Recall, ids, title,
            "Nội dung các tin đã chọn sẽ được ẩn với cả hai bên trong hội thoại này.", "Thu hồi");
    }

    private async Task PrepareForward(Guid[] ids)
    {
        if (busy || selected is null || ids.Length is < 1 or > 20) return;
        var sourceId = selected.Id;
        var feedback = Toasts.BeginOperation(toastLifetime);
        busy = true;
        try
        {
            var result = await Api.List(1, lifetime.Token);
            if (!feedback.IsCurrent || selected?.Id != sourceId) return;
            if (!result.IsSuccess) { feedback.Publish(() => OperationError(result)); return; }
            // Each account has at most one open conversation in this support workflow.
            var destination = result.Data?.Items.FirstOrDefault(item => item.Status is "ACTIVE" or "WAITING");
            if (destination is null)
            {
                feedback.Publish(() => Toasts.Info("Bạn chưa có hội thoại đang mở để chuyển tiếp."));
                return;
            }
            var recipient = ForManager ? destination.ResidentName : destination.ManagerName ?? "Ban quản lý";
            var request = new ChatForwardRequest(destination.Id, ids.Select(id => new ChatForwardItem(id, Guid.NewGuid())).ToArray());
            messageAction = new(sourceId, MessageActionKind.Forward, ids, "Chuyển tiếp tin nhắn?",
                $"Gửi {ids.Length} tin nhắn đến hội thoại với {recipient}.", "Chuyển tiếp", request);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { busy = false; }
    }

    private void CancelMessageAction() { if (!busy) messageAction = null; }

    private async Task ConfirmMessageAction()
    {
        if (busy || messageAction is not { } action || selected?.Id != action.ConversationId) return;
        var feedback = Toasts.BeginOperation(toastLifetime);
        busy = true;
        try
        {
            ApiResult result = action.Kind switch
            {
                MessageActionKind.Recall when action.Ids.Length == 1 => await Api.Recall(action.ConversationId, action.Ids[0], lifetime.Token),
                MessageActionKind.Recall => await Api.RecallMany(action.ConversationId, new(action.Ids), lifetime.Token),
                MessageActionKind.Hide => await Api.Hide(action.ConversationId, new(action.Ids), lifetime.Token),
                _ => await Api.Forward(action.ConversationId, action.Forward!, lifetime.Token)
            };
            if (!feedback.IsCurrent || identityChanged) return;
            if (!result.IsSuccess) { feedback.Publish(() => Toasts.Error(result.Message)); return; }
            if (action.Kind == MessageActionKind.Hide) messages.RemoveAll(message => action.Ids.Contains(message.Id));
            messageAction = null;
            CancelSelection();
            await Reload();
            feedback.Publish(() => Toasts.Success(action.Kind switch
            {
                MessageActionKind.Recall => "Đã thu hồi tin nhắn.",
                MessageActionKind.Hide => "Đã xóa tin nhắn ở phía bạn.",
                _ => "Đã chuyển tiếp tin nhắn."
            }));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { busy = false; }
    }

    private async Task OpenMessageDetails(Guid id)
    {
        if (busy || selected is null) return;
        var conversationId = selected.Id;
        var feedback = Toasts.BeginOperation(toastLifetime);
        busy = true;
        try
        {
            var result = await Api.Message(conversationId, id, lifetime.Token);
            if (!feedback.IsCurrent || selected?.Id != conversationId) return;
            if (result.IsSuccess)
            {
                messageDetails = result.Data;
                openDetails = messageDetails is not null;
            }
            else feedback.Publish(() => OperationError(result));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { busy = false; }
    }

    private async Task CloseMessageDetails()
    {
        if (workspaceModule is not null) await workspaceModule.InvokeVoidAsync("closeMessageDetails", detailsDialog);
        messageDetails = null;
    }

    private string DetailsTime(DateTimeOffset timestamp)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var local = TimeZoneInfo.ConvertTime(timestamp, zone);
        return local.Date == TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).Date
            ? $"Hôm nay · {local:HH:mm}"
            : $"{local:dd/MM/yyyy} · {local:HH:mm}";
    }

    private void ResetMessageTools()
    {
        CancelSelection();
        messageAction = null;
        messageDetails = replyTo = null;
        openDetails = false;
        pendingReplyId = null;
        sendFailed = false;
        focusReply = false;
        delivered.Clear();
        seen.Clear();
        nextReceiptAttempt = default;
    }

    private void SyncMessageTools()
    {
        var ids = messages.Select(m => m.Id).ToHashSet();
        selectedMessages.IntersectWith(ids);
        delivered.IntersectWith(ids);
        seen.IntersectWith(ids);
        if (replyTo is { } target)
        {
            var current = messages.FirstOrDefault(m => m.Id == target.Id && m.RecalledAt is null);
            if (current is null) replyTo = target with { Text = "Tin nhắn gốc không còn hiển thị" };
        }
        if (messageDetails is { } detail)
            messageDetails = messages.FirstOrDefault(m => m.Id == detail.Id);
    }

    private async Task SendReceipts()
    {
        if (disposed || identityChanged || selected is not { } chat || workspaceModule is null ||
            DateTimeOffset.UtcNow < nextReceiptAttempt) return;

        var incoming = messages.Where(m => m.Kind != "SYSTEM" && !IsMine(m) && m.RecalledAt is null).ToArray();
        var toDeliver = incoming.Where(m => m.DeliveredAt is null && !delivered.Contains(m.Id)).Take(50).Select(m => m.Id).ToArray();
        if (toDeliver.Length > 0)
        {
            var result = await Api.Acknowledge(chat.Id, new(toDeliver, Seen: false), lifetime.Token);
            if (selected?.Id != chat.Id || identityChanged || disposed) return;
            if (!result.IsSuccess) { nextReceiptAttempt = DateTimeOffset.UtcNow.AddSeconds(5); return; }
            delivered.UnionWith(toDeliver);
        }
        if (confirmation is not null || messageAction is not null || messageDetails is not null ||
            !incoming.Any(m => m.SeenAt is null && !seen.Contains(m.Id))) return;
        var visible = (await workspaceModule.InvokeAsync<Guid[]>("visibleMessages", lifetime.Token, thread)).ToHashSet();
        if (selected?.Id != chat.Id || identityChanged || disposed ||
            confirmation is not null || messageAction is not null || messageDetails is not null) return;
        var toSee = incoming.Where(m => m.SeenAt is null && !seen.Contains(m.Id) && visible.Contains(m.Id))
            .Take(50).Select(m => m.Id).ToArray();
        if (toSee.Length == 0) return;
        var seenResult = await Api.Acknowledge(chat.Id, new(toSee, Seen: true), lifetime.Token);
        if (selected?.Id != chat.Id || identityChanged || disposed) return;
        if (seenResult.IsSuccess) seen.UnionWith(toSee);
        else nextReceiptAttempt = DateTimeOffset.UtcNow.AddSeconds(5);
    }
}
