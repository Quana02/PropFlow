using PropFlow.Modules.Communication.Domain.Announcements;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;

namespace PropFlow.Modules.Communication.Application.Announcements;

public sealed record PublishAnnouncement(string Title, string Content);
public sealed record AnnouncementView(Guid Id, string Title, string Content, DateTimeOffset? PublishedAt);
public sealed record AnnouncementPage(IReadOnlyList<AnnouncementView> Items, int Total, int Page, int PageSize, string? TimeZoneId = null);
public sealed record AnnouncementResult<T>(int Status, T? Data = default, string? Message = null);
public interface IAnnouncementStore
{
    Task SaveAsync(Announcement announcement, CancellationToken ct);
    Task<AnnouncementPage> ListAsync(ActiveResidentResidence? residence, bool manager, int page, CancellationToken ct);
    Task<AnnouncementView?> GetAsync(Guid id, ActiveResidentResidence? residence, bool manager, CancellationToken ct);
}

public sealed class AnnouncementUseCases(IAnnouncementStore store, IResidentResidenceSource residences, ICurrentBuildingTimeZone zone, TimeProvider clock)
{
    private async Task<ActiveResidentResidence?> Residence(Guid actor, CancellationToken ct) => await residences.FindActiveAsync(actor,
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(await zone.GetAsync(ct))).DateTime), ct);

    public async Task<AnnouncementResult<AnnouncementView>> PublishAsync(Guid actor, PublishAnnouncement request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200 || string.IsNullOrWhiteSpace(request.Content))
            return new(400, Message: "Nhập tiêu đề (tối đa 200 ký tự) và nội dung thông báo.");
        var now = clock.GetUtcNow();
        var announcement = new Announcement(request.Title.Normalize(), request.Content.Normalize(), actor, now);
        announcement.AddAllResidentsAudience(now);
        announcement.Publish(actor, now, "{\"audienceType\":\"ALL_RESIDENTS\"}");
        await store.SaveAsync(announcement, ct);
        return new(201, new(announcement.Id, announcement.Title, announcement.Content, announcement.PublishedAt));
    }

    public async Task<AnnouncementResult<AnnouncementPage>> ListAsync(Guid actor, bool manager, int page, CancellationToken ct)
    {
        if (page < 1 || page > 100000) return new(400, Message: "Trang không hợp lệ.");
        var residence = manager ? null : await Residence(actor, ct);
        if (!manager && residence is null) return new(403, Message: "Tài khoản chưa có liên kết cư trú được xác minh.");
        return new(200, (await store.ListAsync(residence, manager, page, ct)) with { TimeZoneId = await zone.GetAsync(ct) });
    }
    public async Task<AnnouncementResult<AnnouncementView>> GetAsync(Guid actor, bool manager, Guid id, CancellationToken ct)
    {
        var residence = manager ? null : await Residence(actor, ct);
        if (!manager && residence is null) return new(403, Message: "Tài khoản chưa có liên kết cư trú được xác minh.");
        var item = await store.GetAsync(id, residence, manager, ct);
        return item is null ? new(404, Message: "Không tìm thấy thông báo.") : new(200, item);
    }
}
