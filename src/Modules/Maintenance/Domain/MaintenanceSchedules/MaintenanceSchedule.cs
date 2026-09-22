namespace PropFlow.Modules.Maintenance.Domain.MaintenanceSchedules;

public class MaintenanceSchedule
{
    private MaintenanceSchedule()
    {
    }

    public MaintenanceSchedule(
        string scheduleCode,
        string title,
        DateTimeOffset plannedStartAt,
        Guid createdBy,
        DateTimeOffset now,
        Guid? facilityId = null,
        Guid? equipmentId = null,
        string? description = null,
        DateTimeOffset? plannedEndAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ThrowIfEmpty(createdBy, nameof(createdBy));
        EnsureEndAfterStart(plannedStartAt, plannedEndAt);

        Id = Guid.NewGuid();
        ScheduleCode = scheduleCode.Trim();
        FacilityId = facilityId;
        EquipmentId = equipmentId;
        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        PlannedStartAt = plannedStartAt;
        PlannedEndAt = plannedEndAt;
        Status = MaintenanceScheduleStatus.ACTIVE;
        CreatedBy = createdBy;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public string ScheduleCode { get; private set; } = null!;
    public Guid? FacilityId { get; private set; }
    public Guid? EquipmentId { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public DateTimeOffset PlannedStartAt { get; private set; }
    public DateTimeOffset? PlannedEndAt { get; private set; }
    public MaintenanceScheduleStatus Status { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void UpdatePlan(
        string title,
        DateTimeOffset plannedStartAt,
        Guid updatedBy,
        DateTimeOffset now,
        Guid? facilityId = null,
        Guid? equipmentId = null,
        string? description = null,
        DateTimeOffset? plannedEndAt = null)
    {
        EnsureActive();
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ThrowIfEmpty(updatedBy, nameof(updatedBy));
        EnsureEndAfterStart(plannedStartAt, plannedEndAt);

        FacilityId = facilityId;
        EquipmentId = equipmentId;
        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        PlannedStartAt = plannedStartAt;
        PlannedEndAt = plannedEndAt;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void Complete(Guid updatedBy, DateTimeOffset now)
    {
        EnsureActive();
        ThrowIfEmpty(updatedBy, nameof(updatedBy));

        Status = MaintenanceScheduleStatus.COMPLETED;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void Cancel(Guid updatedBy, DateTimeOffset now)
    {
        EnsureActive();
        ThrowIfEmpty(updatedBy, nameof(updatedBy));

        Status = MaintenanceScheduleStatus.CANCELLED;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    private void EnsureActive()
    {
        if (Status != MaintenanceScheduleStatus.ACTIVE)
            throw new InvalidOperationException("Only active maintenance schedules can be modified.");
    }

    private static void EnsureEndAfterStart(DateTimeOffset plannedStartAt, DateTimeOffset? plannedEndAt)
    {
        if (plannedEndAt is not null && plannedEndAt < plannedStartAt)
            throw new ArgumentException("Planned end must be greater than or equal to planned start.", nameof(plannedEndAt));
    }

    private static void ThrowIfEmpty(Guid value, string paramName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException($"{paramName} cannot be empty.", paramName);
    }
}
