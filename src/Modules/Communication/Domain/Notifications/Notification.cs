namespace PropFlow.Modules.Communication.Domain.Notifications;

public class Notification
{
    private Notification()
    {
        Title = string.Empty;
        Message = string.Empty;
    }

    public Notification(
        Guid recipientUserId,
        NotificationType type,
        string title,
        string message,
        DateTimeOffset now,
        Guid? sourceEventId = null,
        string? sourceType = null,
        Guid? sourceId = null,
        string? actionPath = null)
    {
        if (recipientUserId == Guid.Empty)
        {
            throw new ArgumentException("Recipient user id is required.", nameof(recipientUserId));
        }

        ValidateRelatedReference(sourceType, sourceId);

        Id = Guid.NewGuid();
        RecipientUserId = recipientUserId;
        Type = type;
        Title = NormalizeRequired(title, nameof(title));
        Message = NormalizeRequired(message, nameof(message));
        SourceEventId = sourceEventId;
        SourceType = NormalizeOptional(sourceType);
        SourceId = sourceId;
        ActionPath = NormalizeOptional(actionPath);
        IsRead = false;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid RecipientUserId { get; private set; }
    public NotificationType Type { get; private set; }
    public string Title { get; private set; }
    public string Message { get; private set; }
    public Guid? SourceEventId { get; private set; }
    public string? SourceType { get; private set; }
    public Guid? SourceId { get; private set; }
    public string? ActionPath { get; private set; }
    public bool IsRead { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void MarkAsRead(DateTimeOffset now)
    {
        if (now < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(now), "Read time cannot be before notification creation time.");
        }

        if (IsRead)
        {
            return;
        }

        IsRead = true;
        ReadAt = now;
    }

    public void MarkAsUnread()
    {
        if (!IsRead)
        {
            return;
        }

        IsRead = false;
        ReadAt = null;
    }

    private static void ValidateRelatedReference(string? sourceType, Guid? sourceId)
    {
        var hasSourceType = !string.IsNullOrWhiteSpace(sourceType);
        var hasSourceId = sourceId.HasValue;

        if (hasSourceType != hasSourceId)
        {
            throw new ArgumentException("Source type and source id must either both be supplied or both be omitted.");
        }

        if (sourceId == Guid.Empty)
        {
            throw new ArgumentException("Source id cannot be empty.", nameof(sourceId));
        }
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
