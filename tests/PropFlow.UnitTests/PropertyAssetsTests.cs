using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;
using PropFlow.Modules.PropertyAssets.Domain.Equipment;

namespace PropFlow.UnitTests;

public class PropertyAssetsTests
{
    private readonly DateTimeOffset _now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Building_Constructor_GeneratesNonEmptyId_AndSetsInitialProperties()
    {
        var building = new Building("BLD-01", "Tower A", "123 Main St", _now, numberOfFloors: 25, description: "Luxury tower");

        Assert.NotEqual(Guid.Empty, building.Id);
        Assert.Equal("BLD-01", building.Code);
        Assert.Equal("Tower A", building.Name);
        Assert.Equal(25, building.NumberOfFloors);
        Assert.Equal(MasterDataStatus.ACTIVE, building.Status);
        Assert.Equal(_now, building.CreatedAt);
        Assert.Equal(_now, building.UpdatedAt);
    }

    [Theory]
    [InlineData("", "Tower A", "123 Main St")]
    [InlineData("   ", "Tower A", "123 Main St")]
    [InlineData("BLD-01", "", "123 Main St")]
    [InlineData("BLD-01", "   ", "123 Main St")]
    [InlineData("BLD-01", "Tower A", "")]
    [InlineData("BLD-01", "Tower A", "   ")]
    public void Building_Constructor_ThrowsWhenRequiredFieldIsEmpty(string code, string name, string address)
    {
        Assert.Throws<ArgumentException>(() => new Building(code, name, address, _now));
    }

    [Fact]
    public void Building_ActivateAndDeactivate_UpdatesStatusAndUpdatedAt()
    {
        var building = new Building("BLD-01", "Tower A", "123 Main St", _now);
        var later = _now.AddHours(2);
        var actor = Guid.NewGuid();

        building.Deactivate(actor, later);
        Assert.Equal(MasterDataStatus.INACTIVE, building.Status);
        Assert.Equal(later, building.UpdatedAt);
        Assert.Equal(actor, building.UpdatedBy);

        var evenLater = later.AddHours(1);
        building.Activate(actor, evenLater);
        Assert.Equal(MasterDataStatus.ACTIVE, building.Status);
        Assert.Equal(evenLater, building.UpdatedAt);
    }

    [Fact]
    public void Facility_Constructor_GeneratesId_AndValidatesBuildingId()
    {
        Assert.Throws<ArgumentException>(() => new Facility(Guid.Empty, "FAC-01", "Gym", _now, facilityType: "FITNESS"));

        var buildingId = Guid.NewGuid();
        var facility = new Facility(buildingId, "FAC-01", "Gym", _now, facilityType: "FITNESS");

        Assert.NotEqual(Guid.Empty, facility.Id);
        Assert.Equal(buildingId, facility.BuildingId);
        Assert.Equal("FAC-01", facility.Code);
        Assert.Equal(MasterDataStatus.ACTIVE, facility.Status);
        Assert.Equal(_now, facility.CreatedAt);
    }

    [Fact]
    public void Equipment_StateTransitions_WorkCorrectly()
    {
        var buildingId = Guid.NewGuid();
        var equipment = new Equipment(buildingId, "EQ-01", "Elevator 1", _now, equipmentType: "ELEVATOR");

        Assert.NotEqual(Guid.Empty, equipment.Id);
        Assert.Equal(EquipmentStatus.ACTIVE, equipment.Status);

        var time1 = _now.AddDays(1);
        equipment.MarkUnderMaintenance(null, time1);
        Assert.Equal(EquipmentStatus.UNDER_MAINTENANCE, equipment.Status);
        Assert.Equal(time1, equipment.UpdatedAt);

        var time2 = time1.AddDays(1);
        equipment.MarkOutOfService(null, time2);
        Assert.Equal(EquipmentStatus.OUT_OF_SERVICE, equipment.Status);

        var time3 = time2.AddDays(1);
        equipment.MarkActive(null, time3);
        Assert.Equal(EquipmentStatus.ACTIVE, equipment.Status);

        var time4 = time3.AddDays(1);
        equipment.Deactivate(null, time4);
        Assert.Equal(EquipmentStatus.INACTIVE, equipment.Status);
    }

    [Fact]
    public async Task BuildingService_Create_Get_Update_SetStatus_WorksCorrectly()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Buildings.Services.BuildingService(dbContext);

        // 1. Create Building
        var createCommand = new PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos.CreateBuildingCommand(
            "TWR-A", "Tòa A", "123 Đường Nguyễn Huệ", "Asia/Ho_Chi_Minh", 30, "Tòa nhà căn hộ cao cấp");

        var created = await service.CreateBuildingAsync(createCommand);

        Assert.NotNull(created);
        Assert.Equal("TWR-A", created.Code);
        Assert.Equal("Tòa A", created.Name);
        Assert.Equal(MasterDataStatus.ACTIVE, created.Status);

        // 2. Duplicate Code throws exception
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateBuildingAsync(createCommand));

        // 3. Get By ID
        var fetched = await service.GetBuildingByIdAsync(created.Id);
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched!.Id);

        // 4. Get Paged List & Filter
        var paged = await service.GetBuildingsAsync(new PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos.BuildingFilterQuery("Tòa A", MasterDataStatus.ACTIVE, 1, 10));
        Assert.Single(paged.Items);
        Assert.Equal(1, paged.TotalCount);

        // 5. Update Building
        var updateCommand = new PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos.UpdateBuildingCommand(
            "Tòa A - Sài Gòn", "456 Lê Lợi", "Asia/Ho_Chi_Minh", 32, "Căn hộ cao cấp cập nhật");

        var updated = await service.UpdateBuildingAsync(created.Id, updateCommand);
        Assert.Equal("Tòa A - Sài Gòn", updated.Name);
        Assert.Equal("456 Lê Lợi", updated.Address);
        Assert.Equal(32, updated.NumberOfFloors);

        // 6. Set Status Deactivate & Activate
        var deactivated = await service.SetBuildingStatusAsync(created.Id, new PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos.SetBuildingStatusCommand(MasterDataStatus.INACTIVE));
        Assert.Equal(MasterDataStatus.INACTIVE, deactivated.Status);

        var activated = await service.SetBuildingStatusAsync(created.Id, new PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos.SetBuildingStatusCommand(MasterDataStatus.ACTIVE));
        Assert.Equal(MasterDataStatus.ACTIVE, activated.Status);

        // 7. Get Building Detail with Facility and Equipment count
        var detail = await service.GetBuildingDetailByIdAsync(created.Id);
        Assert.NotNull(detail);
        Assert.Equal(0, detail!.FacilityCount);
        Assert.Equal(0, detail.EquipmentCount);
    }
}
