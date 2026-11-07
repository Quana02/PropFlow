using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.Communication.Services;

public sealed record AnnouncementView(Guid Id, string Title, string Content, DateTimeOffset? PublishedAt);
public sealed record AnnouncementPage(IReadOnlyList<AnnouncementView> Items, int Total, int Page, int PageSize, string TimeZoneId);
public sealed class AnnouncementApiClient(AuthenticatedApiClient api)
{
    public Task<ApiResult<AnnouncementPage>> ListAsync(int page, CancellationToken ct) =>
        api.SendAsync<AnnouncementPage>(HttpMethod.Get, $"api/v1/announcements?page={page}", ct: ct);
    public Task<ApiResult<AnnouncementView>> PublishAsync(string title, string content, CancellationToken ct) =>
        api.SendAsync<AnnouncementView>(HttpMethod.Post, "api/v1/announcements", new { title, content }, ct: ct);
}
