using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;
using PropFlow.Modules.PropertyAssets.Domain.Equipment;
using PropFlow.Modules.PropertyAssets.Application.Equipment.Dtos;
using PropFlow.Modules.PropertyAssets.Application.Facilities.Dtos;
using PropFlow.Modules.PropertyAssets.Application.Facilities.Services;
using PropFlow.Modules.PropertyAssets.Application.Equipment.Services;
using System.Text.Json;

namespace PropFlow.UnitTests;

public class PropertyAssetsTests
{
    private readonly DateTimeOffset _now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CurrentBuildingOverview_ReturnsNull_WhenBuildingIsMissing()
    {
        await using var context = CreateContext();
        var store = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(context);

        Assert.Null(await store.CurrentBuildingOverviewAsync(default));
    }

    [Fact]
    public async Task CurrentBuildingOverview_ReturnsProfileAndOperationalSummaries()
    {
        await using var context = CreateContext();
        var building = new Building("TWR-A", "Sunshore", "123 Nguyễn Huệ", _now, numberOfFloors: 28, description: "Chung cư trung tâm");
        context.Buildings.Add(building);
        context.Facilities.AddRange(
            new Facility("FAC-01", "Sảnh", _now),
            new Facility("FAC-02", "Hồ bơi", _now));
        context.Equipment.Add(new Equipment("EQ-01", "Thang máy", _now));
        await context.SaveChangesAsync();

        var overview = await new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(context)
            .CurrentBuildingOverviewAsync(default);

        Assert.NotNull(overview);
        Assert.Equal(building.Id, overview!.Id);
        Assert.Equal(28, overview.NumberOfFloors);
        Assert.Equal(_now, overview.CreatedAt);
        Assert.Equal(_now, overview.UpdatedAt);
        Assert.Equal(2, overview.Facilities.Total);
        Assert.Equal(2, overview.Facilities.Active);
        Assert.Equal(1, overview.Equipment.Total);
        Assert.Equal(1, overview.Equipment.Active);
    }

    [Fact]
    public async Task CurrentBuildingOverview_UsesLatestActiveProfile_WhenHistoricalBuildingExists()
    {
        await using var context = CreateContext();
        var historical = new Building("TWR-A", "Tower A", "Address A", _now);
        historical.Deactivate(null, _now.AddMinutes(1));
        var current = new Building("TWR-B", "Tower B", "Address B", _now.AddMinutes(2));
        context.Buildings.AddRange(historical, current);
        await context.SaveChangesAsync();
        var store = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(context);

        var overview = await store.CurrentBuildingOverviewAsync(default);

        Assert.NotNull(overview);
        Assert.Equal(current.Id, overview.Id);
    }

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
    public void Building_RejectsInvalidTimeZoneOnCreateAndUpdate()
    {
        Assert.Throws<ArgumentException>(() =>
            new Building("BLD-01", "Tower A", "123 Main St", _now, "Not/A_Time_Zone"));

        var building = new Building("BLD-01", "Tower A", "123 Main St", _now);
        Assert.Throws<ArgumentException>(() => building.Update(
            "Tower A", "123 Main St", "Not/A_Time_Zone", 10, null, null, _now.AddMinutes(1)));
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
    public void Facility_Constructor_GeneratesId_AndValidatesCode()
    {
        Assert.Throws<ArgumentException>(() => new Facility("", "Gym", _now, facilityType: "FITNESS"));

        var facility = new Facility("FAC-01", "Gym", _now, facilityType: "FITNESS");

        Assert.NotEqual(Guid.Empty, facility.Id);
        Assert.Equal("FAC-01", facility.Code);
        Assert.Equal(MasterDataStatus.ACTIVE, facility.Status);
        Assert.Equal(_now, facility.CreatedAt);
    }

    [Fact]
    public void Equipment_StateTransitions_WorkCorrectly()
    {
        var equipment = new Equipment("EQ-01", "Elevator 1", _now, equipmentType: "ELEVATOR");

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
    public async Task BuildingService_Singleton_CurrentBuildingOverview_WorksCorrectly()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Buildings.Services.BuildingService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        // 1. No Building - returns null
        var noBuilding = await service.GetCurrentBuildingOverviewAsync();
        Assert.Null(noBuilding);

        // 2. Create one Building via domain for test setup
        var now = DateTimeOffset.UtcNow;
        var building = new PropFlow.Modules.PropertyAssets.Domain.Buildings.Building(
            "TWR-A", "Tòa A", "123 Đường Nguyễn Huệ", now, "Asia/Ho_Chi_Minh", 30, "Tòa nhà căn hộ cao cấp", Guid.NewGuid());
        dbContext.Buildings.Add(building);
        await dbContext.SaveChangesAsync();

        // 3. Get Current Building Overview
        var overview = await service.GetCurrentBuildingOverviewAsync();
        Assert.NotNull(overview);
        Assert.Equal("TWR-A", overview!.Code);
        Assert.Equal("Tòa A", overview.Name);
        Assert.Equal(0, overview.Facilities.Total);
        Assert.Equal(0, overview.Equipment.Total);

        // 4. Update Current Building
        var updateCommand = new PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos.UpdateCurrentBuildingCommand(
            "Tòa A - Sài Gòn", "456 Lê Lợi", "Asia/Ho_Chi_Minh", 32, "Căn hộ cao cấp cập nhật", Guid.NewGuid());

        var updated = await service.UpdateCurrentBuildingAsync(updateCommand);
        Assert.Equal("Tòa A - Sài Gòn", updated.Name);
        Assert.Equal("456 Lê Lợi", updated.Address);
        Assert.Equal(32, updated.NumberOfFloors);

        // 5. Verify update persisted
        var reloaded = await service.GetCurrentBuildingOverviewAsync();
        Assert.Equal("Tòa A - Sài Gòn", reloaded!.Name);
    }


    [Fact]
    public async Task EquipmentService_CreateEquipment_ThrowsWhenFacilityDoesNotExist()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Equipment.Services.EquipmentService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        var nonExistentFacilityId = Guid.NewGuid();
        var command = new CreateEquipmentCommand(
            "EQ-01",
            "Test Equipment",
            FacilityId: nonExistentFacilityId,
            EquipmentType: "TEST");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateEquipmentAsync(command));
        Assert.Contains("không tồn tại", exception.Message);
    }

    [Fact]
    public async Task EquipmentService_CreateEquipment_WithExistingFacility_Succeeds()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Equipment.Services.EquipmentService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        // Create a facility
        var facility = new Facility("FAC-01", "Gym", _now);
        dbContext.Facilities.Add(facility);
        await dbContext.SaveChangesAsync();

        // Equipment with a valid Facility in the current deployment should succeed.
        var command = new CreateEquipmentCommand(
            "EQ-01",
            "Test Equipment",
            FacilityId: facility.Id,
            EquipmentType: "TEST");

        var result = await service.CreateEquipmentAsync(command);
        Assert.NotNull(result);
        Assert.Equal("EQ-01", result.Code);
    }

    [Fact]
    public async Task EquipmentService_CreateEquipment_DuplicateCodeThrowsException()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Equipment.Services.EquipmentService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        // Create first equipment
        var command1 = new CreateEquipmentCommand(
            "EQ-01",
            "First Equipment",
            EquipmentType: "TEST");
        await service.CreateEquipmentAsync(command1);

        // Try to create second equipment with same code
        var command2 = new CreateEquipmentCommand(
            "EQ-01",
            "Second Equipment",
            EquipmentType: "TEST");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateEquipmentAsync(command2));
        Assert.Contains("đã tồn tại", exception.Message);
    }

    [Fact]
    public async Task EquipmentService_UpdateEquipment_AllowsChangingFacility()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Equipment.Services.EquipmentService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        // Create two facilities
        var facility1 = new Facility("FAC-01", "Gym", _now);
        var facility2 = new Facility("FAC-02", "Pool", _now);
        dbContext.Facilities.AddRange(facility1, facility2);
        await dbContext.SaveChangesAsync();

        // Create equipment linked to facility1
        var command = new CreateEquipmentCommand(
            "EQ-01",
            "Test Equipment",
            FacilityId: facility1.Id,
            EquipmentType: "TEST");
        var equipment = await service.CreateEquipmentAsync(command);

        // Update equipment to link to facility2 should succeed
        var updateCommand = new UpdateEquipmentCommand(
            "Updated Equipment",
            FacilityId: facility2.Id,
            EquipmentType: "TEST");

        var updated = await service.UpdateEquipmentAsync(equipment.Id, updateCommand);
        Assert.Equal(facility2.Id, updated.FacilityId);
    }

    [Fact]
    public async Task FacilityService_UpdateFacility_AllowsUpdatingProperties()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Facilities.Services.FacilityService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        // Create a facility
        var facility = new Facility("FAC-01", "Gym", _now);
        dbContext.Facilities.Add(facility);
        await dbContext.SaveChangesAsync();

        // Update facility properties should succeed
        var updateCommand = new UpdateFacilityCommand(
            "Gym Updated",
            FacilityType: "FITNESS",
            LocationDescription: "Updated location",
            Description: "Updated description");

        var updated = await service.UpdateFacilityAsync(facility.Id, updateCommand);
        Assert.Equal("Gym Updated", updated.Name);
        // FE-04.3: Edit should NOT change status
        Assert.Equal(MasterDataStatus.ACTIVE, updated.Status);
    }

    [Fact]
    public async Task EquipmentService_CreateEquipment_WithFacilityId_Succeeds()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Equipment.Services.EquipmentService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        // Create a facility
        var facility = new Facility("FAC-01", "Gym", _now);
        dbContext.Facilities.Add(facility);
        await dbContext.SaveChangesAsync();

        // Create equipment linked to the facility
        var command = new CreateEquipmentCommand(
            "EQ-01",
            "Treadmill",
            FacilityId: facility.Id,
            EquipmentType: "FITNESS");

        var result = await service.CreateEquipmentAsync(command);
        Assert.NotNull(result);
        Assert.Equal("EQ-01", result.Code);
        Assert.Equal(facility.Id, result.FacilityId);
    }

    [Fact]
    public async Task EquipmentService_CreateEquipment_WithNullFacilityId_Succeeds()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Equipment.Services.EquipmentService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        // Create equipment without facility (building-level equipment)
        var command = new CreateEquipmentCommand(
            "EQ-01",
            "Main Transformer",
            FacilityId: null,
            EquipmentType: "ELECTRICAL");

        var result = await service.CreateEquipmentAsync(command);
        Assert.NotNull(result);
        Assert.Equal("EQ-01", result.Code);
        Assert.Null(result.FacilityId);
    }

    [Fact]
    public async Task EquipmentService_CreateEquipment_WithInvalidFacilityId_Throws()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Equipment.Services.EquipmentService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        // Try to create equipment with non-existent facility
        var command = new CreateEquipmentCommand(
            "EQ-01",
            "Treadmill",
            FacilityId: Guid.NewGuid(),
            EquipmentType: "FITNESS");

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateEquipmentAsync(command));
    }

    [Fact]
    public async Task EquipmentService_UpdateEquipment_PreservesStatus()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Equipment.Services.EquipmentService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        // Create equipment
        var equipment = new Equipment("EQ-01", "Treadmill", _now);
        dbContext.Equipment.Add(equipment);
        await dbContext.SaveChangesAsync();

        var initialStatus = equipment.Status;

        // Update equipment properties (status not included in command)
        var updateCommand = new UpdateEquipmentCommand(
            "Treadmill Updated",
            FacilityId: null,
            EquipmentType: "FITNESS");

        var updated = await service.UpdateEquipmentAsync(equipment.Id, updateCommand);
        Assert.Equal("Treadmill Updated", updated.Name);
        // FE-04.5: Edit should NOT change status
        Assert.Equal(initialStatus, updated.Status);
    }

    [Fact]
    public async Task EquipmentService_SetStatus_ChangesStatusAndUpdatesAuditFields()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Equipment.Services.EquipmentService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        // Create equipment with ACTIVE status
        var equipment = new Equipment("EQ-01", "Treadmill", _now);
        dbContext.Equipment.Add(equipment);
        await dbContext.SaveChangesAsync();

        var initialUpdatedAt = equipment.UpdatedAt;

        // Change status to UNDER_MAINTENANCE
        var statusCommand = new SetEquipmentStatusCommand(EquipmentStatus.UNDER_MAINTENANCE, Guid.NewGuid());
        var updated = await service.SetEquipmentStatusAsync(equipment.Id, statusCommand);

        Assert.Equal(EquipmentStatus.UNDER_MAINTENANCE, updated.Status);
        Assert.Equal(statusCommand.UpdatedBy, updated.UpdatedBy);
        Assert.True(updated.UpdatedAt > initialUpdatedAt);
    }

    [Fact]
    public async Task EquipmentService_SetStatus_SameStatus_IsIdempotent()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Equipment.Services.EquipmentService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        // Create equipment with ACTIVE status
        var equipment = new Equipment("EQ-01", "Treadmill", _now);
        dbContext.Equipment.Add(equipment);
        await dbContext.SaveChangesAsync();

        var initialUpdatedAt = equipment.UpdatedAt;
        var initialUpdatedBy = equipment.UpdatedBy;

        // Set status to same value (ACTIVE)
        var statusCommand = new SetEquipmentStatusCommand(EquipmentStatus.ACTIVE, Guid.NewGuid());
        var updated = await service.SetEquipmentStatusAsync(equipment.Id, statusCommand);

        // Verify idempotent behavior - status should remain ACTIVE
        Assert.Equal(EquipmentStatus.ACTIVE, updated.Status);
        // Verify audit fields are updated (domain methods always update audit fields)
        Assert.Equal(statusCommand.UpdatedBy, updated.UpdatedBy);
        Assert.True(updated.UpdatedAt >= initialUpdatedAt);
    }

    [Fact]
    public async Task EquipmentService_SetStatus_EquipmentNotFound_Throws()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Equipment.Services.EquipmentService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        // Try to set status on non-existent equipment
        var nonexistentId = Guid.NewGuid();
        var statusCommand = new SetEquipmentStatusCommand(EquipmentStatus.ACTIVE, Guid.NewGuid());

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SetEquipmentStatusAsync(nonexistentId, statusCommand));
    }

    [Fact]
    public async Task FacilityService_GetFacilityById_ReturnsDetailForValidId()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Facilities.Services.FacilityService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        // Create a facility
        var facility = new Facility("FAC-01", "Gym", _now);
        dbContext.Facilities.Add(facility);
        await dbContext.SaveChangesAsync();

        // Get facility by ID
        var result = await service.GetFacilityByIdAsync(facility.Id);

        Assert.NotNull(result);
        Assert.Equal("FAC-01", result.Code);
        Assert.Equal("Gym", result.Name);
    }

    [Fact]
    public async Task FacilityService_GetFacilityById_ReturnsNullForNonexistentId()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Facilities.Services.FacilityService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        // Try to get non-existent facility
        var result = await service.GetFacilityByIdAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task EquipmentService_GetEquipmentDetailById_ReturnsDetailForValidId()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Equipment.Services.EquipmentService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        // Create equipment
        var equipment = new Equipment("EQ-01", "Treadmill", _now);
        dbContext.Equipment.Add(equipment);
        await dbContext.SaveChangesAsync();

        // Get equipment detail by ID
        var result = await service.GetEquipmentDetailByIdAsync(equipment.Id);

        Assert.NotNull(result);
        Assert.Equal("EQ-01", result.Code);
        Assert.Equal("Treadmill", result.Name);
    }

    [Fact]
    public async Task EquipmentService_GetEquipmentDetailById_ReturnsNullForNonexistentId()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Equipment.Services.EquipmentService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        // Try to get non-existent equipment
        var result = await service.GetEquipmentDetailByIdAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task EquipmentService_GetEquipmentDetailById_WithFacilityId_ResolvesFacilityName()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Equipment.Services.EquipmentService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        // Create a facility
        var facility = new Facility("FAC-01", "Gym", _now);
        dbContext.Facilities.Add(facility);
        await dbContext.SaveChangesAsync();

        // Create equipment linked to the facility
        var equipment = new Equipment("EQ-01", "Treadmill", _now, facilityId: facility.Id);
        dbContext.Equipment.Add(equipment);
        await dbContext.SaveChangesAsync();

        // Get equipment detail
        var result = await service.GetEquipmentDetailByIdAsync(equipment.Id);

        Assert.NotNull(result);
        Assert.Equal(facility.Id, result.FacilityId);
        Assert.Equal("Gym", result.FacilityName);
    }

    [Fact]
    public async Task EquipmentService_GetEquipmentDetailById_WithNullFacilityId_RemainsValid()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Equipment.Services.EquipmentService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        // Create equipment without facility (building-level equipment)
        var equipment = new Equipment("EQ-01", "Main Transformer", _now);
        dbContext.Equipment.Add(equipment);
        await dbContext.SaveChangesAsync();

        // Get equipment detail
        var result = await service.GetEquipmentDetailByIdAsync(equipment.Id);

        Assert.NotNull(result);
        Assert.Null(result.FacilityId);
        Assert.Null(result.FacilityName);
    }

    [Fact]
    public async Task FacilityService_GetFacilities_WithSearchKeyword_FiltersCorrectly()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Facilities.Services.FacilityService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        // Create facilities
        var facility1 = new Facility("FAC-01", "Gym", _now, facilityType: "Phòng GYM / Fitness");
        var facility2 = new Facility("FAC-02", "Pool", _now, facilityType: "Hồ Bơi");
        dbContext.Facilities.AddRange(facility1, facility2);
        await dbContext.SaveChangesAsync();

        // Search by keyword
        var query = new FacilityFilterQuery(SearchKeyword: "gym", PageIndex: 1, PageSize: 10);
        var result = await service.GetFacilitiesAsync(query);

        Assert.Single(result.Items);
        Assert.Equal("FAC-01", result.Items[0].Code);
    }

    [Fact]
    public async Task FacilityService_GetFacilities_WithFacilityTypeFilter_FiltersCorrectly()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Facilities.Services.FacilityService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        // Create facilities
        var facility1 = new Facility("FAC-01", "Gym", _now, facilityType: "Phòng GYM / Fitness");
        var facility2 = new Facility("FAC-02", "Pool", _now, facilityType: "Hồ Bơi");
        dbContext.Facilities.AddRange(facility1, facility2);
        await dbContext.SaveChangesAsync();

        // Filter by FacilityType
        var query = new FacilityFilterQuery(FacilityType: "Phòng GYM / Fitness", PageIndex: 1, PageSize: 10);
        var result = await service.GetFacilitiesAsync(query);

        Assert.Single(result.Items);
        Assert.Equal("FAC-01", result.Items[0].Code);
    }

    [Fact]
    public async Task EquipmentService_GetEquipments_WithEquipmentTypeFilter_FiltersCorrectly()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Equipment.Services.EquipmentService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        // Create equipment
        var equipment1 = new Equipment("EQ-01", "Pump", _now, equipmentType: "Hệ Thống Cơ Điện (MEP)");
        var equipment2 = new Equipment("EQ-02", "Light", _now, equipmentType: "Hệ Thống Điện & Chiếu Sáng");
        dbContext.Equipment.AddRange(equipment1, equipment2);
        await dbContext.SaveChangesAsync();

        // Filter by EquipmentType
        var query = new EquipmentFilterQuery(EquipmentType: "Hệ Thống Cơ Điện (MEP)", PageIndex: 1, PageSize: 10);
        var result = await service.GetEquipmentsAsync(query);

        Assert.Single(result.Items);
        Assert.Equal("EQ-01", result.Items[0].Code);
    }

    [Fact]
    public async Task FacilityService_GetFacilities_WithCombinedFilters_ReturnsOnlyMatchingRecords()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Facilities.Services.FacilityService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        // Create facilities with different combinations
        var facility1 = new Facility("FAC-01", "Gym Pool", _now, facilityType: "Hồ Bơi", initialStatus: MasterDataStatus.ACTIVE);
        var facility2 = new Facility("FAC-02", "Gym", _now, facilityType: "Phòng GYM / Fitness", initialStatus: MasterDataStatus.ACTIVE);
        var facility3 = new Facility("FAC-03", "Pool", _now, facilityType: "Hồ Bơi", initialStatus: MasterDataStatus.INACTIVE);
        var facility4 = new Facility("FAC-04", "Tennis", _now, facilityType: "Sân Tennis / Cầu Lông", initialStatus: MasterDataStatus.ACTIVE);
        dbContext.Facilities.AddRange(facility1, facility2, facility3, facility4);
        await dbContext.SaveChangesAsync();

        // Search with combined criteria: keyword "Pool", Status ACTIVE, FacilityType "Hồ Bơi"
        var query = new FacilityFilterQuery(SearchKeyword: "Pool", Status: MasterDataStatus.ACTIVE, FacilityType: "Hồ Bơi", PageIndex: 1, PageSize: 10);
        var result = await service.GetFacilitiesAsync(query);

        // Only facility1 should match ALL criteria
        Assert.Single(result.Items);
        Assert.Equal("FAC-01", result.Items[0].Code);
        Assert.Equal("Gym Pool", result.Items[0].Name);
    }

    [Fact]
    public async Task FacilityService_GetFacilities_FilterAppliesBeforePagination_AndTotalCountIsFiltered()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Facilities.Services.FacilityService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        // Create 5 facilities, 3 with same FacilityType
        for (int i = 1; i <= 5; i++)
        {
            var facility = new Facility($"FAC-{i:00}", $"Facility {i}", _now, facilityType: i <= 3 ? "Phòng GYM / Fitness" : "Hồ Bơi");
            dbContext.Facilities.Add(facility);
        }
        await dbContext.SaveChangesAsync();

        // Request page 2 with page size 2, filtered by FacilityType
        var query = new FacilityFilterQuery(FacilityType: "Phòng GYM / Fitness", PageIndex: 2, PageSize: 2);
        var result = await service.GetFacilitiesAsync(query);

        // TotalCount should be 3 (all matching records, not total in database)
        Assert.Equal(3, result.TotalCount);
        // Items should be 1 (remaining records on page 2)
        Assert.Single(result.Items);
        Assert.Equal("FAC-03", result.Items[0].Code);
    }

    [Fact]
    public async Task EquipmentService_GetEquipments_WithCombinedFilters_ReturnsOnlyMatchingRecords()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Equipment.Services.EquipmentService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        // Create a facility
        var facility = new Facility("FAC-01", "Gym", _now, facilityType: "Phòng GYM / Fitness");
        dbContext.Facilities.Add(facility);
        await dbContext.SaveChangesAsync();

        // Create equipment with different combinations
        var equipment1 = new Equipment("EQ-01", "Pump A", _now, facilityId: facility.Id, equipmentType: "Hệ Thống Cơ Điện (MEP)");
        var equipment2 = new Equipment("EQ-02", "Pump B", _now, facilityId: facility.Id, equipmentType: "Hệ Thống Cơ Điện (MEP)");
        equipment2.Deactivate(Guid.NewGuid(), _now);
        var equipment3 = new Equipment("EQ-03", "Light", _now, facilityId: facility.Id, equipmentType: "Hệ Thống Điện & Chiếu Sáng");
        var equipment4 = new Equipment("EQ-04", "Pump C", _now, equipmentType: "Hệ Thống Cơ Điện (MEP)"); // No facility
        dbContext.Equipment.AddRange(equipment1, equipment2, equipment3, equipment4);
        await dbContext.SaveChangesAsync();

        // Search with combined criteria: keyword "Pump", Status ACTIVE, EquipmentType "Hệ Thống Cơ Điện (MEP)", FacilityId
        var query = new EquipmentFilterQuery(SearchKeyword: "Pump", Status: EquipmentStatus.ACTIVE, EquipmentType: "Hệ Thống Cơ Điện (MEP)", FacilityId: facility.Id, PageIndex: 1, PageSize: 10);
        var result = await service.GetEquipmentsAsync(query);

        // Only equipment1 should match ALL criteria
        Assert.Single(result.Items);
        Assert.Equal("EQ-01", result.Items[0].Code);
        Assert.Equal("Pump A", result.Items[0].Name);
    }

    [Fact]
    public async Task EquipmentService_GetEquipments_FilterAppliesBeforePagination_AndTotalCountIsFiltered()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Equipment.Services.EquipmentService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        // Create 5 equipment, 3 with same EquipmentType
        for (int i = 1; i <= 5; i++)
        {
            var equipment = new Equipment($"EQ-{i:00}", $"Equipment {i}", _now, equipmentType: i <= 3 ? "Hệ Thống Cơ Điện (MEP)" : "Hệ Thống Điện & Chiếu Sáng");
            dbContext.Equipment.Add(equipment);
        }
        await dbContext.SaveChangesAsync();

        // Request page 2 with page size 2, filtered by EquipmentType
        var query = new EquipmentFilterQuery(EquipmentType: "Hệ Thống Cơ Điện (MEP)", PageIndex: 2, PageSize: 2);
        var result = await service.GetEquipmentsAsync(query);

        // TotalCount should be 3 (all matching records, not total in database)
        Assert.Equal(3, result.TotalCount);
        // Items should be 1 (remaining records on page 2)
        Assert.Single(result.Items);
        Assert.Equal("EQ-03", result.Items[0].Code);
    }

    [Fact]
    public async Task FacilityService_SetStatus_ChangesStatusAndUpdatesAuditFields()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Facilities.Services.FacilityService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        // Create a facility with ACTIVE status
        var facility = new Facility("FAC-01", "Gym", _now);
        dbContext.Facilities.Add(facility);
        await dbContext.SaveChangesAsync();

        var initialUpdatedAt = facility.UpdatedAt;

        // Change status to UNDER_MAINTENANCE
        var statusCommand = new SetFacilityStatusCommand(MasterDataStatus.UNDER_MAINTENANCE, Guid.NewGuid());
        var updated = await service.SetFacilityStatusAsync(facility.Id, statusCommand);

        Assert.Equal(MasterDataStatus.UNDER_MAINTENANCE, updated.Status);
        Assert.Equal(statusCommand.UpdatedBy, updated.UpdatedBy);
        Assert.True(updated.UpdatedAt > initialUpdatedAt);
    }

    [Fact]
    public async Task EquipmentService_CreateEquipment_ValidFacilitySucceeds()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Equipment.Services.EquipmentService(new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        // Create a facility
        var facility = new Facility("FAC-01", "Gym", _now);
        dbContext.Facilities.Add(facility);
        await dbContext.SaveChangesAsync();

        // Create equipment linked to the facility - should succeed
        var command = new CreateEquipmentCommand(
            "EQ-01",
            "Treadmill",
            FacilityId: facility.Id,
            EquipmentType: "FITNESS");

        var result = await service.CreateEquipmentAsync(command);
        Assert.NotNull(result);
        Assert.Equal("EQ-01", result.Code);
        Assert.Equal(facility.Id, result.FacilityId);
    }

    [Fact]
    public async Task FacilityService_GetFacilities_NormalizesInvalidPagination()
    {
        await using var dbContext = CreateContext();
        dbContext.Facilities.Add(new Facility("FAC-01", "Gym", _now));
        await dbContext.SaveChangesAsync();
        var service = new PropFlow.Modules.PropertyAssets.Application.Facilities.Services.FacilityService(
            new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        var result = await service.GetFacilitiesAsync(new FacilityFilterQuery(PageIndex: 0, PageSize: 0));

        Assert.Equal(1, result.PageIndex);
        Assert.Equal(1, result.PageSize);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task BuildingService_UpdateCurrent_CreatesInitialProfile_WhenBuildingIsMissing()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Buildings.Services.BuildingService(
            new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));
        var actor = Guid.NewGuid();
        var command = new PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos.UpdateCurrentBuildingCommand(
            "Sunshore", "123 Nguyễn Huệ", "Asia/Ho_Chi_Minh", 28, "Chung cư trung tâm", actor, "TWR-A");

        var created = await service.UpdateCurrentBuildingAsync(command);

        Assert.Equal("TWR-A", created.Code);
        Assert.Equal("Sunshore", created.Name);
        Assert.Equal(actor, created.CreatedBy);
        Assert.Single(dbContext.Buildings);
    }

    [Fact]
    public async Task BuildingService_UpdateCurrent_RequiresCode_WhenCreatingInitialProfile()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
        var service = new PropFlow.Modules.PropertyAssets.Application.Buildings.Services.BuildingService(
            new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));
        var command = new PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos.UpdateCurrentBuildingCommand(
            "Sunshore", "123 Nguyễn Huệ", "Asia/Ho_Chi_Minh", 28);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateCurrentBuildingAsync(command));

        Assert.Contains("Mã chung cư là bắt buộc", exception.Message);
        Assert.Empty(dbContext.Buildings);
    }

    [Fact]
    public void UpdateCurrentBuildingCommand_Validation_IsDeclaredOnRecordConstructorParameters()
    {
        var constructor = typeof(PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos.UpdateCurrentBuildingCommand)
            .GetConstructors()
            .Single();
        var parameters = constructor.GetParameters().ToDictionary(parameter => parameter.Name!);

        Assert.Contains(parameters["Name"].GetCustomAttributes(false), attribute => attribute is System.ComponentModel.DataAnnotations.RequiredAttribute);
        Assert.Contains(parameters["Address"].GetCustomAttributes(false), attribute => attribute is System.ComponentModel.DataAnnotations.RequiredAttribute);
        Assert.Contains(parameters["TimeZoneId"].GetCustomAttributes(false), attribute => attribute is System.ComponentModel.DataAnnotations.StringLengthAttribute { MaximumLength: 64 });
        Assert.Contains(parameters["NumberOfFloors"].GetCustomAttributes(false), attribute => attribute is System.ComponentModel.DataAnnotations.RangeAttribute);
        Assert.Contains(parameters["Code"].GetCustomAttributes(false), attribute => attribute is System.ComponentModel.DataAnnotations.StringLengthAttribute { MaximumLength: 50 });
    }

    [Fact]
    public void Fe04WriteCommands_Validation_IsDeclaredOnRecordConstructorParameters()
    {
        AssertRecordValidationTargetsConstructor(typeof(CreateFacilityCommand));
        AssertRecordValidationTargetsConstructor(typeof(UpdateFacilityCommand));
        AssertRecordValidationTargetsConstructor(typeof(CreateEquipmentCommand));
        AssertRecordValidationTargetsConstructor(typeof(UpdateEquipmentCommand));
    }

    private static void AssertRecordValidationTargetsConstructor(Type commandType)
    {
        var constructor = commandType.GetConstructors().Single();
        Assert.Contains(constructor.GetParameters().SelectMany(parameter => parameter.GetCustomAttributes(false)),
            attribute => attribute is System.ComponentModel.DataAnnotations.ValidationAttribute);
        Assert.DoesNotContain(commandType.GetProperties().SelectMany(property => property.GetCustomAttributes(false)),
            attribute => attribute is System.ComponentModel.DataAnnotations.ValidationAttribute);
    }

    [Fact]
    public async Task EquipmentService_GetEquipments_LimitsPageSizeToOneHundred()
    {
        await using var dbContext = CreateContext();
        var service = new PropFlow.Modules.PropertyAssets.Application.Equipment.Services.EquipmentService(
            new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore(dbContext));

        var result = await service.GetEquipmentsAsync(new EquipmentFilterQuery(PageIndex: 1, PageSize: 1000));

        Assert.Equal(100, result.PageSize);
    }

    [Fact]
    public void Equipment_CannotMoveDirectlyFromOutOfServiceToMaintenance_ReturnsVietnameseMessage()
    {
        var equipment = new Equipment("EQ-01", "Pump", _now);
        equipment.MarkOutOfService(Guid.NewGuid(), _now.AddMinutes(1));

        var error = Assert.Throws<InvalidOperationException>(() =>
            equipment.MarkUnderMaintenance(Guid.NewGuid(), _now.AddMinutes(2)));

        Assert.Equal("Thiết bị đã ngừng phục vụ không thể chuyển thẳng sang trạng thái đang bảo trì.", error.Message);
    }

    [Fact]
    public void FacilityCommands_DoNotAcceptAuditActorFromJson()
    {
        var spoofedActor = Guid.NewGuid();
        var json = $$"""{"code":"FAC-01","name":"Gym","createdBy":"{{spoofedActor}}"}""";

        var command = JsonSerializer.Deserialize<CreateFacilityCommand>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(command);
        Assert.Null(command.CreatedBy);
    }

    private static PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.PropertyAssetsDbContext(options);
    }
}
