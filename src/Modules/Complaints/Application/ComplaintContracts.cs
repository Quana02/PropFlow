using PropFlow.Modules.Complaints.Domain.Complaints;
using PropFlow.Modules.Complaints.Domain.ComplaintActivities;
using PropFlow.Modules.Complaints.Domain.ComplaintFollowups;

namespace PropFlow.Modules.Complaints.Application;

public sealed record SubmitComplaint(string Subject, string Description);
public sealed record ComplaintContext(Guid? ApartmentUnitId, Guid? RelatedServiceRequestId, Guid? FacilityId, Guid? EquipmentId);
public sealed record AssignComplaintFollowup(Guid StaffUserId, string Title, string? Instruction);
public sealed record FollowupUpdate(bool Complete, string? Result);
public sealed record OfficialComplaintResponse(string Response);
public sealed record ComplaintItem(Guid Id, string ComplaintNumber, string Subject, string Status, DateTimeOffset SubmittedAt);
public sealed record ComplaintPage(IReadOnlyList<ComplaintItem> Items, int Total, int Page, int PageSize);
public sealed record ComplaintHistory(string Type, string? FromStatus, string? ToStatus, string? Detail,
    DateTimeOffset CreatedAt, string? ActorLabel = null);
public sealed record FollowupView(Guid Id, Guid StaffUserId, string Title, string? Instruction, string Status,
    string? Result, string? StaffDisplayName = null);
public sealed record ComplaintDetail(Guid Id, string ComplaintNumber, string Subject, string Description, string Status,
    string? OfficialResponse, DateTimeOffset SubmittedAt, ComplaintContext Context,
    IReadOnlyList<ComplaintHistory> History, IReadOnlyList<FollowupView> Followups, string TimeZoneId);
public sealed record ComplaintResult<T>(int Status, T? Data = default, string? Message = null);
public sealed record ComplaintClosed(Guid EventId, Guid ComplaintId, Guid ResidentId, DateTimeOffset OccurredAt);

// Source workflow and communication are committed together by the composition adapter.
public interface IComplaintCommitter
{
    Task SaveAsync(ComplaintClosed? closed, CancellationToken ct);
}

public interface IComplaintStore
{
    Task<ComplaintPage> ListAsync(Guid? residentId, Guid? staffId, string? search, ComplaintStatus? status, int page, CancellationToken ct);
    Task<Complaint?> FindAsync(Guid id, CancellationToken ct);
    void Add(Complaint complaint);
    void Add(ComplaintActivity activity);
    void Add(ComplaintFollowup followup);
}

public sealed record ContextOption(Guid Id, string Label);
public sealed record ComplaintContextOptions(IReadOnlyList<ContextOption> Apartments, IReadOnlyList<ContextOption> ServiceRequests,
    IReadOnlyList<ContextOption> Facilities, IReadOnlyList<ContextOption> Equipment);
public interface IComplaintContextSource
{
    Task<bool> IsValidAsync(ComplaintContext context, CancellationToken ct);
    Task<ComplaintContextOptions> OptionsAsync(string? search, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, string>> DisplayNamesAsync(Guid[] userIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}
