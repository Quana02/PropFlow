namespace PropFlow.Web.Client.Shared.Feedback;

public sealed record ToastMessage(Guid Id, string Title, string Message, string Severity, int DurationMs);

// Presentation state only. Timers belong to the rendered toast components.
public sealed class ToastService
{
    private readonly List<ToastMessage> messages = [];
    private (ToastMessage Message, string Path, DateTimeOffset ExpiresAt)? pending;
    public event Action? Changed;
    public IReadOnlyList<ToastMessage> Messages => messages.ToArray();

    public void Show(string title, string message = "", string severity = "info", int? durationMs = null)
    {
        Add(Create(title, message, severity, durationMs));
        Changed?.Invoke();
    }

    public void SuccessAfterNavigation(string title, string message, string path)
        => pending = (Create(title, message, "success", 3000), path.Trim('/'), DateTimeOffset.UtcNow.AddSeconds(10));

    public void Dismiss(Guid id)
    {
        if (messages.RemoveAll(item => item.Id == id) > 0) Changed?.Invoke();
    }

    public void NavigationChanged(string relativePath)
    {
        messages.Clear();
        if (pending is { } next && next.ExpiresAt > DateTimeOffset.UtcNow &&
            relativePath.Split('?', '#')[0].Trim('/') == next.Path) Add(next.Message);
        pending = null;
        Changed?.Invoke();
    }

    public void Clear()
    {
        messages.Clear();
        pending = null;
        Changed?.Invoke();
    }

    private void Add(ToastMessage message)
    {
        // Bound the visible stack on smaller screens; removed components dispose their timers.
        if (messages.Count >= 3) messages.RemoveAt(0);
        messages.Add(message);
    }

    private static ToastMessage Create(string title, string message, string severity, int? durationMs)
    {
        var kind = severity.ToLowerInvariant() is "success" or "error" or "warning" ? severity.ToLowerInvariant() : "info";
        var duration = durationMs ?? (kind switch { "success" => 3000, "error" => 8000, "warning" => 6000, _ => 4000 });
        return new(Guid.NewGuid(), title, message, kind, Math.Max(0, duration));
    }
}
