using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PropFlow.Modules.AiRecommendation.Application.FacilityDiscovery;
using PropFlow.Modules.AiRecommendation.Domain.Recommendations;
using PropFlow.Modules.AiRecommendation.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.Modules.AiRecommendation.Infrastructure.FacilityDiscovery;

public sealed class FacilityRecommendations(AiRecommendationDbContext db,IFacilityGuideSource source,IFacilityResidentAccess access,
    ICurrentBuildingTimeZone timezone,TimeProvider clock,IFacilityNeedAdvisor advisor,FacilityRecommendationOptions options,ILogger<FacilityRecommendations> logger)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private async Task<DateTime> LocalNow(CancellationToken ct)=>TimeZoneInfo.ConvertTime(clock.GetUtcNow(),TimeZoneInfo.FindSystemTimeZoneById(await timezone.GetAsync(ct))).DateTime;
    private async Task Check(Guid actor,CancellationToken ct)
    {if(!await access.ActiveAsync(actor,ct))throw new FacilityRecommendationFailure(403,"facility_resident_inactive","Cần có hồ sơ cư trú hiện hành để xem gợi ý tiện ích.");}
    public async Task<ResidentFacilityCatalogue> CatalogueAsync(Guid actor,CancellationToken ct)
    {await Check(actor,ct);return new((await source.ReadAsync(ct)).Guides.Select(FacilityNeedMatcher.Card).ToArray(),DateOnly.FromDateTime(await LocalNow(ct)));}
    public async Task<FacilityRecommendationView> TodayAsync(Guid actor,Guid? facilityId,CancellationToken ct)
    {
        await Check(actor,ct);
        var facts=await source.ReadAsync(ct);
        if(facilityId is not null)facts=facts with {Guides=facts.Guides.Where(x=>x.FacilityId==facilityId).ToArray()};
        var view=FacilityWeekendPlanner.View(facts,await LocalNow(ct),clock.GetUtcNow());
        return facilityId is not null && view.Suggestions.Length==0
            ? view with {Message="Hiện không còn lịch công bố phù hợp cho tiện ích được nhắc. Bạn có thể nói nhu cầu để tìm lựa chọn khác."}
            : view;
    }
    public async Task<FacilityRecommendationView> RecommendAsync(Guid actor,FacilityRecommendationRequest request,CancellationToken ct)
    {
        await Check(actor,ct);var now=await LocalNow(ct);var today=DateOnly.FromDateTime(now);
        if(string.IsNullOrWhiteSpace(request.Need)||request.Need.Trim().Length is <2 or >1200 || request.PreferredDate<today || request.PreferredDate>today.AddDays(30))
            throw new FacilityRecommendationFailure(400,"facility_need_invalid","Mô tả nhu cầu từ hai đến một nghìn hai trăm ký tự; ngày tùy chọn nằm trong ba mươi ngày tới.");
        var need=FacilityNeedMatcher.Redact(request.Need.Trim());var intent=FacilityNeedMatcher.Interpret(need);
        FacilityRecommendationView Clarify(string question)=>new([],"CLARIFY","Mình cần hiểu thêm để chọn đúng tiện ích cho bạn.",question,clock.GetUtcNow());
        if(FacilityNeedMatcher.Clarification(need) is { } question)return Clarify(question);
        DateOnly? date=request.PreferredDate;
        date??=FacilityNeedMatcher.Date(need,today);
        if(date<today||date>today.AddDays(30))throw new FacilityRecommendationFailure(400,"facility_date_invalid","Bạn chọn ngày trong ba mươi ngày tới nhé.");
        var window=await source.ReadAsync(ct);
        if(intent.ForChildren && intent.Age is null)
            return Clarify("Người sử dụng là trẻ bao nhiêu tuổi? Có người lớn đi cùng hay bạn cần hoạt động có người phụ trách? Bạn muốn đi vào lúc nào?");
        FacilityMatch[] Search(FacilityGuideWindow facts,FacilityNeedIntent target,DateTime instant,out bool alternative)
        {
            var found=FacilityNeedMatcher.Match(facts,target,need,date,instant);alternative=false;
            if(found.Length==0 && (date is not null || target.At is not null))
            {
                alternative=true;
                found=FacilityNeedMatcher.Match(facts,target with {At=null,Weekdays=[]},need,null,instant);
            }
            return found.Take(options.MaxCandidates).ToArray();
        }
        var matches=Search(window,intent,now,out var alternative);
        FacilityRecommendationView Response(IEnumerable<FacilityMatch> chosen,string mode,FacilityNeedIntent effective)
        {
            var items=chosen.OrderByDescending(x=>FacilityNeedMatcher.OpenNow(x,now)).ThenBy(x=>x.Times[0].Date.ToDateTime(x.Times[0].OpensAt)>now?x.Times[0].Date.ToDateTime(x.Times[0].OpensAt):now)
                .Take(3).Select(x=>new FacilitySuggestion(FacilityNeedMatcher.Card(x.Guide),x.Times,
                effective.ForChildren?"Hồ sơ BQL xác định tiện ích phù hợp cho trẻ em hoặc gia đình; kiểm tra độ tuổi và điều kiện đi cùng.":"Tiện ích được đối chiếu với nhu cầu và hồ sơ do BQL cung cấp.",
                (alternative?"Lịch thay thế · ":"")+FacilityNeedMatcher.Availability(x,now),FacilityNeedMatcher.OpenNow(x,now))).ToArray();
            var message=items.Length==0 ? effective.NeedsSupervision ? "Chưa tìm thấy chương trình có thông tin giám sát phù hợp. Khu vui chơi thông thường không thay thế dịch vụ trông trẻ; hãy liên hệ BQL."
                : "Chưa tìm thấy tiện ích phù hợp nhu cầu và lịch hoạt động. Bạn có thể nói rõ hơn hoặc liên hệ BQL."
                : "Thông tin giờ hoạt động, người phụ trách và điều kiện sử dụng lấy từ hồ sơ BQL. Gợi ý không phải xác nhận đăng ký hoặc giữ chỗ.";
            if(mode is "FALLBACK" or "CATALOGUE")message="AI chưa sẵn sàng; kết quả hiện tại là tra cứu hồ sơ theo nội dung bạn nhập. "+message;
            if(items.Length>0)message=(alternative?"Không tìm thấy lựa chọn đáp ứng lịch bạn yêu cầu; dưới đây là lịch thay thế. ":"Mình đối chiếu lịch theo ngày giờ hiện tại của chung cư. ")+message;
            var followup=effective.NeedsSupervision?"Liên hệ người phụ trách để xác nhận độ tuổi, lịch chương trình và việc tiếp nhận trẻ khi không có người nhà.":null;
            return new(items,mode,message,followup,clock.GetUtcNow(),ForChildren:effective.ForChildren);
        }
        if(matches.Length==0)return Response([],"NO_MATCH",intent);
        if(!advisor.Ready)return Response(matches.Where(x=>x.Score>0),"CATALOGUE",intent);
        var fingerprint=Fingerprint(window,matches,now,alternative);
        string Fingerprint(FacilityGuideWindow facts,FacilityMatch[] candidates,DateTime instant,bool isAlternative)=>Hash(new {facts.Fingerprint,need,date,Alternative=isAlternative,Day=DateOnly.FromDateTime(instant),Matches=candidates.Select(x=>new {x.Guide.FacilityId,x.Times,Open=FacilityNeedMatcher.OpenNow(x,instant)}),FacilityNeedMatcher.Version});
        FacilityRecommendationRun run;
        await using(var tx=db.Database.IsRelational()?await db.Database.BeginTransactionAsync(ct):null)
        {
            if(db.Database.IsRelational())await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended('facility-recommendation-quota',8))",ct);
            var instant=clock.GetUtcNow();var recent=instant.AddMinutes(-options.CacheMinutes);var since=instant.AddDays(-1);
            var cached=await db.FacilityRecommendationRuns.Where(x=>x.Actor==actor&&x.Fingerprint==fingerprint&&x.CreatedAt>=recent&&x.ResultJson!=null).OrderByDescending(x=>x.CreatedAt).FirstOrDefaultAsync(ct);
            if(cached is not null){if(tx is not null)await tx.CommitAsync(ct);return JsonSerializer.Deserialize<FacilityRecommendationView>(cached.ResultJson!,Json)!;}
            if(await db.FacilityRecommendationRuns.CountAsync(x=>x.Actor==actor&&x.CreatedAt>=since,ct)>=options.AiRequestsPerUserDay ||
               await db.FacilityRecommendationRuns.CountAsync(x=>x.CreatedAt>=since,ct)>=options.AiRequestsPerDay ||
               await db.FacilityRecommendationRuns.AnyAsync(x=>x.Actor==actor&&x.Fingerprint==fingerprint&&x.CompletedAt==null&&x.CreatedAt>=instant.AddMinutes(-1),ct))
                throw new FacilityRecommendationFailure(429,"facility_ai_quota","Yêu cầu tương tự đang xử lý hoặc đã đạt hạn mức AI. Vui lòng thử lại sau.");
            run=new(actor,fingerprint,instant);db.FacilityRecommendationRuns.Add(run);await db.SaveChangesAsync(ct);if(tx is not null)await tx.CommitAsync(ct);
        }
        var choices=matches.Select((x,index)=>new FacilityAdvisorInput(index.ToString(System.Globalization.CultureInfo.InvariantCulture),FacilityNeedMatcher.Redact(x.Guide.Name),
            FacilityNeedMatcher.Redact(x.Guide.Type??""),Preview(x.Guide.Description),x.Guide.Audience,x.Guide.MinimumAge,x.Guide.MaximumAge,x.Guide.ChildPolicy,now,x.Times)).ToArray();
        var result=await advisor.RecommendAsync(need,choices,ct);var effective=intent;
        // Model flags may strengthen safety for an explicitly child-related request, never invent children for an adult request.
        if(result is not null)effective=intent with {NeedsSupervision=intent.NeedsSupervision || intent.ForChildren&&result.NeedsSupervision};
        var eligible=Search(window,effective,now,out _).ToDictionary(x=>x.Guide.FacilityId);
        var valid=result is not null&&result.Keys.Length<=3&&result.Keys.Distinct().Count()==result.Keys.Length&&result.Keys.All(key=>choices.Any(x=>x.Key==key))&&result.Clarification is null or "NONE" or "INTEREST" or "PARTY" or "TIME" or "AGE";
        var response=!valid?Response(matches.Where(x=>x.Score>0),"FALLBACK",intent):Response(result!.Keys.Select(key=>matches[Array.FindIndex(choices,x=>x.Key==key)])
            .Where(x=>eligible.ContainsKey(x.Guide.FacilityId)).Select(x=>eligible[x.Guide.FacilityId]),"GEMINI",effective);
        if(valid && result!.Clarification is not (null or "NONE"))
        {
            var missing=FacilityNeedMatcher.Question(result.Clarification,need);
            response=missing is not null ? Clarify(missing) : Response(matches.Where(x=>x.Score>0),"FALLBACK",intent);
        }
        var fresh=await source.ReadAsync(ct);var freshNow=await LocalNow(ct);var active=await access.ActiveAsync(actor,ct);
        var freshMatches=Search(fresh,intent,freshNow,out var freshAlternative);
        var currentHash=Fingerprint(fresh,freshMatches,freshNow,freshAlternative);
        if(!active||currentHash!=fingerprint)
        {
            run.Complete(null,result?.Model,result?.InputTokens,result?.OutputTokens,clock.GetUtcNow());await db.SaveChangesAsync(ct);
            throw new FacilityRecommendationFailure(active?409:403,active?"facility_source_changed":"facility_resident_inactive",active?"Hồ sơ hoặc lịch tiện ích vừa thay đổi. Vui lòng tìm lại.":"Hồ sơ cư trú không còn đủ điều kiện xem gợi ý.");
        }
        run.Complete(JsonSerializer.Serialize(response,Json),result?.Model,result?.InputTokens,result?.OutputTokens,clock.GetUtcNow());await db.SaveChangesAsync(ct);
        logger.LogInformation("Facility discovery {RunId}: {Mode}, input tokens {Input}, output tokens {Output}",run.Id,response.Mode,run.InputTokens,run.OutputTokens);
        return response;
    }
    private static string Hash(object value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,Json)));
    private static string Preview(string text){var safe=FacilityNeedMatcher.Redact(text);return safe[..Math.Min(safe.Length,700)];}
}
