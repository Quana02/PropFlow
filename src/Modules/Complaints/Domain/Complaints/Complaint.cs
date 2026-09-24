using PropFlow.Modules.Complaints.Domain.ComplaintActivities;
using PropFlow.Modules.Complaints.Domain.ComplaintFollowups;

namespace PropFlow.Modules.Complaints.Domain.Complaints;

public class Complaint
{
    private readonly List<ComplaintFollowup> _followups = [];
    private readonly List<ComplaintActivity> _activities = [];

    private Complaint()
    {
    }

    public Complaint(
        string complaintNumber,
        Guid residentId,
        Guid residentApartmentId,
        
        string subject,
        string description,
        DateTimeOffset now,
        Guid? apartmentUnitId = null,
        Guid? relatedServiceRequestId = null,
        Guid? facilityId = null,
        Guid? equipmentId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(complaintNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ThrowIfEmpty(residentId, nameof(residentId));
        ThrowIfEmpty(residentApartmentId, nameof(residentApartmentId));
        

        Id = Guid.NewGuid();
        ComplaintNumber = complaintNumber.Trim();
        ResidentId = residentId;
        ResidentApartmentId = residentApartmentId;
       
        ApartmentUnitId = apartmentUnitId;
        RelatedServiceRequestId = relatedServiceRequestId;
        FacilityId = facilityId;
        EquipmentId = equipmentId;
        Subject = subject.Trim();
        Description = description.Trim();
        Status = ComplaintStatus.SUBMITTED;
        SubmittedAt = now;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public string ComplaintNumber { get; private set; } = null!;
    public Guid ResidentId { get; private set; }
    public Guid ResidentApartmentId { get; private set; }
    
    public Guid? ApartmentUnitId { get; private set; }
    public Guid? RelatedServiceRequestId { get; private set; }
    public Guid? FacilityId { get; private set; }
    public Guid? EquipmentId { get; private set; }
    public string Subject { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public ComplaintStatus Status { get; private set; }
    public string? OfficialResponse { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public Guid? ClosedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<ComplaintFollowup> Followups => _followups.AsReadOnly();
    public IReadOnlyCollection<ComplaintActivity> Activities => _activities.AsReadOnly();

    public void UpdateDetails(string subject, string description, DateTimeOffset now)
    {
        EnsureNotClosedOrCancelled();
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Subject = subject.Trim();
        Description = description.Trim();
        UpdatedAt = now;
    }

    public void AddOfficialResponse(string response, DateTimeOffset now)
    {
        EnsureNotClosedOrCancelled();
        ArgumentException.ThrowIfNullOrWhiteSpace(response);

        OfficialResponse = response.Trim();
        UpdatedAt = now;
    }

    public void MarkUnderReview(DateTimeOffset now)
    {
        EnsureNotClosedOrCancelled();
        if (Status != ComplaintStatus.SUBMITTED)
            throw new InvalidOperationException("Only submitted complaints can move under review.");

        Status = ComplaintStatus.UNDER_REVIEW;
        UpdatedAt = now;
    }

    public void MarkFollowUp(DateTimeOffset now)
    {
        EnsureNotClosedOrCancelled();
        if (Status is not (ComplaintStatus.UNDER_REVIEW or ComplaintStatus.FOLLOW_UP))
            throw new InvalidOperationException("Complaint must be under review before follow-up handling.");

        Status = ComplaintStatus.FOLLOW_UP;
        UpdatedAt = now;
    }

    public void Resolve(DateTimeOffset now)
    {
        EnsureNotClosedOrCancelled();
        if (Status is not (ComplaintStatus.UNDER_REVIEW or ComplaintStatus.FOLLOW_UP))
            throw new InvalidOperationException("Complaint must be reviewed or in follow-up before resolution.");

        Status = ComplaintStatus.RESOLVED;
        ResolvedAt = now;
        UpdatedAt = now;
    }

    public void Close(Guid closedBy, DateTimeOffset now)
    {
        ThrowIfEmpty(closedBy, nameof(closedBy));
        EnsureNotClosedOrCancelled();
        if (Status != ComplaintStatus.RESOLVED)
            throw new InvalidOperationException("Only resolved complaints can be closed.");
        if (string.IsNullOrWhiteSpace(OfficialResponse))
            throw new InvalidOperationException("Complaint must have an official response before closure.");

        Status = ComplaintStatus.CLOSED;
        ClosedBy = closedBy;
        ClosedAt = now;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        EnsureNotClosedOrCancelled();
        Status = ComplaintStatus.CANCELLED;
        UpdatedAt = now;
    }

    private void EnsureNotClosedOrCancelled()
    {
        if (Status is ComplaintStatus.CLOSED or ComplaintStatus.CANCELLED)
            throw new InvalidOperationException("Closed or cancelled complaints cannot be modified.");
    }

    private static void ThrowIfEmpty(Guid value, string paramName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException($"{paramName} cannot be empty.", paramName);
    }
}
