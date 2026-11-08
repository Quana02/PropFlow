namespace PropFlow.Web.Client.Shared.Feedback;

public sealed record ToastMessage(Guid Id, string Title, string Message, string Severity, int DurationMs);

// Presentation state only. Timers belong to the rendered toast components.
public sealed class ToastService
{
    private long navigationGeneration;
    private readonly List<ToastMessage> messages = [];
    private readonly List<ToastMessage> waiting = [];
    private (ToastMessage Message, string Path, DateTimeOffset ExpiresAt)? pending;
    public event Action? Changed;
    public IReadOnlyList<ToastMessage> Messages => messages.ToArray();

    public ToastOperation BeginOperation(params ToastLifetime[] lifetimes)
        => new(this, navigationGeneration, lifetimes);

    internal bool IsCurrent(long generation) => generation == navigationGeneration;

    public void Show(string title, string message = "", string severity = "info", int? durationMs = null)
    {
        var next = Create(title, message, severity, durationMs);
        var duplicate = messages.Concat(waiting).FirstOrDefault(item =>
            item.Title == next.Title && item.Message == next.Message && item.Severity == next.Severity);
        if (duplicate is not null)
        {
            if (duplicate.DurationMs == 0 || next.DurationMs > 0) return;
            // An explicit persistent notification upgrades the existing transient copy.
            messages.Remove(duplicate);
            waiting.Remove(duplicate);
        }
        Add(next);
        Changed?.Invoke();
    }

    public void Notify(string? message, string severity = "info", string? title = null)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        severity = severity.ToLowerInvariant();
        Show(title ?? (severity switch { "success" => "Thành công", "error" => "Không thể hoàn tất", "warning" => "Lưu ý", _ => "Thông tin" }), message, severity);
    }
    public void Success(string? message) => Notify(message, "success");
    public void Error(string? message) => Notify(string.IsNullOrWhiteSpace(message) ? "Chưa thể xử lý yêu cầu. Vui lòng thử lại." : message, "error");
    public void Warning(string? message) => Notify(message, "warning");
    public void Info(string? message) => Notify(message, "info");

    public void SuccessAfterNavigation(string title, string message, string path)
        => pending = (Create(title, message, "success", 3000), path.Trim('/'), DateTimeOffset.UtcNow.AddSeconds(10));

    public void Dismiss(Guid id)
    {
        var removed = messages.RemoveAll(item => item.Id == id) + waiting.RemoveAll(item => item.Id == id);
        if (removed == 0) return;
        FillAvailableSlots();
        Changed?.Invoke();
    }

    public void NavigationChanged(string relativePath)
    {
        navigationGeneration++;
        // Route cleanup applies to transient feedback; unresolved persistent items need explicit dismissal.
        messages.RemoveAll(item => item.DurationMs > 0);
        waiting.RemoveAll(item => item.DurationMs > 0);
        FillAvailableSlots();
        if (pending is { } next && next.ExpiresAt > DateTimeOffset.UtcNow &&
            relativePath.Split('?', '#')[0].Trim('/') == next.Path) Add(next.Message);
        pending = null;
        Changed?.Invoke();
    }

    public void Clear()
    {
        navigationGeneration++;
        messages.Clear();
        waiting.Clear();
        pending = null;
        Changed?.Invoke();
    }

    private void Add(ToastMessage message)
    {
        if (messages.Count >= 3)
        {
            var transient = messages.FindIndex(item => item.DurationMs > 0);
            if (transient < 0)
            {
                // Timers start only when queued components become visible. Never evict persistent items.
                if (message.DurationMs > 0 && waiting.Count(item => item.DurationMs > 0) >= 20)
                    waiting.RemoveAt(waiting.FindIndex(item => item.DurationMs > 0));
                waiting.Add(message);
                return;
            }
            messages.RemoveAt(transient);
        }
        messages.Add(message);
    }

    private void FillAvailableSlots()
    {
        while (messages.Count < 3 && waiting.Count > 0)
        {
            messages.Add(waiting[0]);
            waiting.RemoveAt(0);
        }
    }

    private static ToastMessage Create(string title, string message, string severity, int? durationMs)
    {
        var kind = severity.ToLowerInvariant() is "success" or "error" or "warning" ? severity.ToLowerInvariant() : "info";
        var duration = durationMs ?? (kind switch { "success" => 3000, "error" => 8000, "warning" => 6000, _ => 4000 });
        return new(Guid.NewGuid(), title, message, kind, Math.Max(0, duration));
    }
}
