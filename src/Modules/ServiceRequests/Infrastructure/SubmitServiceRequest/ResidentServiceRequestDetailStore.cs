using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.ServiceRequests.Application.GetResidentServiceRequest;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Application.WorkAssignedServiceRequests;

namespace PropFlow.Modules.ServiceRequests.Infrastructure.SubmitServiceRequest;

public sealed class ResidentServiceRequestDetailStore(ServiceRequestsDbContext db, IMaintenanceAssetSource? assets = null)
    : IResidentServiceRequestDetailStore
{
    public async Task<ResidentServiceRequestDetailResponse?> GetAsync(
        Guid residentId,
        Guid serviceRequestId,
        CancellationToken ct)
    {
        var request = await db.ServiceRequests
            .AsNoTracking()
            .Where(item => item.Id == serviceRequestId && item.ResidentId == residentId)
            .Select(item => new
            {
                item.Id,
                item.RequestNumber,
                item.Title,
                item.Description,
                CategoryCode = item.Category == null ? null : item.Category.Code,
                CategoryName = item.Category == null ? null : item.Category.Name,
                Status = item.Status.ToString(),
                PriorityCode = item.FinalPriorityCode,
                item.ServiceAreaCode,
                item.FacilityId,
                item.PreferredDate,
                item.PreferredTimeCode,
                item.SubmittedAt,
                item.UpdatedAt,
                item.ResolvedAt,
                item.ClosedAt,
                item.BaseServiceCost,
                item.ApprovedAdditionalCost,
                item.AdditionalCostProposalId, item.ProposedAdditionalCost, item.AdditionalCostNote, item.AdditionalCostConsentStatus, item.AdditionalCostProposedAt, item.AdditionalCostRespondedAt,
                Rating = db.ServiceRequestFeedbacks
                    .Where(feedback => feedback.ServiceRequestId == item.Id)
                    .Select(feedback => (int?)feedback.Rating)
                    .SingleOrDefault(),
                FeedbackComment = db.ServiceRequestFeedbacks
                    .Where(feedback => feedback.ServiceRequestId == item.Id)
                    .Select(feedback => feedback.Comment)
                    .SingleOrDefault(),
                FeedbackSubmittedAt = db.ServiceRequestFeedbacks
                    .Where(feedback => feedback.ServiceRequestId == item.Id)
                    .Select(feedback => (DateTimeOffset?)feedback.SubmittedAt)
                    .SingleOrDefault()
            })
            .SingleOrDefaultAsync(ct);

        if (request is null) return null;

        var activities = await db.ServiceRequestActivities
            .AsNoTracking()
            .Where(activity => activity.ServiceRequestId == request.Id)
            .OrderBy(activity => activity.CreatedAt)
            .ThenBy(activity => activity.Id)
            .Select(activity => new
            {
                activity.Id,
                activity.ActivityType,
                activity.AssignmentId,
                activity.Title,
                activity.Detail,
                activity.FromStatus,
                activity.ToStatus,
                activity.CreatedAt
            })
            .ToArrayAsync(ct);

        var approvedResults = activities.Where(x => x.ActivityType == ServiceActivityType.RESOLVED)
            .Select(approval => activities.Where(x => x.ActivityType == ServiceActivityType.WORK_RESULT_ADDED &&
                x.AssignmentId == approval.AssignmentId && x.CreatedAt <= approval.CreatedAt)
                .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Select(x => x.Id).FirstOrDefault())
            .Where(x => x != Guid.Empty).Distinct().ToArray();
        var evidence = await db.ServiceRequestEvidenceFiles.AsNoTracking()
            .Where(x => x.ServiceRequestId == request.Id && approvedResults.Contains(x.ActivityId))
            .OrderByDescending(x => x.UploadedAt)
            .Select(x => new ResidentServiceRequestEvidenceItem(x.Id, x.FileName, x.ContentType, x.UploadedAt,
                $"/api/v1/resident/service-requests/{request.Id}/evidence/{x.Id}"))
            .ToArrayAsync(ct);

        var facility = request.FacilityId is { } facilityId && assets is not null
            ? await assets.GetFacilityAsync(facilityId, ct) : null;

        return new ResidentServiceRequestDetailResponse(
            request.Id,
            request.RequestNumber,
            request.Title,
            request.Description,
            request.CategoryCode,
            request.CategoryName,
            request.Status,
            request.PriorityCode,
            request.ServiceAreaCode,
            request.PreferredDate,
            request.PreferredTimeCode,
            request.SubmittedAt,
            request.UpdatedAt,
            request.ResolvedAt,
            request.ClosedAt,
            request.Rating,
            request.FeedbackComment,
            request.FeedbackSubmittedAt,
            activities.Select(activity => ToResidentActivity(
                activity.Id,
                activity.ActivityType,
                activity.FromStatus?.ToString(),
                activity.ToStatus?.ToString(),
                activity.CreatedAt) is var item && (activity.ActivityType == ServiceActivityType.CLOSED && activity.Title == "Cư dân đã nghiệm thu" || activity.ActivityType == ServiceActivityType.REWORK_REQUESTED && activity.ToStatus?.ToString() == "SUBMITTED")
                    ? item with { Title = activity.Title ?? item.Title, Description = activity.Detail ?? item.Description } : item).ToArray(),
            request.BaseServiceCost,
            request.ApprovedAdditionalCost, AdditionalCostConsent: request.AdditionalCostProposalId.HasValue && request.AdditionalCostConsentStatus != "AWAITING_MANAGER" ? new(request.AdditionalCostProposalId.Value, request.ProposedAdditionalCost, request.AdditionalCostNote, request.AdditionalCostConsentStatus, request.AdditionalCostProposedAt, request.AdditionalCostRespondedAt) : null,
            FacilityName: ServiceRequestAreaCodes.IsCommonArea(request.ServiceAreaCode) ? facility?.Name : null,
            Evidence: evidence);
    }

    public async Task<StaffEvidenceMetadata?> EvidenceAsync(Guid residentId, Guid requestId, Guid evidenceId, CancellationToken ct)
    {
        var detail = await GetAsync(residentId, requestId, ct);
        if (detail?.Evidence?.Any(x => x.Id == evidenceId) != true) return null;
        return await db.ServiceRequestEvidenceFiles.AsNoTracking().Where(x => x.Id == evidenceId && x.ServiceRequestId == requestId)
            .Select(x => new StaffEvidenceMetadata(x.Id, x.ServiceRequestId, x.ActivityId, x.FileName, x.ContentType, x.SizeBytes, x.StorageKey, x.UploadedBy, x.UploadedAt)).SingleOrDefaultAsync(ct);
    }

    private static ResidentServiceRequestActivityItem ToResidentActivity(
        Guid id,
        ServiceActivityType type,
        string? fromStatus,
        string? toStatus,
        DateTimeOffset createdAt)
    {
        var (title, description) = type switch
        {
            ServiceActivityType.ADDITIONAL_COST_PROPOSED => ("Ban Quản lý đang kiểm tra phí phát sinh", "Bộ phận xử lý đã báo phí để Ban Quản lý xem xét trước khi gửi bạn xác nhận."),
            ServiceActivityType.ADDITIONAL_COST_MANAGER_APPROVED => ("Cần xác nhận chi phí phát sinh", "Ban Quản lý đã duyệt báo giá. Vui lòng chấp thuận hoặc từ chối chi phí để tiếp tục xử lý."),
            ServiceActivityType.ADDITIONAL_COST_ACCEPTED => ("Đã đồng ý chi phí phát sinh", "Bạn đã chấp nhận chi phí; bộ phận xử lý được tiếp tục công việc."),
            ServiceActivityType.ADDITIONAL_COST_REJECTED => ("Đã từ chối chi phí phát sinh", "Yêu cầu dịch vụ đã tự động bị từ chối theo phản hồi của bạn."),
            ServiceActivityType.CREATED => ("Đã gửi yêu cầu", "Yêu cầu đã được ghi nhận và đang chờ Ban Quản lý tiếp nhận."),
            ServiceActivityType.STATUS_CHANGED => ("Cập nhật trạng thái", StatusDescription(toStatus)),
            ServiceActivityType.ASSIGNED => ("Đã phân công xử lý", "Ban Quản lý đã chuyển yêu cầu đến bộ phận phụ trách."),
            ServiceActivityType.REASSIGNED => ("Đã điều phối lại", "Yêu cầu đã được chuyển đến bộ phận phù hợp hơn."),
            ServiceActivityType.PROGRESS_UPDATED => ("Đã cập nhật tiến độ", "Bộ phận phụ trách đã cập nhật quá trình xử lý."),
            ServiceActivityType.WORK_RESULT_ADDED => ("Đã ghi nhận kết quả", "Kết quả thực hiện đã được gửi đến Ban Quản lý để xem xét."),
            ServiceActivityType.MANAGER_REVIEWED => ("Ban Quản lý đã xem xét", "Kết quả xử lý đang được Ban Quản lý kiểm tra."),
            ServiceActivityType.RESOLVED => ("Yêu cầu đã được xử lý", "Yêu cầu đã có kết quả xử lý và có thể được đánh giá."),
            ServiceActivityType.CLOSED => ("Yêu cầu đã hoàn tất", "Ban Quản lý đã xác nhận và đóng yêu cầu."),
            _ => ("Yêu cầu đã được cập nhật", "Tiến độ xử lý yêu cầu vừa được cập nhật.")
        };

        return new(id, type.ToString(), fromStatus, toStatus, title, description, createdAt);
    }

    private static string StatusDescription(string? status) => status switch
    {
        "UNDER_REVIEW" => "Ban Quản lý đang xem xét thông tin yêu cầu.",
        "ASSIGNED" => "Yêu cầu đã được phân công xử lý.",
        "IN_PROGRESS" => "Yêu cầu đang được thực hiện.",
        "RESOLVED" => "Yêu cầu đã có kết quả xử lý.",
        "CLOSED" => "Yêu cầu đã hoàn tất.",
        "CANCELLED" => "Yêu cầu đã được hủy.",
        _ => "Trạng thái yêu cầu vừa được cập nhật."
    };
}
