namespace PropFlow.Web.Client.Features.Communication.SupportChat;

// UI seam: browser transport is independent from the conversation rendering and input lifecycle.
public interface ISupportChatConnection
{
    bool Connected { get; }
    event Func<Guid, Task>? Changed;
    event Action? TransientChanged;
    event Action? StateChanged;
    Task EnsureAsync(CancellationToken ct = default);
    bool IsTyping(Guid id);
    Task PublishTyping(Guid id, bool value);
    Task ReleaseResidentAsync();
}
