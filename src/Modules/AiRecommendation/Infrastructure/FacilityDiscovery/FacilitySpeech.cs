using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PropFlow.Modules.AiRecommendation.Application.FacilityDiscovery;

namespace PropFlow.Modules.AiRecommendation.Infrastructure.FacilityDiscovery;

// Only server-created recommendation transcripts can be synthesized. No arbitrary resident text or staff contact is sent.
public sealed class FacilitySpeech(IFacilitySpeechProvider provider, FacilitySpeechOptions options, TimeProvider clock, ILogger<FacilitySpeech> logger) : IDisposable
{
    private sealed record Ticket(Guid Actor, string Text);
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 32 * 1024 * 1024 });
    private readonly MemoryCache budgetCache = new(new MemoryCacheOptions());
    private readonly SemaphoreSlim generation = new(1, 1);
    private readonly object budgets = new();
    public Guid Prepare(Guid actor, FacilityRecommendationView view)
    {
        var id = Guid.NewGuid();
        var text = Transcript(view);
        cache.Set("ticket:" + id, new Ticket(actor, text), new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10), Size = text.Length * 2 + 128 });
        return id;
    }
    public static string Transcript(FacilityRecommendationView view)
    {
        if(view.Mode=="CLARIFY")return view.FollowUp??"Bạn muốn tìm hoạt động gì và vào lúc nào?";
        if (view.Suggestions.Length == 0) return "Mình chưa tìm thấy tiện ích phù hợp nhu cầu và lịch hoạt động. Bạn có thể nói rõ hơn hoặc liên hệ Ban quản lý để được hỗ trợ.";
        var item = view.Suggestions[0]; var facility = item.Facility; var time = item.OpeningTimes.FirstOrDefault();
        var intro = view.Mode == "GEMINI" ? $"Mình tìm được {view.Suggestions.Length} lựa chọn cho bạn." : "Mình đang tra cứu thông tin do Ban quản lý cung cấp.";
        var name = FacilityNeedMatcher.Redact(facility.Name).Replace("[DEMO]", "bản mẫu").Replace("CLB", "câu lạc bộ");
        var schedule = time is null ? "" : $" Ngày {time.Date.Day} tháng {time.Date.Month}, mở cửa từ {Hour(time.OpensAt)} đến {Hour(time.ClosesAt)}.";
        var condition = !view.ForChildren && facility.Audience!="CHILDREN" ? "" : facility.ChildPolicy == "SUPERVISED_CONTACT_REQUIRED" ? " Cần liên hệ Ban quản lý xác nhận tiếp nhận trẻ, đây chưa phải đăng ký thành công."
            : view.ForChildren || facility.Audience=="CHILDREN" ? " Trẻ em cần có phụ huynh hoặc người giám hộ đi cùng." : "";
        // The provider receives public amenity name/schedule, never the resident's query, staff names, phone numbers or contact fields.
        var text = $"{intro} Bạn có thể xem {name}. {item.Availability}.{schedule}{condition} {view.FollowUp}".Trim();
        return text[..Math.Min(text.Length, 800)];
    }
    private static string Hour(TimeOnly value) => value.Minute == 0 ? $"{value.Hour} giờ" : $"{value.Hour} giờ {value.Minute} phút";
    public async Task<FacilitySpeechAudio> GetAsync(Guid actor, FacilitySpeechRequest input, CancellationToken ct)
    {
        string text;
        if (input.SpeechId is { } id)
        {
            if (!cache.TryGetValue<Ticket>("ticket:" + id, out var ticket) || ticket!.Actor != actor)
                throw Failure(404, "speech_expired", "Phản hồi đã hết thời gian nghe lại. Hãy tìm tiện ích lại nhé.");
            text = ticket.Text;
        }
        else text = input.Kind switch {
            "WELCOME" => "Chào bạn, mình là trợ lý tiện ích PropFlow. Hôm nay bạn muốn làm gì? Mình sẽ cùng bạn tìm hoạt động và thời gian phù hợp nhé.",
            "NEW_NEED" => "Mình nghe đây. Lần này bạn muốn tìm hoạt động gì?",
            _ => throw Failure(400, "speech_invalid", "Không xác định được phản hồi cần đọc.") };
        var key = "audio:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        if (cache.TryGetValue<FacilitySpeechAudio>(key, out var hit)) return hit!;
        if (!await generation.WaitAsync(TimeSpan.FromSeconds(20), ct)) throw Failure(429, "speech_busy", "Giọng đọc đang bận. Bạn nhấn nghe lại sau một chút nhé.");
        try
        {
            if (cache.TryGetValue<FacilitySpeechAudio>(key, out hit)) return hit!;
            lock (budgets)
            {
                var now = clock.GetUtcNow();
                var userKey = $"user:{actor}:{now:yyyyMMddHH}"; var globalKey = $"global:{now:yyyyMMdd}";
                var userCount = budgetCache.Get<int>(userKey); var globalCount = budgetCache.Get<int>(globalKey);
                if (userCount >= options.PerResidentHour || globalCount >= options.PerDay)
                    throw Failure(429, "speech_quota", "Đã đạt hạn mức giọng đọc. Phần gợi ý bằng chữ vẫn dùng được.");
                budgetCache.Set(userKey, userCount + 1, TimeSpan.FromHours(1));
                budgetCache.Set(globalKey, globalCount + 1, TimeSpan.FromDays(1));
            }
            var audio = await provider.GenerateAsync(text, ct);
            if (audio is null) throw Failure(503, "speech_unavailable", "Chưa tải được giọng tiếng Việt. Bạn có thể nhấn nghe lại; trạng thái bật tiếng vẫn được giữ.");
            var result = new FacilitySpeechAudio(Convert.ToBase64String(audio));
            cache.Set(key, result, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30), Size = result.Audio.Length * 2 + 128 });
            logger.LogInformation("Vietnamese amenity speech generated: {Bytes} bytes", audio.Length);
            return result;
        }
        finally { generation.Release(); }
    }
    private static FacilityRecommendationFailure Failure(int status, string code, string text) => new(status, code, text);
    public void Dispose() { cache.Dispose(); budgetCache.Dispose(); generation.Dispose(); }
}
