using System.Security.Claims;

namespace PropFlow.Modules.Communication.Contracts;

public static class SupportChatPolicies
{
    public const string Use = "support-chat.use";
    public const string Manager = "support-chat.manager";
    public const string Resident = "support-chat.resident";
    public const string RateLimit = "support-chat-write";
    public const string ReceiptRateLimit = "support-chat-receipts";
    public static bool IsManager(ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true &&
        user.IsInRole("MANAGER") && user.HasClaim("permission", "MANAGE_OPERATIONS");
    public static bool IsResident(ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true &&
        user.IsInRole("RESIDENT") && user.HasClaim("permission", "USE_RESIDENT_SERVICES");
    public static bool CanUse(ClaimsPrincipal user) => IsManager(user) || IsResident(user);
}
public sealed record ChatSendRequest(Guid ClientMessageId, string Text, Guid? ReplyToId = null);
public sealed record ChatMessageSelection(Guid[] MessageIds);
public sealed record ChatMessageReceipt(Guid[] MessageIds, bool Seen);
public sealed record ChatForwardItem(Guid MessageId, Guid ClientMessageId);
public sealed record ChatForwardRequest(Guid DestinationId, ChatForwardItem[] Items);
public sealed record ChatAvailabilityRequest(bool Available);
public sealed record SupportConversationView(Guid Id, string Status, string ResidentName, string? ManagerName,
    bool ManagerOnline, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, SupportMessagePreview? LastMessage = null);
public sealed record SupportMessagePreview(string Kind, string Text, DateTimeOffset CreatedAt);
public sealed record SupportConversationPage(IReadOnlyList<SupportConversationView> Items, int Total, int Page, string TimeZoneId);
public sealed record SupportReplyPreview(Guid Id, string? SenderName, string? Text);
public sealed record SupportMessageView(Guid Id, long Sequence, string Kind, string SenderName, string Text, DateTimeOffset CreatedAt,
    SupportReplyPreview? Reply = null, bool Forwarded = false, DateTimeOffset? RecalledAt = null,
    DateTimeOffset? DeliveredAt = null, DateTimeOffset? SeenAt = null);
public sealed record SupportMessagePage(IReadOnlyList<SupportMessageView> Items, bool HasMore, string TimeZoneId);
public sealed record SupportAvailabilityView(bool Available, bool Connected, Guid? ActiveConversationId);
public sealed record SupportDraft(Guid ConversationId, string StreamId, long Sequence, string Text, DateTimeOffset At);
public sealed record SupportTyping(Guid ConversationId, bool Typing, DateTimeOffset At);
