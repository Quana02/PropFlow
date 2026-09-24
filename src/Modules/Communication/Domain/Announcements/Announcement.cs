namespace PropFlow.Modules.Communication.Domain.Announcements;

public class Announcement
{
    private readonly List<AnnouncementAudience> _audiences = [];
    private readonly List<AnnouncementVersion> _versions = [];

    private Announcement()
    {
        Title = string.Empty;
        Content = string.Empty;
    }

    public Announcement(
        string title,
        string content,
        Guid createdBy,
        DateTimeOffset now)
    {
        if (createdBy == Guid.Empty)
        {
            throw new ArgumentException("Created by is required.", nameof(createdBy));
        }

        Id = Guid.NewGuid();
        Title = NormalizeRequired(title, nameof(title));
        Content = NormalizeRequired(content, nameof(content));
        Status = AnnouncementStatus.DRAFT;
        CreatedBy = createdBy;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public string Title { get; private set; }
    public string Content { get; private set; }
    public AnnouncementStatus Status { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset? WithdrawnAt { get; private set; }
    public string? WithdrawalReason { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyCollection<AnnouncementAudience> Audiences => _audiences.AsReadOnly();
    public IReadOnlyCollection<AnnouncementVersion> Versions => _versions.AsReadOnly();

    public void UpdateDraft(
        string title,
        string content,
        Guid updatedBy,
        DateTimeOffset now,
        string? changeReason = null)
    {
        EnsureDraft();
        ValidateActorAndTime(updatedBy, now, nameof(updatedBy));

        Title = NormalizeRequired(title, nameof(title));
        Content = NormalizeRequired(content, nameof(content));
        UpdatedBy = updatedBy;
        UpdatedAt = now;
        AddVersion(updatedBy, now, null, changeReason);
    }

    public AnnouncementAudience AddAllUsersAudience(DateTimeOffset now)
    {
        return AddAudience(AnnouncementAudienceType.ALL_USERS, now);
    }

    public AnnouncementAudience AddAllResidentsAudience(DateTimeOffset now)
    {
        return AddAudience(AnnouncementAudienceType.ALL_RESIDENTS, now);
    }

    public AnnouncementAudience AddRoleAudience(Guid roleId, DateTimeOffset now)
    {
        return AddAudience(AnnouncementAudienceType.ROLE, now, roleId: roleId);
    }

    

    public AnnouncementAudience AddApartmentAudience(Guid apartmentUnitId, DateTimeOffset now)
    {
        return AddAudience(AnnouncementAudienceType.APARTMENT, now, apartmentUnitId: apartmentUnitId);
    }

    public AnnouncementAudience AddResidentAudience(Guid residentId, DateTimeOffset now)
    {
        return AddAudience(AnnouncementAudienceType.RESIDENT, now, residentId: residentId);
    }

    public void Publish(Guid publishedBy, DateTimeOffset now, string? audienceSnapshotJson = null)
    {
        EnsureDraft();
        ValidateActorAndTime(publishedBy, now, nameof(publishedBy));

        if (_audiences.Count == 0)
        {
            throw new InvalidOperationException("Announcement audience must be defined before publishing.");
        }

        Status = AnnouncementStatus.PUBLISHED;
        PublishedAt = now;
        UpdatedBy = publishedBy;
        UpdatedAt = now;
        AddVersion(publishedBy, now, audienceSnapshotJson, "Published");
    }

    public void Withdraw(Guid withdrawnBy, string reason, DateTimeOffset now, string? audienceSnapshotJson = null)
    {
        if (Status != AnnouncementStatus.PUBLISHED)
        {
            throw new InvalidOperationException("Only published announcements can be withdrawn.");
        }

        ValidateActorAndTime(withdrawnBy, now, nameof(withdrawnBy));

        if (PublishedAt.HasValue && now < PublishedAt.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(now), "Withdraw time cannot be before publish time.");
        }

        WithdrawalReason = NormalizeRequired(reason, nameof(reason));
        Status = AnnouncementStatus.WITHDRAWN;
        WithdrawnAt = now;
        UpdatedBy = withdrawnBy;
        UpdatedAt = now;
        AddVersion(withdrawnBy, now, audienceSnapshotJson, WithdrawalReason);
    }

    private AnnouncementAudience AddAudience(
        AnnouncementAudienceType audienceType,
        DateTimeOffset now,
        Guid? roleId = null,
        
        Guid? apartmentUnitId = null,
        Guid? residentId = null)
    {
        EnsureDraft();

        if (now < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(now), "Audience creation time cannot be before announcement creation time.");
        }

        EnsureAudienceIsUnique(audienceType, roleId, apartmentUnitId, residentId);

        var audience = new AnnouncementAudience(Id, audienceType, now, roleId, apartmentUnitId, residentId);
        _audiences.Add(audience);
        return audience;
    }

    private void EnsureAudienceIsUnique(
        AnnouncementAudienceType audienceType,
        Guid? roleId,
        
        Guid? apartmentUnitId,
        Guid? residentId)
    {
        var duplicateExists = audienceType switch
        {
            AnnouncementAudienceType.ALL_USERS or AnnouncementAudienceType.ALL_RESIDENTS =>
                _audiences.Any(audience => audience.AudienceType == audienceType),
            AnnouncementAudienceType.ROLE =>
                _audiences.Any(audience => audience.AudienceType == audienceType && audience.RoleId == roleId),
            
            AnnouncementAudienceType.APARTMENT =>
                _audiences.Any(audience => audience.AudienceType == audienceType && audience.ApartmentUnitId == apartmentUnitId),
            AnnouncementAudienceType.RESIDENT =>
                _audiences.Any(audience => audience.AudienceType == audienceType && audience.ResidentId == residentId),
            _ => false
        };

        if (duplicateExists)
        {
            throw new InvalidOperationException("Announcement already contains the same audience scope.");
        }
    }

    private void AddVersion(Guid changedBy, DateTimeOffset now, string? audienceSnapshotJson, string? changeReason)
    {
        _versions.Add(new AnnouncementVersion(
            Id,
            _versions.Count + 1,
            Title,
            Content,
            Status,
            changedBy,
            now,
            audienceSnapshotJson,
            changeReason));
    }

    private void EnsureDraft()
    {
        if (Status != AnnouncementStatus.DRAFT)
        {
            throw new InvalidOperationException("Only draft announcements can be changed by this operation.");
        }
    }

    private void ValidateActorAndTime(Guid actorId, DateTimeOffset now, string parameterName)
    {
        if (actorId == Guid.Empty)
        {
            throw new ArgumentException("Actor id is required.", parameterName);
        }

        if (now < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(now), "Change time cannot be before announcement creation time.");
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
}
