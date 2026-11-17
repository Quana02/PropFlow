namespace PropFlow.Web.Client.Features.Facility.Services;

// Speech repeats grounded result fields; it never invents an activity, hours, or availability.
public static class FacilityAssistantReply
{
    public static string Create(FacilityRecommendationView data)
    {
        if (data.Mode == "CLARIFY") return data.FollowUp ?? data.Message;
        if (data.Suggestions.Length == 0) return data.Message + (data.FollowUp is null ? "" : " " + data.FollowUp);
        var first = data.Suggestions[0]; var facility = first.Facility;
        var prefix = data.Mode == "GEMINI" ? $"Mình tìm được {data.Suggestions.Length} lựa chọn cho bạn." : "Mình đang tra cứu thông tin do BQL cung cấp.";
        var time = first.OpeningTimes.FirstOrDefault();
        var schedule = time is null ? "" : $" {FacilityRecommendationsApi.Weekday((int)time.Date.DayOfWeek)}, ngày {time.Date:dd/MM}, mở cửa từ {time.OpensAt:HH:mm} đến {time.ClosesAt:HH:mm}.";
        var condition = !data.ForChildren && facility.Audience!="CHILDREN" ? "" : facility.ChildPolicy == "SUPERVISED_CONTACT_REQUIRED"
            ? " Hoạt động này cần liên hệ người phụ trách để xác nhận tiếp nhận trẻ, chưa phải đăng ký thành công."
            : data.ForChildren || facility.Audience=="CHILDREN" ? " Nếu đi cùng trẻ em, cần có phụ huynh hoặc người giám hộ đi cùng." : "";
        return $"{prefix} Bạn có thể xem {facility.Name} tại {facility.Location ?? "khu tiện ích chung cư"}. {first.Availability}.{schedule} Người phụ trách là {facility.ResponsibleName}.{condition} {data.FollowUp}".Trim();
    }
}
