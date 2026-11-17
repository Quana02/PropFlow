using System.Globalization;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.Modules.AiRecommendation.Application.FacilityDiscovery;

public sealed class FacilityWeekendNoticeOptions
{
    public bool Enabled { get; set; } = true;
    public TimeOnly StartsAt { get; set; } = new(8, 0);
    public TimeOnly EndsAt { get; set; } = new(20, 0);
    public int PollSeconds { get; set; } = 60;
}

public sealed record FacilityWeekendNotice(Guid EventId, DateOnly LocalDate, Guid FacilityId, string Title, string Message)
{
    public const string SourceType = "FACILITY_WEEKEND";
    public string ActionPath => $"/resident/facilities?facility={FacilityId:D}";
}

public interface IFacilityWeekendNoticeDelivery
{
    Task<int> DeliverAsync(FacilityWeekendNotice notice, CancellationToken ct);
}

public static class FacilityWeekendPlanner
{
    public static TimeOnly? Slot(DateTime localNow, FacilityWeekendNoticeOptions options)
    {
        if (localNow.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) return null;
        var current = TimeOnly.FromDateTime(localNow);
        for (var slot = options.StartsAt.ToTimeSpan(); slot <= options.EndsAt.ToTimeSpan(); slot += TimeSpan.FromHours(4))
            if (current.ToTimeSpan() >= slot && current.ToTimeSpan() < slot + TimeSpan.FromMinutes(15))
                return TimeOnly.FromTimeSpan(slot);
        return null;
    }
    public static DateOnly WeekendStart(DateOnly date) => date.DayOfWeek == DayOfWeek.Sunday ? date.AddDays(-1) : date;
    public static FacilityRecommendationView View(FacilityGuideWindow source, DateTime localNow, DateTimeOffset instant,
        IReadOnlyCollection<Guid>? excluded = null, int limit = 3)
    {
        var today = DateOnly.FromDateTime(localNow);
        // A general daily invitation never assumes the recipient has children or books an activity.
        var matches = FacilityNeedMatcher.Match(source, new(false, false, null, []), "", today, localNow)
            .Where(x => !string.IsNullOrWhiteSpace(x.Guide.ResponsibleName))
            .Where(x => excluded is null || !excluded.Contains(x.Guide.FacilityId))
            .ToArray();
        var open = matches.Where(x => FacilityNeedMatcher.OpenNow(x, localNow)).ToArray();
        var offset = open.Length == 0 ? 0 : today.DayNumber % open.Length;
        var choices = open.Skip(offset).Concat(open.Take(offset))
            .Concat(matches.Where(x => !FacilityNeedMatcher.OpenNow(x, localNow))).Take(Math.Clamp(limit, 1, 3));
        var suggestions = choices.Select(x => new FacilitySuggestion(FacilityNeedMatcher.Card(x.Guide), x.Times,
            "Lịch mở cửa do Ban quản lý công bố; không phải xác nhận đặt chỗ hoặc có sự kiện riêng.",
            FacilityNeedMatcher.Availability(x, localNow), FacilityNeedMatcher.OpenNow(x, localNow))).ToArray();
        var message = suggestions.Length == 0
            ? "Hôm nay chưa có tiện ích được công bố còn trong lịch mở cửa. Bạn có thể nói nhu cầu để tìm lịch tiếp theo."
            : $"Tiện ích ngày {today:dd/MM/yyyy}, theo giờ chung cư. Bạn có thể chọn một địa điểm hoặc nói nhu cầu để mình tìm điều phù hợp hơn.";
        return new(suggestions, "TODAY", message, null, instant);
    }

    public static FacilityWeekendNotice? Notice(Guid eventId, FacilityRecommendationView view, DateOnly date)
    {
        if (view.Suggestions.Length == 0) return null;
        var activities = view.Suggestions.Select(item => {
            var time = item.OpeningTimes[0];
            return $"{item.Facility.Name} ({time.OpensAt.ToString("HH:mm", CultureInfo.InvariantCulture)}–{time.ClosesAt.ToString("HH:mm", CultureInfo.InvariantCulture)})";
        });
        string[] titles = ["Cuối tuần mình cùng khám phá chung cư nhé!", "Một chút thời gian cho bản thân hôm nay nhé!", "Cuối tuần này bạn thử tiện ích này nhé!"];
        return new(eventId, date, view.Suggestions[0].Facility.Id, titles[date.DayNumber % titles.Length],
            $"Ngày {date:dd/MM}: {string.Join("; ", activities)}. Lịch do BQL công bố. Bấm để xem vị trí, người phụ trách và tìm hoạt động phù hợp với bạn.");
    }
}
