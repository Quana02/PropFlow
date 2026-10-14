using PropFlow.Web.Client.Features.Maintenance.Models;

namespace PropFlow.UnitTests;

public sealed class MaintenanceProgressLifecycleMapperTests
{
    [Theory]
    [InlineData(MaintenanceTaskStatus.OPEN, 1, 0)]
    [InlineData(MaintenanceTaskStatus.ASSIGNED, 2, 1)]
    [InlineData(MaintenanceTaskStatus.IN_PROGRESS, 3, 2)]
    [InlineData(MaintenanceTaskStatus.COMPLETED, 4, 3)]
    [InlineData(MaintenanceTaskStatus.CLOSED, 0, 5)]
    public void Map_UsesOnlyConfirmedTaskLifecycle(MaintenanceTaskStatus status, int expectedCurrentStep, int expectedCompletedThroughStep)
    {
        var lifecycle = MaintenanceProgressLifecycleMapper.Map(Task(status));

        Assert.Equal(expectedCurrentStep, lifecycle.CurrentStep);
        Assert.Equal(expectedCompletedThroughStep, lifecycle.CompletedThroughStep);
        Assert.False(lifecycle.IsCancelled);
    }

    [Fact]
    public void Map_UsesInProgressStatusForTheActiveWorkStep()
    {
        var lifecycle = MaintenanceProgressLifecycleMapper.Map(Task(MaintenanceTaskStatus.IN_PROGRESS) with
        {
            LastProgressActivityType = MaintenanceActivityType.WORK_LOG_ADDED
        });

        Assert.Equal(3, lifecycle.CurrentStep);
        Assert.Equal(2, lifecycle.CompletedThroughStep);
    }

    [Fact]
    public void Map_ShowsCancelledSeparatelyWithoutCompletingLifecycle()
    {
        var lifecycle = MaintenanceProgressLifecycleMapper.Map(Task(MaintenanceTaskStatus.CANCELLED));

        Assert.Equal(0, lifecycle.CurrentStep);
        Assert.Equal(0, lifecycle.CompletedThroughStep);
        Assert.True(lifecycle.IsCancelled);
    }

    private static MaintenanceTaskModel Task(MaintenanceTaskStatus status) => new(
        Guid.NewGuid(), "MT-001", null, null, null, "Kiểm tra", null, null, status,
        null, null, Guid.NewGuid(), DateTimeOffset.UtcNow);
}
