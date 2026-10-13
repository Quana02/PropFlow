using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.ServiceRequests.Contracts;

namespace PropFlow.Modules.ServiceRequests.Application.RateResidentServiceRequest;

public enum RateResidentServiceRequestOutcome { Saved, InvalidInput, ResidentResidenceNotFound, RequestNotFound, RequestNotCompleted }
public enum SaveResidentServiceRequestFeedbackOutcome { Saved, RequestNotFound, RequestNotCompleted }

public sealed record RateResidentServiceRequestResult(
    RateResidentServiceRequestOutcome Outcome,
    ResidentServiceRequestFeedbackResponse? Response = null,
    string? ErrorCode = null,
    string? Message = null);

public interface IResidentServiceRequestFeedbackStore
{
    Task<(SaveResidentServiceRequestFeedbackOutcome Outcome, ResidentServiceRequestFeedbackResponse? Response)> SaveAsync(
        Guid residentId, Guid serviceRequestId, int rating, string comment, DateTimeOffset now, CancellationToken ct);
}

public sealed class RateResidentServiceRequestHandler(
    IResidentServiceRequestFeedbackStore store,
    IResidentResidenceSource residences,
    ICurrentBuildingTimeZone buildingTimeZone,
    TimeProvider clock)
{
    public async Task<RateResidentServiceRequestResult> HandleAsync(
        Guid userId, Guid serviceRequestId, RateResidentServiceRequestRequest command, CancellationToken ct)
    {
        var comment = command.Comment?.Trim() ?? string.Empty;
        if (command.Rating is < 1 or > 5 || comment.Length is < 3 or > 1000)
            return new(RateResidentServiceRequestOutcome.InvalidInput, ErrorCode: "invalid_feedback", Message: "Vui lòng chọn từ 1 đến 5 sao và nhập nhận xét từ 3 đến 1000 ký tự.");

        var timeZoneId = await buildingTimeZone.GetAsync(ct);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        var residence = await residences.FindActiveAsync(userId, today, ct);
        if (residence is null)
            return new(RateResidentServiceRequestOutcome.ResidentResidenceNotFound, ErrorCode: "active_residence_not_found", Message: "Tài khoản chưa được liên kết với căn hộ đang cư trú.");

        var saved = await store.SaveAsync(residence.ResidentId, serviceRequestId, command.Rating, comment, now, ct);
        return saved.Outcome switch
        {
            SaveResidentServiceRequestFeedbackOutcome.Saved => new(RateResidentServiceRequestOutcome.Saved, saved.Response),
            SaveResidentServiceRequestFeedbackOutcome.RequestNotCompleted => new(RateResidentServiceRequestOutcome.RequestNotCompleted, ErrorCode: "request_not_completed", Message: "Chỉ có thể đánh giá yêu cầu đã hoàn thành."),
            _ => new(RateResidentServiceRequestOutcome.RequestNotFound, ErrorCode: "request_not_found", Message: "Không tìm thấy yêu cầu thuộc tài khoản của bạn.")
        };
    }
}
