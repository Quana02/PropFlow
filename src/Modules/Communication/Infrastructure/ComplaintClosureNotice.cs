using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Communication.Application;
using PropFlow.Modules.Communication.Domain.Notifications;
using PropFlow.Modules.Communication.Infrastructure.Persistence;

namespace PropFlow.Modules.Communication.Infrastructure;

public sealed class ComplaintClosureNotice(CommunicationDbContext db) : IComplaintClosureNotice
{
    public async Task<FinanceNotificationUpdate?> AddAsync(Guid eventId, Guid complaintId, Guid recipientUserId, DateTimeOffset occurredAt, CancellationToken ct)
    {
        if (await db.Notifications.AnyAsync(n => n.SourceEventId == eventId && n.RecipientUserId == recipientUserId, ct)) return null;
        var notification = new Notification(recipientUserId, NotificationType.COMPLAINT,
            "Phản ánh đã được giải quyết", "Ban Quản lý đã phản hồi và đóng phản ánh của bạn. Mở phản ánh để xem kết quả.",
            occurredAt, eventId, "COMPLAINT", complaintId, $"/resident/complaints/{complaintId}");
        db.Notifications.Add(notification);
        return new(notification.Id, notification.Title, notification.Message, notification.ActionPath,
            notification.IsRead, notification.CreatedAt, notification.SourceType);
    }
}
