using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PropFlow.Modules.Communication.Application;
using PropFlow.Modules.Communication.Domain.Notifications;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
namespace PropFlow.Modules.Communication.Infrastructure;
public sealed class FinanceNoticePublisher(CommunicationDbContext db, IFinanceNotificationRealtime? realtime = null,
    ILogger<FinanceNoticePublisher>? logger = null) : IFinanceNotificationStore
{
    public async Task PublishAsync(FinanceNotification n, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({n.EventId.ToString() + n.UserId.ToString()}, 1))", ct);
        var existingNotification = n.UserId is not null && await db.Notifications.AnyAsync(
            x => x.RecipientUserId == n.UserId && x.SourceEventId == n.EventId, ct);
        var emailEvent = n.PreviousRecipientEventId ?? n.EventId;
        if (!string.IsNullOrWhiteSpace(n.Email))
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({emailEvent.ToString() + n.Email.ToUpperInvariant()}, 2))", ct);
        Notification? notification = null;
        if (n.UserId is Guid userId && !existingNotification) {
            notification = new Notification(userId, n.SourceType == "PAYMENT" ? NotificationType.PAYMENT : NotificationType.INVOICE,
                n.Title, n.Message, DateTimeOffset.UtcNow, n.EventId, n.SourceType, n.SourceId, n.ActionPath);
            db.Notifications.Add(notification);
        }
        // A newly linked account needs the web notice, but reuse email already queued/sent to the addressed person.
        var existingRecipientEmail = !string.IsNullOrWhiteSpace(n.Email)
            && await db.FinanceEmails.AnyAsync(x => x.EventId == emailEvent && x.Address.ToUpper() == n.Email.ToUpper(), ct);
        if (!string.IsNullOrWhiteSpace(n.Email) && !existingRecipientEmail) db.FinanceEmails.Add(new FinanceEmail {
            Id = Guid.NewGuid(), EventId = emailEvent, UserId = n.UserId, Address = n.Email,
            Subject = n.Title, Html = n.Html, AttachmentFileName = n.AttachmentFileName, NextAttemptAt = DateTimeOffset.UtcNow });
        // Notification and durable email enqueue commit together. Unique event keys prevent duplicates.
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (notification is not null && realtime is not null)
        {
            try { await realtime.CreatedAsync(notification.RecipientUserId, new(notification.Id, notification.Title,
                notification.Message, notification.ActionPath, notification.IsRead, notification.CreatedAt), ct); }
            catch (Exception ex) { logger?.LogWarning("Realtime notification deferred for {NoticeId} ({Type}).", notification.Id, ex.GetType().Name); }
        }
    }
}
