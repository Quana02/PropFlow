using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestCategories;

namespace PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;

public class ServiceRequest
{
    private readonly List<ServiceRequestAssignment> _assignments = [];
    private readonly List<ServiceRequestActivity> _activities = [];

    private ServiceRequest()
    {
    }

    public ServiceRequest(
        string requestNumber,
        Guid residentId,
        Guid residentApartmentId,
        Guid buildingId,
        string title,
        string description,
        DateTimeOffset now,
        Guid? apartmentUnitId = null,
        Guid? categoryId = null,
        Guid? facilityId = null,
        Guid? equipmentId = null,
        string? finalPriorityCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ThrowIfEmpty(residentId, nameof(residentId));
        ThrowIfEmpty(residentApartmentId, nameof(residentApartmentId));
        ThrowIfEmpty(buildingId, nameof(buildingId));

        Id = Guid.NewGuid();
        RequestNumber = requestNumber.Trim();
        ResidentId = residentId;
        ResidentApartmentId = residentApartmentId;
        BuildingId = buildingId;
        ApartmentUnitId = apartmentUnitId;
        CategoryId = categoryId;
        FacilityId = facilityId;
        EquipmentId = equipmentId;
        Title = title.Trim();
        Description = description.Trim();
        FinalPriorityCode = string.IsNullOrWhiteSpace(finalPriorityCode) ? null : finalPriorityCode.Trim().ToUpperInvariant();
        Status = ServiceRequestStatus.SUBMITTED;
        SubmittedAt = now;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public string RequestNumber { get; private set; } = null!;
    public Guid ResidentId { get; private set; }
    public Guid ResidentApartmentId { get; private set; }
    public Guid BuildingId { get; private set; }
    public Guid? ApartmentUnitId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public Guid? FacilityId { get; private set; }
    public Guid? EquipmentId { get; private set; }
    public string Title { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public string? FinalPriorityCode { get; private set; }
    public ServiceRequestStatus Status { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public Guid? ClosedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public ServiceRequestCategory? Category { get; private set; }
    public IReadOnlyCollection<ServiceRequestAssignment> Assignments => _assignments.AsReadOnly();
    public IReadOnlyCollection<ServiceRequestActivity> Activities => _activities.AsReadOnly();

    public void UpdateDetails(string title, string description, Guid? categoryId, string? finalPriorityCode, DateTimeOffset now)
    {
        EnsureNotClosedOrCancelled();
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Title = title.Trim();
        Description = description.Trim();
        CategoryId = categoryId;
        FinalPriorityCode = string.IsNullOrWhiteSpace(finalPriorityCode) ? null : finalPriorityCode.Trim().ToUpperInvariant();
        UpdatedAt = now;
    }

    public void MarkUnderReview(DateTimeOffset now)
    {
        EnsureNotClosedOrCancelled();
        if (Status != ServiceRequestStatus.SUBMITTED)
            throw new InvalidOperationException("Only submitted service requests can move under review.");

        Status = ServiceRequestStatus.UNDER_REVIEW;
        UpdatedAt = now;
    }

    public void AssociatePropertyContext(
        Guid buildingId,
        Guid? apartmentUnitId,
        Guid? facilityId,
        Guid? equipmentId,
        DateTimeOffset now)
    {
        EnsureNotClosedOrCancelled();
        ThrowIfEmpty(buildingId, nameof(buildingId));

        BuildingId = buildingId;
        ApartmentUnitId = apartmentUnitId;
        FacilityId = facilityId;
        EquipmentId = equipmentId;
        UpdatedAt = now;
    }

    public void MarkAssigned(DateTimeOffset now)
    {
        EnsureNotClosedOrCancelled();
        if (Status is not (ServiceRequestStatus.UNDER_REVIEW or ServiceRequestStatus.ASSIGNED or ServiceRequestStatus.IN_PROGRESS))
            throw new InvalidOperationException("Service request must be under review or already in operational handling before assignment.");

        Status = ServiceRequestStatus.ASSIGNED;
        UpdatedAt = now;
    }

    public void MarkInProgress(DateTimeOffset now)
    {
        EnsureNotClosedOrCancelled();
        if (Status is not (ServiceRequestStatus.ASSIGNED or ServiceRequestStatus.IN_PROGRESS))
            throw new InvalidOperationException("Only assigned service requests can move in progress.");

        Status = ServiceRequestStatus.IN_PROGRESS;
        UpdatedAt = now;
    }

    public void Resolve(DateTimeOffset now)
    {
        EnsureNotClosedOrCancelled();
        if (Status is not (ServiceRequestStatus.ASSIGNED or ServiceRequestStatus.IN_PROGRESS))
            throw new InvalidOperationException("Only assigned or in-progress service requests can be resolved.");

        Status = ServiceRequestStatus.RESOLVED;
        ResolvedAt = now;
        UpdatedAt = now;
    }

    public void Close(Guid closedBy, DateTimeOffset now)
    {
        ThrowIfEmpty(closedBy, nameof(closedBy));
        EnsureNotClosedOrCancelled();
        if (Status != ServiceRequestStatus.RESOLVED)
            throw new InvalidOperationException("Only resolved service requests can be closed.");

        Status = ServiceRequestStatus.CLOSED;
        ClosedBy = closedBy;
        ClosedAt = now;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        EnsureNotClosedOrCancelled();
        Status = ServiceRequestStatus.CANCELLED;
        UpdatedAt = now;
    }

    private void EnsureNotClosedOrCancelled()
    {
        if (Status is ServiceRequestStatus.CLOSED or ServiceRequestStatus.CANCELLED)
            throw new InvalidOperationException("Closed or cancelled service requests cannot be modified.");
    }

    private static void ThrowIfEmpty(Guid value, string paramName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException($"{paramName} cannot be empty.", paramName);
    }
}
