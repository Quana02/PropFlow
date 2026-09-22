namespace PropFlow.Modules.Communication.Domain.Announcements;

public class AnnouncementAudience
{
    private AnnouncementAudience()
    {
    }

    internal AnnouncementAudience(
        Guid announcementId,
        AnnouncementAudienceType audienceType,
        DateTimeOffset now,
        Guid? roleId = null,
        Guid? apartmentUnitId = null,
        Guid? residentId = null)
    {
        if (announcementId == Guid.Empty)
        {
            throw new ArgumentException("Announcement id is required.", nameof(announcementId));
        }

        ValidateTarget(audienceType, roleId, apartmentUnitId, residentId);

        Id = Guid.NewGuid();
        AnnouncementId = announcementId;
        AudienceType = audienceType;
        RoleId = roleId;
        ApartmentUnitId = apartmentUnitId;
        ResidentId = residentId;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid AnnouncementId { get; private set; }
    public AnnouncementAudienceType AudienceType { get; private set; }
    public Guid? RoleId { get; private set; }
    public Guid? ApartmentUnitId { get; private set; }
    public Guid? ResidentId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public Announcement? Announcement { get; private set; }

    private static void ValidateTarget(
        AnnouncementAudienceType audienceType,
        Guid? roleId,
        Guid? apartmentUnitId,
        Guid? residentId)
    {
        static bool Present(Guid? id) => id.HasValue && id.Value != Guid.Empty;

        var targetCount = new[] { roleId, apartmentUnitId, residentId }.Count(Present);

        switch (audienceType)
        {
            case AnnouncementAudienceType.ALL_USERS:
            case AnnouncementAudienceType.ALL_RESIDENTS:
                if (targetCount != 0)
                {
                    throw new ArgumentException("Global announcement audience must not have a target id.");
                }
                break;
            case AnnouncementAudienceType.ROLE:
                RequireOnly(roleId, targetCount, "Role audience requires role id only.");
                break;
            case AnnouncementAudienceType.APARTMENT:
                RequireOnly(apartmentUnitId, targetCount, "Apartment audience requires apartment unit id only.");
                break;
            case AnnouncementAudienceType.RESIDENT:
                RequireOnly(residentId, targetCount, "Resident audience requires resident id only.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(audienceType), audienceType, "Unsupported announcement audience type.");
        }

        static void RequireOnly(Guid? id, int targetCount, string message)
        {
            if (!Present(id) || targetCount != 1)
            {
                throw new ArgumentException(message);
            }
        }
    }
}
