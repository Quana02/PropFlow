using System.Reflection;
using PropFlow.Modules.Maintenance.Domain.MaintenanceAssignments;
using PropFlow.Modules.Maintenance.Domain.MaintenanceResults;
using PropFlow.Modules.Maintenance.Domain.MaintenanceSchedules;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTaskActivities;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;

namespace PropFlow.UnitTests;

public class MaintenanceTests
{
    private readonly DateTimeOffset _now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MaintenanceSchedule_Constructor_CreatesActiveScheduleWithScalarReferences()
    {
        var schedule = NewSchedule();

        Assert.NotEqual(Guid.Empty, schedule.Id);
        Assert.Equal(MaintenanceScheduleStatus.ACTIVE, schedule.Status);
        Assert.Equal("MS-2026-0001", schedule.ScheduleCode);
        Assert.Equal(_now, schedule.CreatedAt);
        Assert.Equal(_now, schedule.UpdatedAt);
        Assert.NotNull(schedule.FacilityId);
        Assert.NotNull(schedule.EquipmentId);
        Assert.Throws<ArgumentException>(() => new MaintenanceSchedule("", Guid.NewGuid(), "Title", _now, Guid.NewGuid(), _now));
        Assert.Throws<ArgumentException>(() => new MaintenanceSchedule("MS-2", Guid.Empty, "Title", _now, Guid.NewGuid(), _now));
        Assert.Throws<ArgumentException>(() => new MaintenanceSchedule("MS-3", Guid.NewGuid(), "Title", _now, Guid.NewGuid(), _now, plannedEndAt: _now.AddMinutes(-1)));
    }

    [Fact]
    public void MaintenanceSchedule_CanBeUpdatedCompletedOrCancelledOnlyWhileActive()
    {
        var schedule = NewSchedule();
        var updatedBy = Guid.NewGuid();
        var updatedAt = _now.AddDays(1);

        schedule.UpdatePlan("Monthly pump inspection", updatedAt, updatedBy, updatedAt, description: "Inspect pumps");
        schedule.Complete(updatedBy, updatedAt.AddHours(1));

        Assert.Equal(MaintenanceScheduleStatus.COMPLETED, schedule.Status);
        Assert.Equal(updatedBy, schedule.UpdatedBy);
        Assert.Throws<InvalidOperationException>(() => schedule.Cancel(updatedBy, updatedAt.AddHours(2)));

        var cancelled = NewSchedule("MS-2026-0002");
        cancelled.Cancel(updatedBy, updatedAt);
        Assert.Equal(MaintenanceScheduleStatus.CANCELLED, cancelled.Status);
        Assert.Throws<InvalidOperationException>(() => cancelled.UpdatePlan("New", updatedAt, updatedBy, updatedAt));
    }

    [Fact]
    public void MaintenanceTask_Constructor_CreatesOpenTaskWithSourcesAndTimingRules()
    {
        var task = NewTask();

        Assert.NotEqual(Guid.Empty, task.Id);
        Assert.Equal(MaintenanceTaskStatus.OPEN, task.Status);
        Assert.Equal("HIGH", task.PriorityCode);
        Assert.NotNull(task.ScheduleId);
        Assert.NotNull(task.SourceServiceRequestId);
        Assert.NotNull(task.SourceComplaintId);
        Assert.Throws<ArgumentException>(() => new MaintenanceTask("", Guid.NewGuid(), "Title", Guid.NewGuid(), _now));
        Assert.Throws<ArgumentException>(() => new MaintenanceTask("MT-2", Guid.Empty, "Title", Guid.NewGuid(), _now));
        Assert.Throws<ArgumentException>(() => new MaintenanceTask("MT-3", Guid.NewGuid(), "Title", Guid.NewGuid(), _now, plannedStartAt: _now.AddHours(2), dueAt: _now.AddHours(1)));
    }

    [Fact]
    public void MaintenanceTask_Lifecycle_RequiresAssignedBeforeStartAndCompletedBeforeClose()
    {
        var task = NewTask();
        var startedAt = _now.AddMinutes(30);
        var completedAt = _now.AddHours(2);
        var closedAt = _now.AddHours(3);
        var closedBy = Guid.NewGuid();

        Assert.Throws<InvalidOperationException>(() => task.Start(startedAt));

        task.MarkAssigned(_now.AddMinutes(5));
        task.Start(startedAt);
        task.Complete(completedAt);
        task.Close(closedBy, closedAt);

        Assert.Equal(MaintenanceTaskStatus.CLOSED, task.Status);
        Assert.Equal(startedAt, task.StartedAt);
        Assert.Equal(completedAt, task.CompletedAt);
        Assert.Equal(closedAt, task.ClosedAt);
        Assert.Equal(closedBy, task.ClosedBy);
        Assert.Throws<InvalidOperationException>(() => task.Cancel(closedAt.AddMinutes(1)));
    }

    [Fact]
    public void MaintenanceTask_TerminalStatesRejectFurtherTransitions()
    {
        var cancelled = NewTask("MT-2026-0002");
        cancelled.Cancel(_now.AddMinutes(1));

        Assert.Equal(MaintenanceTaskStatus.CANCELLED, cancelled.Status);
        Assert.Throws<InvalidOperationException>(() => cancelled.MarkAssigned(_now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => cancelled.UpdateDetails("New", _now.AddMinutes(3)));

        var completed = NewTask("MT-2026-0003");
        completed.MarkAssigned(_now.AddMinutes(1));
        completed.Start(_now.AddMinutes(2));
        Assert.Throws<ArgumentException>(() => completed.Complete(_now.AddMinutes(1)));
    }

    [Fact]
    public void MaintenanceAssignment_Lifecycle_AllowsActiveWorkAndRejectsTerminalMutation()
    {
        var assignment = new MaintenanceAssignment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), _now, "Initial");
        var startedAt = _now.AddMinutes(10);
        var completedAt = _now.AddHours(1);

        assignment.Start(startedAt);
        assignment.Complete(completedAt);

        Assert.Equal(AssignmentStatus.COMPLETED, assignment.Status);
        Assert.Equal(startedAt, assignment.StartedAt);
        Assert.Equal(completedAt, assignment.CompletedAt);
        Assert.Equal(completedAt, assignment.EndedAt);
        Assert.False(assignment.IsActive);
        Assert.Throws<InvalidOperationException>(() => assignment.Cancel(completedAt.AddMinutes(1)));
        Assert.Throws<ArgumentException>(() => new MaintenanceAssignment(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), _now));
    }

    [Fact]
    public void MaintenanceAssignment_ReassignAndCancel_EndActiveAssignment()
    {
        var reassigned = new MaintenanceAssignment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), _now);
        var cancelled = new MaintenanceAssignment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), _now);

        reassigned.Reassign(_now.AddMinutes(20));
        cancelled.Cancel(_now.AddMinutes(30));

        Assert.Equal(AssignmentStatus.REASSIGNED, reassigned.Status);
        Assert.Equal(AssignmentStatus.CANCELLED, cancelled.Status);
        Assert.False(reassigned.IsActive);
        Assert.False(cancelled.IsActive);
    }

    [Fact]
    public void MaintenanceResult_Constructor_CreatesSubmittedAttemptAndValidatesRequiredFields()
    {
        var result = NewResult();

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(MaintenanceResultStatus.SUBMITTED, result.ResultStatus);
        Assert.Equal(1, result.AttemptNo);
        Assert.Equal(_now, result.SubmittedAt);
        Assert.Throws<ArgumentOutOfRangeException>(() => new MaintenanceResult(Guid.NewGuid(), 0, Guid.NewGuid(), "Summary", _now));
        Assert.Throws<ArgumentException>(() => new MaintenanceResult(Guid.NewGuid(), 1, Guid.Empty, "Summary", _now));
        Assert.Throws<ArgumentException>(() => new MaintenanceResult(Guid.NewGuid(), 1, Guid.NewGuid(), "", _now));
    }

    [Fact]
    public void MaintenanceResult_ManagerReview_IsTerminalForThatSubmission()
    {
        var approved = NewResult();
        var reviewedBy = Guid.NewGuid();
        var reviewedAt = _now.AddHours(1);

        approved.Approve(reviewedBy, reviewedAt, "Looks good");

        Assert.Equal(MaintenanceResultStatus.APPROVED, approved.ResultStatus);
        Assert.Equal(reviewedBy, approved.ReviewedBy);
        Assert.Equal(reviewedAt, approved.ReviewedAt);
        Assert.Throws<InvalidOperationException>(() => approved.RequestRevision(reviewedBy, reviewedAt.AddMinutes(1)));

        var revision = NewResult(attemptNo: 2);
        revision.RequestRevision(reviewedBy, reviewedAt);
        Assert.Equal(MaintenanceResultStatus.REVISION_REQUIRED, revision.ResultStatus);
        Assert.Throws<ArgumentException>(() => NewResult(attemptNo: 3).Approve(reviewedBy, _now.AddMinutes(-1)));
    }

    [Fact]
    public void MaintenanceTaskActivity_IsAppendOnlySnapshot()
    {
        var taskId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        var actor = Guid.NewGuid();

        var activity = new MaintenanceTaskActivity(
            taskId,
            MaintenanceActivityType.STATUS_CHANGED,
            _now,
            assignmentId,
            MaintenanceTaskStatus.OPEN,
            MaintenanceTaskStatus.ASSIGNED,
            "Assigned",
            actor);

        Assert.NotEqual(Guid.Empty, activity.Id);
        Assert.Equal(taskId, activity.MaintenanceTaskId);
        Assert.Equal(assignmentId, activity.AssignmentId);
        Assert.Equal(MaintenanceActivityType.STATUS_CHANGED, activity.ActivityType);
        Assert.Equal(MaintenanceTaskStatus.OPEN, activity.FromStatus);
        Assert.Equal(MaintenanceTaskStatus.ASSIGNED, activity.ToStatus);
        Assert.Equal(actor, activity.PerformedBy);

        var publicMethods = typeof(MaintenanceTaskActivity)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .ToList();
        Assert.Empty(publicMethods);
    }

    private MaintenanceSchedule NewSchedule(string code = "MS-2026-0001")
    {
        return new MaintenanceSchedule(
            code,
            Guid.NewGuid(),
            "Quarterly generator maintenance",
            _now.AddDays(1),
            Guid.NewGuid(),
            _now,
            facilityId: Guid.NewGuid(),
            equipmentId: Guid.NewGuid(),
            description: "Inspect generator",
            plannedEndAt: _now.AddDays(1).AddHours(2));
    }

    private MaintenanceTask NewTask(string taskNumber = "MT-2026-0001")
    {
        return new MaintenanceTask(
            taskNumber,
            Guid.NewGuid(),
            "Repair water pump",
            Guid.NewGuid(),
            _now,
            scheduleId: Guid.NewGuid(),
            sourceServiceRequestId: Guid.NewGuid(),
            sourceComplaintId: Guid.NewGuid(),
            facilityId: Guid.NewGuid(),
            equipmentId: Guid.NewGuid(),
            description: "Pump is noisy",
            priorityCode: "high",
            plannedStartAt: _now.AddHours(1),
            dueAt: _now.AddHours(4));
    }

    private MaintenanceResult NewResult(int attemptNo = 1)
    {
        return new MaintenanceResult(
            Guid.NewGuid(),
            attemptNo,
            Guid.NewGuid(),
            "Pump repaired",
            _now,
            workPerformed: "Replaced seal",
            issueFound: "Worn seal",
            partsOrResourcesUsed: "Seal kit",
            recommendation: "Monitor vibration");
    }
}
