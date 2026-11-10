using PropFlow.Modules.Communication.Contracts;

namespace PropFlow.Modules.Communication.Application.SupportChat;

public sealed record SupportActor(Guid UserId, string Role, string Name, Guid? ResidentId = null, string TimeZoneId = "Asia/Ho_Chi_Minh");
public interface ISupportChatActors { Task<SupportActor?> FindAsync(Guid id, CancellationToken ct); }
public interface ISupportChatRealtime
{
    Task ChangedAsync(Guid conversationId, IReadOnlyCollection<Guid> users, CancellationToken ct);
    Task DraftAsync(Guid recipient, SupportDraft draft, CancellationToken ct);
    Task TypingAsync(Guid recipient, SupportTyping typing, CancellationToken ct);
}
public interface ISupportChat
{
    Task<SupportConversationPage> ListAsync(Guid actor, int page, CancellationToken ct);
    Task<SupportConversationView> GetAsync(Guid actor, Guid id, CancellationToken ct);
    Task<SupportMessagePage> MessagesAsync(Guid actor, Guid id, long? before, CancellationToken ct);
    Task<SupportConversationView> StartAsync(Guid actor, ChatSendRequest request, CancellationToken ct);
    Task<SupportMessageView> SendAsync(Guid actor, Guid id, ChatSendRequest request, CancellationToken ct);
    Task<SupportMessageView> MessageAsync(Guid actor, Guid id, Guid messageId, CancellationToken ct);
    Task<bool> RecallAsync(Guid actor, Guid id, Guid messageId, CancellationToken ct);
    Task<bool> RecallManyAsync(Guid actor, Guid id, ChatMessageSelection request, CancellationToken ct);
    Task<bool> HideAsync(Guid actor, Guid id, ChatMessageSelection request, CancellationToken ct);
    Task<bool> AcknowledgeAsync(Guid actor, Guid id, ChatMessageReceipt request, CancellationToken ct);
    Task<int> ForwardAsync(Guid actor, Guid id, ChatForwardRequest request, CancellationToken ct);
    Task<SupportConversationView> CloseAsync(Guid actor, Guid id, CancellationToken ct);
    Task<SupportAvailabilityView> AvailabilityAsync(Guid actor, CancellationToken ct);
    Task<SupportAvailabilityView> SetAvailabilityAsync(Guid actor, bool available, CancellationToken ct);
    Task ConnectedAsync(Guid actor, string connection, CancellationToken ct);
    Task HeartbeatAsync(Guid actor, string connection, CancellationToken ct);
    Task DisconnectedAsync(string connection, CancellationToken ct);
    Task DispatchAsync(CancellationToken ct);
    Task DraftAsync(Guid actor, Guid id, string stream, long sequence, string text, CancellationToken ct);
    Task TypingAsync(Guid actor, Guid id, bool typing, CancellationToken ct);
}
public sealed class SupportChatFailure(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
