using System.ComponentModel.DataAnnotations;
using PropFlow.Modules.Maintenance.Application;

namespace PropFlow.UnitTests;

public sealed class CreateMaintenanceTaskCommandValidationTests
{
    [Fact]
    public void ManualTask_RequiresAssetAndCompleteValidTimeRange()
    {
        var start = new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
        var results = Validate(new CreateMaintenanceTaskCommand(null, "Kiểm tra", Description: "Mô tả", PriorityCode: "HIGH", PlannedStartAt: start, DueAt: start));

        Assert.Contains(results, result => result.ErrorMessage == "Vui lòng chọn cơ sở vật chất hoặc thiết bị.");
        Assert.Contains(results, result => result.ErrorMessage == "Hạn hoàn thành phải sau thời điểm dự kiến.");
    }

    [Fact]
    public void ManualTask_RequiresBothPlannedStartAndDueAt()
    {
        var facilityId = Guid.NewGuid();
        var results = Validate(new CreateMaintenanceTaskCommand(null, "Kiểm tra", Description: "Mô tả", PriorityCode: "HIGH", FacilityId: facilityId));

        Assert.Contains(results, result => result.ErrorMessage == "Vui lòng chọn thời điểm dự kiến.");
        Assert.Contains(results, result => result.ErrorMessage == "Vui lòng chọn hạn hoàn thành.");
    }

    [Fact]
    public void ManualTask_AllowsBuildingLevelEquipmentWithoutFacility()
    {
        var start = new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
        var results = Validate(new CreateMaintenanceTaskCommand(null, "Kiểm tra", Description: "Mô tả", PriorityCode: "HIGH",
            PlannedStartAt: start, DueAt: start.AddHours(1), EquipmentId: Guid.NewGuid()));

        Assert.Empty(results);
    }

    [Fact]
    public void ScheduleTask_UsesScheduleContextWithoutManualFieldRequirements()
    {
        var results = Validate(new CreateMaintenanceTaskCommand(null, "Theo lịch", Guid.NewGuid(), Description: "Mô tả", PriorityCode: "HIGH"));

        Assert.Empty(results);
    }

    [Fact]
    public void EmergencyTask_CannotUseScheduleContext()
    {
        var results = Validate(new CreateMaintenanceTaskCommand(null, "Khẩn cấp", Guid.NewGuid(), Description: "Mô tả",
            PriorityCode: "HIGH", IsEmergency: true));

        Assert.Contains(results, result => result.ErrorMessage == "Công việc khẩn cấp không được tạo từ lịch bảo trì.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("URGENT")]
    public void Task_RequiresSupportedPriority(string? priorityCode)
    {
        var results = Validate(new CreateMaintenanceTaskCommand(null, "Theo lịch", Guid.NewGuid(), Description: "Mô tả", PriorityCode: priorityCode));

        Assert.Contains(results, result => result.ErrorMessage == "Vui lòng chọn mức ưu tiên.");
    }

    private static List<ValidationResult> Validate(CreateMaintenanceTaskCommand command)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(command, new ValidationContext(command), results, validateAllProperties: true);
        return results;
    }
}
