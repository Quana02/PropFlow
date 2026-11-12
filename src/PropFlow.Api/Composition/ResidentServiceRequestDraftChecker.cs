using PropFlow.Modules.AiClassification.Application.ResidentDraftReview;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.ServiceRequests.Contracts;

namespace PropFlow.Api.Composition;

public sealed class ResidentServiceRequestDraftChecker(IResidentDraftReviewer reviewer, IResidentResidenceSource residences,
    ICurrentBuildingTimeZone buildingTimeZone, TimeProvider clock)
{
    private static readonly IReadOnlyDictionary<string, string> MissingMessages = new Dictionary<string, string>
    {
        ["request_details"] = "Mô tả rõ việc bạn cần hỗ trợ hoặc hiện tượng đang xảy ra",
        ["service_type"] = "Bạn muốn đăng ký dịch vụ nào, ví dụ vệ sinh, sửa chữa hoặc thi công",
        ["location"] = "Vị trí xảy ra sự cố hoặc nơi cần hỗ trợ",
        ["visitor_name"] = "Họ tên người viếng thăm",
        ["visit_duration"] = "Thời gian lưu trú hoặc ngày dự kiến kết thúc lưu trú của khách",
        ["moving_items"] = "Bạn muốn chuyển những đồ gì, ví dụ nội thất, đồ gia dụng hoặc đồ chuyển nhà",
        ["moving_direction"] = "Chuyển đồ vào căn hộ, ra khỏi căn hộ hay chuyển trong tòa nhà",
        ["vehicle_type"] = "Loại xe muốn đăng ký, ví dụ xe máy hoặc ô tô",
        ["vehicle_plate"] = "Biển số xe",
        ["vehicle_color"] = "Màu xe",
        ["appointment_date"] = "Kiểm tra lại ngày thực hiện bạn đã nêu trong mô tả; lịch hẹn là thông tin tùy chọn",
        ["appointment_time"] = "Kiểm tra lại giờ hoặc buổi bạn đã nêu nếu muốn hẹn thời gian cụ thể"
    };

    public async Task<ResidentDraftReviewResult> CheckAsync(Guid actor, string? description, CancellationToken ct)
    {
        if (actor == Guid.Empty) return new(null, 401, "invalid_session", "Phiên đăng nhập không hợp lệ.");
        var text = description?.Trim() ?? "";
        if (text.Length is < 3 or > 3500) return new(null, 400, "invalid_description", "Nội dung kiểm tra cần từ 3 đến 3500 ký tự.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(await buildingTimeZone.GetAsync(ct));
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        if (await residences.FindActiveAsync(actor, today, ct) is null)
            return new(null, 403, "active_residence_not_found", "Tài khoản chưa được liên kết với căn hộ đang cư trú.");
        var prediction = reviewer.Ready ? await reviewer.CheckAsync(text, today, ct) : null;
        if (prediction is null)
            return new(new(false, [], Notice: "AI chưa kiểm tra được nội dung lúc này. Bạn có thể tự bổ sung thông tin và gửi Ban quản lý."));
        if (prediction.MissingFields.Any(code => !MissingMessages.ContainsKey(code)))
            return new(new(false, [], Notice: "Kết quả kiểm tra chưa hợp lệ. Bạn vẫn có thể tự kiểm tra và gửi yêu cầu."));
        return new(new(true, prediction.MissingFields.Distinct().Select(code => new ResidentDraftMissingInformation(code, MissingMessages[code])).ToArray(),
            prediction.PreferredDate, prediction.PreferredTimeCode));
    }
}

public sealed record ResidentDraftReviewResult(ResidentServiceRequestDraftReview? Review, int Status = 200, string? Code = null, string? Message = null);
