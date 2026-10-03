using PropFlow.Web.Client.Features.Maintenance.Models;

namespace PropFlow.UnitTests;

public sealed class MaintenanceProgressLifecycleMapperTests
{
    [Theory]
    [InlineData(MaintenanceTaskStatus.OPEN, 0)]
    [InlineData(MaintenanceTaskStatus.ASSIGNED, 1)]
    [InlineData(MaintenanceTaskStatus.IN_PROGRESS, 2)]
    [InlineData(MaintenanceTaskStatus.COMPLETED, 4)]
    [InlineData(MaintenanceTaskStatus.CLOSED, 5)]
    public void Map_UsesOnlyConfirmedTaskLifecycle(MaintenanceTaskStatus status, int expectedStep)
    {
        var lifecycle = MaintenanceProgressLifecycleMapper.Map(Task(status));

        Assert.Equal(expectedStep, lifecycle.CurrentStep);
        Assert.False(lifecycle.IsCancelled);
    }

    [Fact]
    public void Map_UsesRealWorkLogToShowInProgressStep()
    {
        var lifecycle = MaintenanceProgressLifecycleMapper.Map(Task(MaintenanceTaskStatus.IN_PROGRESS) with
        {
            LastProgressActivityType = MaintenanceActivityType.WORK_LOG_ADDED
        });

        Assert.Equal(3, lifecycle.CurrentStep);
    }

    [Fact]
    public void Map_ShowsCancelledSeparatelyWithoutCompletingLifecycle()
    {
        var lifecycle = MaintenanceProgressLifecycleMapper.Map(Task(MaintenanceTaskStatus.CANCELLED));

        Assert.Equal(0, lifecycle.CurrentStep);
        Assert.True(lifecycle.IsCancelled);
    }

    private static MaintenanceTaskModel Task(MaintenanceTaskStatus status) => new(
        Guid.NewGuid(), "MT-001", null, null, null, "Kiểm tra", null, null, status,
        null, null, Guid.NewGuid(), DateTimeOffset.UtcNow);
}
