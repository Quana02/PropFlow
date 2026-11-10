namespace PropFlow.Web.Client.Features.Complaints.Models;

public sealed record ComplaintItem(Guid Id, string ComplaintNumber, string Subject, string Status, DateTimeOffset SubmittedAt);
public sealed record ComplaintPage(IReadOnlyList<ComplaintItem> Items, int Total, int Page, int PageSize);
public sealed record ComplaintContext(Guid? ApartmentUnitId, Guid? RelatedServiceRequestId, Guid? FacilityId, Guid? EquipmentId);
public sealed record ComplaintHistory(string Type, string? FromStatus, string? ToStatus, string? Detail,
    DateTimeOffset CreatedAt, string? ActorLabel = null);
public sealed record FollowupView(Guid Id, Guid StaffUserId, string Title, string? Instruction, string Status,
    string? Result, string? StaffDisplayName = null);
public sealed record ComplaintDetail(Guid Id, string ComplaintNumber, string Subject, string Description, string Status,
    string? OfficialResponse, DateTimeOffset SubmittedAt, ComplaintContext Context, IReadOnlyList<ComplaintHistory> History, IReadOnlyList<FollowupView> Followups, string TimeZoneId);
public sealed record StaffOption(Guid UserId, string Username, string DisplayName);
public sealed record ContextOption(Guid Id, string Label);
public sealed record ComplaintContextOptions(IReadOnlyList<ContextOption> Apartments, IReadOnlyList<ContextOption> ServiceRequests,
    IReadOnlyList<ContextOption> Facilities, IReadOnlyList<ContextOption> Equipment);
public static class ComplaintLabels
{
    public static readonly string[] Statuses = ["SUBMITTED", "UNDER_REVIEW", "FOLLOW_UP", "RESOLVED", "CLOSED", "CANCELLED"];
    public static string Status(string status) => status switch
    {
        "SUBMITTED" => "Chờ tiếp nhận", "UNDER_REVIEW" => "Đang xem xét", "FOLLOW_UP" => "Đang xử lý", "RESOLVED" => "Đã giải quyết",
        "CLOSED" => "Đã đóng", "CANCELLED" => "Đã hủy", "ASSIGNED" => "Đã giao", "IN_PROGRESS" => "Đang thực hiện", "COMPLETED" => "Đã hoàn thành", _ => "Chưa xác định"
    };
    public static string Activity(string type) => type switch
    {
        "CREATED" => "Gửi phản ánh", "STATUS_CHANGED" => "Cập nhật trạng thái", "MANAGER_REVIEWED" => "Ban Quản lý xem xét",
        "FOLLOWUP_ASSIGNED" => "Giao công việc", "FOLLOWUP_UPDATED" => "Cập nhật công việc", "OFFICIAL_RESPONSE_ADDED" => "Phản hồi chính thức",
        "RESOLVED" => "Giải quyết phản ánh", "CLOSED" => "Đóng phản ánh", _ => "Cập nhật phản ánh"
    };
}
