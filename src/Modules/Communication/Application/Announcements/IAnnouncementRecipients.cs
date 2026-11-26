namespace PropFlow.Modules.Communication.Application.Announcements;

public interface IAnnouncementRecipients
{
    Task<IReadOnlyList<Guid>> GetUserIdsAsync(CancellationToken ct);
}
