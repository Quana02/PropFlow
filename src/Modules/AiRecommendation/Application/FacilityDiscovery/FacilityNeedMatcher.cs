using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.Modules.AiRecommendation.Application.FacilityDiscovery;

public sealed record FacilityNeedIntent(bool ForChildren,bool NeedsSupervision,int? Age,int[] Weekdays,TimeOnly? At=null);
public sealed record FacilityMatch(FacilityGuideView Guide,FacilityVisitTime[] Times,int Score);
public sealed record FacilityConversationContext(bool ActivityKnown,bool CompanionsKnown,bool TimeKnown);

public static class FacilityNeedMatcher
{
    public const string Version="facility-conversation-v3";
    private const string DayReference=@"ngay mai|hom nay|bay gio|hien tai|thu\s*(?:[2-7]|hai|ba|tu|nam|sau|bay)\b|chu nhat|cuoi tuan|\d{4}-\d{2}-\d{2}|\d{1,2}/\d{1,2}";
    public static string Normalize(string text) => new string(text.ToLowerInvariant().Replace('đ','d').Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark).ToArray());
    public static string Redact(string text)
    {
        // Keep the B7 credential boundary for both resident needs and public guide context sent to providers.
        text=Regex.Replace(text,"""
            (?<![\w-])eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+(?![\w-])
            |\b(?:api[ _-]*key|access[ _-]*token|refresh[ _-]*token|jwt|authorization|otp|mật[ \t]+khẩu|password)
            [ \t]*[:=][ \t]*(?:bearer[ \t]+)?(?:"[^"\r\n]*"|'[^'\r\n]*'|[^\s,;]+)
            ""","[thông tin đã ẩn]",RegexOptions.IgnoreCase|RegexOptions.IgnorePatternWhitespace|RegexOptions.CultureInvariant);
        text=Regex.Replace(text,@"[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}|(?<!\d)(?:\+?84|0)(?:[ .-]?\d){8,10}(?!\d)|\b\d{9,12}\b","[thông tin đã ẩn]");
        return Regex.Replace(text,@"(?i)(?:tên(?:\s+là)?|tôi\s+là)\s+\p{L}+(?:\s+\p{L}+){1,3}","[tên đã ẩn]");
    }
    public static FacilityNeedIntent Interpret(string need)
    {
        var text=Normalize(need); var ageMatch=Regex.Matches(text,@"\b(\d{1,3})\s*tuoi\b").LastOrDefault();
        int? age=ageMatch is not null && int.TryParse(ageMatch.Groups[1].Value,out var number) && number<=120 ? number:null;
        var children=age is <18 || Regex.IsMatch(text,@"tre em|tre nho|con nho|em be|\bbe\b|cac con|con toi");
        if(Regex.IsMatch(text,@"khong (?:co|di cung|cho) (?:tre em|tre nho|con nho|be)"))children=false;
        var supervision=Regex.IsMatch(text,@"trong tre|giu tre|gui be") || children&&Regex.IsMatch(text,@"khong co (?:nguoi lon|phu huynh|nguoi giam ho)|(?:cha me|bo me|vo chong).*(?:di lam|len cong ty|den cong ty)|can nguoi trong");
        if(Regex.IsMatch(text,@"khong can (?:trong|giu)|(?:nguoi lon|phu huynh).*di cung"))supervision=false;
        var dayText=LatestDayTurn(text);
        var days=new HashSet<int>();
        if(Regex.IsMatch(dayText,@"thu\s*(?:bay|7)\b"))days.Add(6);
        if(Regex.IsMatch(dayText,@"chu nhat|\bcn\b"))days.Add(0);
        foreach(var pair in new[]{("hai",1),("ba",2),("tu",3),("nam",4),("sau",5)})
            if(Regex.IsMatch(dayText,@"thu\s*(?:"+pair.Item1+"|"+(pair.Item2+1)+@")\b"))days.Add(pair.Item2);
        if(days.Count==0&&dayText.Contains("cuoi tuan")){days.Add(6);days.Add(0);}
        var timeText=text.Split("bo sung:").LastOrDefault(x=>Regex.IsMatch(x,@"bay gio|hien tai|\d\s*(?:gio|h|:)"))??text;
        var hour=Regex.Matches(timeText,@"\b([01]?\d|2[0-3])(?:\s*(?:gio|h)(?:\s*([0-5]\d))?|:([0-5]\d))\b").LastOrDefault();
        TimeOnly? at=null;
        if(hour is not null)
        {
            var h=int.Parse(hour.Groups[1].Value,CultureInfo.InvariantCulture);
            var minute=hour.Groups[2].Success?hour.Groups[2].Value:hour.Groups[3].Success?hour.Groups[3].Value:"0";
            if(h<12&&Regex.IsMatch(timeText[(hour.Index+hour.Length)..],@"^\s*(?:buoi\s*)?(?:chieu|toi)\b"))h+=12;
            at=new(h,int.Parse(minute,CultureInfo.InvariantCulture));
        }
        return new(children||supervision,supervision,age,days.Order().ToArray(),at);
    }
    public static DateOnly? Date(string need,DateOnly today)
    {
        var text=LatestDayTurn(Normalize(need));
        var match=Regex.Matches(text,@"ngay mai|hom nay|bay gio|hien tai|\b\d{4}-\d{2}-\d{2}\b|\b\d{1,2}/\d{1,2}(?:/\d{4})?\b").LastOrDefault();
        if(match is null)return null;
        if(match.Value=="ngay mai")return today.AddDays(1);
        if(match.Value is "hom nay" or "bay gio" or "hien tai")return today;
        return DateOnly.TryParseExact(match.Value,["yyyy-MM-dd","d/M/yyyy","d/M"],CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)
            ? match.Value.Count(x=>x=='/')==1?new(today.Year,date.Month,date.Day):date:null;
    }
    private static string LatestDayTurn(string normalized)=>normalized.Split("bo sung:").LastOrDefault(x=>Regex.IsMatch(x,DayReference))??normalized;
    private static string SearchText(string need)=>Regex.Replace(Normalize(need),@"\b(?:chill|relax)\b","thu gian");
    public static FacilityConversationContext Context(string need)
    {
        var text=SearchText(need);
        return new(
            Regex.IsMatch(text,@"gym|tap (?:luyen|the|the thao)|boi|doc sach|thu vien|di dao|di bo|cau long|bong|yoga|vui choi|tre em|tre nho|con nho|\bbe\b|thu gian|yen tinh|van dong|ca phe|gap go"),
            Regex.IsMatch(text,@"\b(?:mot|1)\s*minh\b|ban than|ca nhan|di rieng|vo chong|gia dinh|ban be|nguoi yeu|di cung|tre em|tre nho|con nho|\bbe\b"),
            Regex.IsMatch(text,DayReference+@"|\d{1,2}\s*(?:gio|h|:)|bat ky luc nao"));
    }
    public static string? Question(string kind,string need)
    {
        var known=Context(need);
        return kind switch {
            "INTEREST" when !known.ActivityKnown=>"Bạn thích vận động, thư giãn yên tĩnh hay gặp gỡ mọi người?",
            "PARTY" when !known.CompanionsKnown=>"Bạn định đi một mình hay cùng ai?",
            "TIME" when !known.TimeKnown=>"Bạn muốn đi vào ngày nào, khoảng mấy giờ?",
            "AGE" when Interpret(need) is {ForChildren:true,Age:null}=>"Trẻ bao nhiêu tuổi?",
            _=>null };
    }
    // Vague moods are conversation starters, not evidence of an activity or family composition.
    public static string? Clarification(string need)
    {
        // A vague opening must not keep triggering the same question after a new answer.
        var text=Normalize(need).Split("bo sung:").Last();
        var known=Context(need);
        if(!known.ActivityKnown && Regex.IsMatch(text,@"chan|buon|khong biet|lam gi|goi y|tien ich|hoat dong|giai tri|vui ve|cuoi tuan"))
            return string.Join(" ",new[]{Question("INTEREST",need),Question("PARTY",need),Question("TIME",need)}.Where(x=>x is not null));
        if(Regex.IsMatch(text,@"\b(?:sau do|luc khac|hom khac|de sau|lat nua)\b") && !known.TimeKnown)
            return Question("TIME",need);
        return null;
    }
    public static bool OpenNow(FacilityMatch match,DateTime now)=>match.Times.Any(x=>x.Date==DateOnly.FromDateTime(now)&&x.OpensAt<=TimeOnly.FromDateTime(now)&&x.ClosesAt>TimeOnly.FromDateTime(now));
    public static string Availability(FacilityMatch match,DateTime now)
    {
        var first=match.Times[0];var today=DateOnly.FromDateTime(now);
        if(OpenNow(match,now))return $"Đang trong giờ mở cửa · đến {first.ClosesAt:HH:mm}";
        var day=first.Date==today?"Hôm nay":first.Date==today.AddDays(1)?"Ngày mai":$"Ngày {first.Date:dd/MM}";
        return $"{day} · khung tiếp theo {first.OpensAt:HH:mm}–{first.ClosesAt:HH:mm}";
    }
    public static ResidentFacilityCard Card(FacilityGuideView guide) => new(guide.FacilityId,guide.Name,guide.Type,guide.Location,guide.Description,
        guide.Audience,guide.MinimumAge,guide.MaximumAge,guide.ChildPolicy,guide.SupervisionNote,guide.Rules,guide.Hours,guide.ClosedDates,
        guide.ResponsibleName??"Ban quản lý đang cập nhật",guide.ContactDetails);
    public static FacilityMatch[] Match(FacilityGuideWindow window,FacilityNeedIntent intent,string need,DateOnly? preferredDate,DateTime now)
    {
        var today=DateOnly.FromDateTime(now); var tokens=Regex.Matches(SearchText(need),@"[a-z0-9]+") .Select(x=>x.Value)
            .Where(x=>x.Length>1 && !StopWords.Contains(x)).Distinct().ToArray();
        var result=new List<FacilityMatch>();
        foreach(var guide in window.Guides.Where(x=>x.Published&&x.Status=="ACTIVE"))
        {
            if(!intent.ForChildren && (guide.Audience=="CHILDREN" || guide.MaximumAge is <18))continue;
            if(intent.ForChildren && (guide.ChildPolicy=="NOT_FOR_CHILDREN" || guide.Audience is not ("CHILDREN" or "FAMILY") || guide.MinimumAge is >=18))continue;
            if(intent.Age is { } age && (guide.MinimumAge>age || guide.MaximumAge<age))continue;
            if(intent.NeedsSupervision && (guide.ChildPolicy!="SUPERVISED_CONTACT_REQUIRED" || string.IsNullOrWhiteSpace(guide.SupervisionNote) || string.IsNullOrWhiteSpace(guide.ResponsibleName)))continue;
            var times=new List<FacilityVisitTime>();
            foreach(var date in preferredDate is { } chosen ? new[]{chosen} : Enumerable.Range(0,14).Select(today.AddDays))
            {
                if(intent.Weekdays.Length>0&&!intent.Weekdays.Contains((int)date.DayOfWeek) || guide.ClosedDates.Contains(date))continue;
                foreach(var opening in guide.Hours.Where(x=>x.DayOfWeek==(int)date.DayOfWeek && date.ToDateTime(x.ClosesAt)>now).OrderBy(x=>x.OpensAt))
                    if(intent.At is null || (opening.OpensAt<=intent.At && opening.ClosesAt>intent.At && date.ToDateTime(intent.At.Value)>=now))
                        times.Add(new(date,opening.OpensAt,opening.ClosesAt));
            }
            if(times.Count==0)continue;
            var text=Normalize(guide.Name+" "+guide.Type+" "+guide.Description);
            var score=tokens.Count(token=>Regex.IsMatch(text,@"\b"+Regex.Escape(token)+@"\b"));
            if(intent.ForChildren&&guide.Audience is "CHILDREN" or "FAMILY")score+=4;
            if(intent.NeedsSupervision)score+=3;
            result.Add(new(guide,times.Take(4).ToArray(),score));
        }
        return result.OrderByDescending(x=>OpenNow(x,now)).ThenBy(x=>x.Times[0].Date.ToDateTime(x.Times[0].OpensAt)>now?x.Times[0].Date.ToDateTime(x.Times[0].OpensAt):now)
            .ThenByDescending(x=>x.Score).ThenBy(x=>x.Guide.Name).ToArray();
    }
    private static readonly HashSet<string> StopWords=["toi","muon","can","cho","voi","va","cac","khu","vuc","cuoi","tuan","thu","bay","chu","nhat","chung","cu","nao","mot","nhung","duoc","khong","tai","trong","tim","phu","hop","tuoi"];
}
