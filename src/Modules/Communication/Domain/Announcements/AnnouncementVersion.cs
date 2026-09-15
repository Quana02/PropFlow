namespace PropFlow.Modules.Communication.Domain.Announcements;

public class AnnouncementVersion
{
    private AnnouncementVersion()
    {
        TitleSnapshot = string.Empty;
        ContentSnapshot = string.Empty;
    }

    internal AnnouncementVersion(
        Guid announcementId,
        int versionNo,
        string titleSnapshot,
        string contentSnapshot,
        AnnouncementStatus statusSnapshot,
        Guid changedBy,
        DateTimeOffset now,
        string? audienceSnapshotJson = null,
        string? changeReason = null)
    {
        if (announcementId == Guid.Empty)
        {
            throw new ArgumentException("Announcement id is required.", nameof(announcementId));
        }

        if (versionNo < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(versionNo), "Version number must be at least 1.");
        }

        if (changedBy == Guid.Empty)
        {
            throw new ArgumentException("Changed by is required.", nameof(changedBy));
        }

        Id = Guid.NewGuid();
        AnnouncementId = announcementId;
        VersionNo = versionNo;
        TitleSnapshot = NormalizeRequired(titleSnapshot, nameof(titleSnapshot));
        ContentSnapshot = NormalizeRequired(contentSnapshot, nameof(contentSnapshot));
        StatusSnapshot = statusSnapshot;
        AudienceSnapshotJson = NormalizeOptional(audienceSnapshotJson);
        ChangedBy = changedBy;
        ChangeReason = NormalizeOptional(changeReason);
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid AnnouncementId { get; private set; }
    public int VersionNo { get; private set; }
    public string TitleSnapshot { get; private set; }
    public string ContentSnapshot { get; private set; }
    public AnnouncementStatus StatusSnapshot { get; private set; }
    public string? AudienceSnapshotJson { get; private set; }
    public Guid ChangedBy { get; private set; }
    public string? ChangeReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public Announcement? Announcement { get; private set; }

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
