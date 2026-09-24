using PropFlow.Modules.Communication.Domain.Announcements;
using PropFlow.Modules.Communication.Domain.Notifications;

namespace PropFlow.UnitTests;

public class CommunicationTests
{
    private readonly DateTimeOffset _now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Notification_Constructor_CreatesUnreadUserSpecificRecord()
    {
        var recipientId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var notification = new Notification(
            recipientId,
            NotificationType.SERVICE_REQUEST,
            " Request updated ",
            " Your request was resolved ",
            _now,
            sourceEventId: Guid.NewGuid(),
            sourceType: "SERVICE_REQUEST",
            sourceId: sourceId,
            actionPath: " /requests/1 ");

        Assert.NotEqual(Guid.Empty, notification.Id);
        Assert.Equal(recipientId, notification.RecipientUserId);
        Assert.Equal(NotificationType.SERVICE_REQUEST, notification.Type);
        Assert.Equal("Request updated", notification.Title);
        Assert.Equal("Your request was resolved", notification.Message);
        Assert.Equal("SERVICE_REQUEST", notification.SourceType);
        Assert.Equal(sourceId, notification.SourceId);
        Assert.Equal("/requests/1", notification.ActionPath);
        Assert.False(notification.IsRead);
        Assert.Null(notification.ReadAt);
        Assert.Equal(_now, notification.CreatedAt);
    }

    [Fact]
    public void Notification_ValidatesRequiredFieldsAndRelatedReferencePair()
    {
        Assert.Throws<ArgumentException>(() => new Notification(Guid.Empty, NotificationType.SYSTEM, "Title", "Message", _now));
        Assert.Throws<ArgumentException>(() => new Notification(Guid.NewGuid(), NotificationType.SYSTEM, "", "Message", _now));
        Assert.Throws<ArgumentException>(() => new Notification(Guid.NewGuid(), NotificationType.SYSTEM, "Title", "", _now));
        Assert.Throws<ArgumentException>(() => new Notification(Guid.NewGuid(), NotificationType.SYSTEM, "Title", "Message", _now, sourceType: "INVOICE"));
        Assert.Throws<ArgumentException>(() => new Notification(Guid.NewGuid(), NotificationType.SYSTEM, "Title", "Message", _now, sourceId: Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new Notification(Guid.NewGuid(), NotificationType.SYSTEM, "Title", "Message", _now, sourceType: "INVOICE", sourceId: Guid.Empty));
    }

    [Fact]
    public void Notification_MarkReadAndUnread_IsDeterministic()
    {
        var notification = new Notification(Guid.NewGuid(), NotificationType.ACCOUNT, "Title", "Message", _now);
        var readAt = _now.AddMinutes(5);

        notification.MarkAsRead(readAt);

        Assert.True(notification.IsRead);
        Assert.Equal(readAt, notification.ReadAt);

        notification.MarkAsRead(readAt.AddMinutes(1));
        Assert.Equal(readAt, notification.ReadAt);

        notification.MarkAsUnread();
        Assert.False(notification.IsRead);
        Assert.Null(notification.ReadAt);

        notification.MarkAsUnread();
        Assert.False(notification.IsRead);
        Assert.Null(notification.ReadAt);

        Assert.Throws<ArgumentOutOfRangeException>(() => notification.MarkAsRead(_now.AddTicks(-1)));
    }

    [Fact]
    public void Notification_PublicApi_DoesNotExposeDeleteOrGenericStatusMutation()
    {
        var publicMethods = typeof(Notification)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .ToList();

        Assert.Contains(nameof(Notification.MarkAsRead), publicMethods);
        Assert.Contains(nameof(Notification.MarkAsUnread), publicMethods);
        Assert.DoesNotContain("Delete", publicMethods);
        Assert.DoesNotContain("SetStatus", publicMethods);
        Assert.DoesNotContain("ChangeStatus", publicMethods);
    }

    [Fact]
    public void Announcement_Constructor_CreatesDraft()
    {
        var createdBy = Guid.NewGuid();
        var announcement = new Announcement(" Water outage ", " Maintenance from 9AM ", createdBy, _now);

        Assert.NotEqual(Guid.Empty, announcement.Id);
        Assert.Equal("Water outage", announcement.Title);
        Assert.Equal("Maintenance from 9AM", announcement.Content);
        Assert.Equal(AnnouncementStatus.DRAFT, announcement.Status);
        Assert.Equal(createdBy, announcement.CreatedBy);
        Assert.Equal(_now, announcement.CreatedAt);
        Assert.Equal(_now, announcement.UpdatedAt);
        Assert.Null(announcement.PublishedAt);
        Assert.Null(announcement.WithdrawnAt);
        Assert.Empty(announcement.Audiences);
        Assert.Empty(announcement.Versions);
    }

    [Fact]
    public void Announcement_ValidatesRequiredFieldsAndDraftUpdate()
    {
        Assert.Throws<ArgumentException>(() => new Announcement("", "Content", Guid.NewGuid(), _now));
        Assert.Throws<ArgumentException>(() => new Announcement("Title", "", Guid.NewGuid(), _now));
        Assert.Throws<ArgumentException>(() => new Announcement("Title", "Content", Guid.Empty, _now));

        var announcement = NewDraftAnnouncement();
        announcement.UpdateDraft("Updated title", "Updated content", Guid.NewGuid(), _now.AddMinutes(1), "Draft correction");

        Assert.Equal("Updated title", announcement.Title);
        Assert.Equal("Updated content", announcement.Content);
        Assert.Single(announcement.Versions);
        Assert.Equal(1, announcement.Versions.Single().VersionNo);
        Assert.Equal(AnnouncementStatus.DRAFT, announcement.Versions.Single().StatusSnapshot);
    }

    [Fact]
    public void Announcement_AudienceMethods_EnforceTargetConsistency()
    {
        var announcement = NewDraftAnnouncement();
        var roleId = Guid.NewGuid();
        
        var apartmentId = Guid.NewGuid();
        var residentId = Guid.NewGuid();

        Assert.Equal(AnnouncementAudienceType.ALL_USERS, announcement.AddAllUsersAudience(_now).AudienceType);
        Assert.Equal(roleId, announcement.AddRoleAudience(roleId, _now).RoleId);
        
        Assert.Equal(apartmentId, announcement.AddApartmentAudience(apartmentId, _now).ApartmentUnitId);
        Assert.Equal(residentId, announcement.AddResidentAudience(residentId, _now).ResidentId);
        Assert.Equal(4, announcement.Audiences.Count);

        Assert.Throws<ArgumentException>(() => announcement.AddRoleAudience(Guid.Empty, _now));
        Assert.Throws<ArgumentOutOfRangeException>(() => announcement.AddAllResidentsAudience(_now.AddTicks(-1)));
    }

    [Fact]
    public void Announcement_AudienceMethods_RejectExactDuplicateScopes()
    {
        AssertDuplicateIsRejected(announcement => announcement.AddAllUsersAudience(_now));
        AssertDuplicateIsRejected(announcement => announcement.AddAllResidentsAudience(_now));

        var roleId = Guid.NewGuid();
        AssertDuplicateIsRejected(announcement => announcement.AddRoleAudience(roleId, _now));

        

        var apartmentId = Guid.NewGuid();
        AssertDuplicateIsRejected(announcement => announcement.AddApartmentAudience(apartmentId, _now));

        var residentId = Guid.NewGuid();
        AssertDuplicateIsRejected(announcement => announcement.AddResidentAudience(residentId, _now));
    }

    [Fact]
    public void Announcement_AudienceMethods_AllowDifferentTargetsAndDifferentTypes()
    {
        var announcement = NewDraftAnnouncement();

        announcement.AddRoleAudience(Guid.NewGuid(), _now);
        announcement.AddRoleAudience(Guid.NewGuid(), _now);
       
        announcement.AddApartmentAudience(Guid.NewGuid(), _now);
        announcement.AddApartmentAudience(Guid.NewGuid(), _now);
        announcement.AddResidentAudience(Guid.NewGuid(), _now);
        announcement.AddResidentAudience(Guid.NewGuid(), _now);

        Assert.Equal(6, announcement.Audiences.Count);

        var sameGuidAcrossTypes = Guid.NewGuid();
        var anotherAnnouncement = NewDraftAnnouncement();
        anotherAnnouncement.AddRoleAudience(sameGuidAcrossTypes, _now);
        
        anotherAnnouncement.AddApartmentAudience(sameGuidAcrossTypes, _now);
        anotherAnnouncement.AddResidentAudience(sameGuidAcrossTypes, _now);

        Assert.Equal(3, anotherAnnouncement.Audiences.Count);
    }

    [Fact]
    public void Announcement_AudiencesCollection_CannotBypassDuplicateInvariant()
    {
        var announcement = NewDraftAnnouncement();
        announcement.AddAllUsersAudience(_now);

        Assert.IsAssignableFrom<IReadOnlyCollection<AnnouncementAudience>>(announcement.Audiences);
        Assert.DoesNotContain(announcement.Audiences.GetType().GetMethods(), method => method.Name == "Add");
        Assert.Throws<InvalidOperationException>(() => announcement.AddAllUsersAudience(_now));
    }

    [Fact]
    public void Announcement_Publish_RequiresAudienceAndCreatesHistoryVersion()
    {
        var noAudience = NewDraftAnnouncement();
        Assert.Throws<InvalidOperationException>(() => noAudience.Publish(Guid.NewGuid(), _now.AddMinutes(1)));

        var announcement = NewDraftAnnouncement();
        announcement.AddAllResidentsAudience(_now);
        var publishedBy = Guid.NewGuid();
        var publishedAt = _now.AddMinutes(5);

        announcement.Publish(publishedBy, publishedAt, "[{\"audienceType\":\"ALL_RESIDENTS\"}]");

        Assert.Equal(AnnouncementStatus.PUBLISHED, announcement.Status);
        Assert.Equal(publishedAt, announcement.PublishedAt);
        Assert.Equal(publishedBy, announcement.UpdatedBy);
        Assert.Single(announcement.Versions);
        var version = announcement.Versions.Single();
        Assert.Equal(1, version.VersionNo);
        Assert.Equal(AnnouncementStatus.PUBLISHED, version.StatusSnapshot);
        Assert.Equal("[{\"audienceType\":\"ALL_RESIDENTS\"}]", version.AudienceSnapshotJson);

        Assert.Throws<InvalidOperationException>(() => announcement.Publish(Guid.NewGuid(), publishedAt.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => announcement.UpdateDraft("New", "Content", Guid.NewGuid(), publishedAt.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => announcement.AddAllUsersAudience(publishedAt.AddMinutes(1)));
    }

    [Fact]
    public void Announcement_Withdraw_OnlyFromPublishedAndIsTerminal()
    {
        var draft = NewDraftAnnouncement();
        Assert.Throws<InvalidOperationException>(() => draft.Withdraw(Guid.NewGuid(), "Cancelled", _now));

        var announcement = NewDraftAnnouncement();
        announcement.AddAllUsersAudience(_now);
        
        announcement.Publish(Guid.NewGuid(), _now.AddMinutes(5), "[{\"audienceType\":\"ALL_USERS\"}]");

        var withdrawnBy = Guid.NewGuid();
        var withdrawnAt = _now.AddMinutes(10);
        announcement.Withdraw(withdrawnBy, " Schedule changed ", withdrawnAt);

        Assert.Equal(AnnouncementStatus.WITHDRAWN, announcement.Status);
        Assert.Equal(withdrawnAt, announcement.WithdrawnAt);
        Assert.Equal("Schedule changed", announcement.WithdrawalReason);
        Assert.Equal(withdrawnBy, announcement.UpdatedBy);
        Assert.Equal(2, announcement.Versions.Count);
        Assert.Equal(AnnouncementStatus.WITHDRAWN, announcement.Versions.Last().StatusSnapshot);

        Assert.Throws<InvalidOperationException>(() => announcement.Withdraw(Guid.NewGuid(), "Again", withdrawnAt.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => announcement.UpdateDraft("New", "Content", Guid.NewGuid(), withdrawnAt.AddMinutes(1)));
        Assert.Throws<ArgumentException>(() =>
        {
            var another = NewDraftAnnouncement();
            another.AddAllUsersAudience(_now);
            another.Publish(Guid.NewGuid(), _now.AddMinutes(1));
            another.Withdraw(Guid.NewGuid(), "", _now.AddMinutes(2));
        });
    }

    [Fact]
    public void Announcement_PublicApi_DoesNotExposeHardDeleteOrGenericStatusMutation()
    {
        var publicMethods = typeof(Announcement)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .ToList();

        Assert.Contains(nameof(Announcement.Publish), publicMethods);
        Assert.Contains(nameof(Announcement.Withdraw), publicMethods);
        Assert.DoesNotContain("Delete", publicMethods);
        Assert.DoesNotContain("ClearHistory", publicMethods);
        Assert.DoesNotContain("SetStatus", publicMethods);
        Assert.DoesNotContain("ChangeStatus", publicMethods);
    }

    private Announcement NewDraftAnnouncement()
    {
        return new Announcement("Elevator maintenance", "Elevator B will be offline tomorrow.", Guid.NewGuid(), _now);
    }

    private void AssertDuplicateIsRejected(Func<Announcement, AnnouncementAudience> addAudience)
    {
        var announcement = NewDraftAnnouncement();

        addAudience(announcement);
        var exception = Assert.Throws<InvalidOperationException>(() => addAudience(announcement));

        Assert.Contains("same audience scope", exception.Message);
    }
}
