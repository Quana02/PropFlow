using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Modules.Maintenance.Domain.MaintenanceAssignments;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;
using PropFlow.Modules.Maintenance.Infrastructure;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Application;
using PropFlow.Modules.PropertyAssets.Application.Equipment.Dtos;
using PropFlow.Modules.PropertyAssets.Application.Facilities.Dtos;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Domain.Equipment;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

// Like the FE-01 fixture, create and remove only our uniquely named test database.
// Uses the current EF model; this verifies SQL execution, not migration history.
public sealed class Fe04PostgresQueryTests
{
    [Fact]
    public async Task Scoped_queries_and_assignment_readers_execute_on_postgresql()
    {
        var database = "propflow_fe04_test_" + Guid.NewGuid().ToString("N");
        var connection = new NpgsqlConnectionStringBuilder(TestDatabaseConfiguration.GetConnectionString());
        connection.Database = "postgres";
        await using var admin = new NpgsqlConnection(connection.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin))
            await create.ExecuteNonQueryAsync();
        try
        {
            connection.Database = database;
            await using var assets = new PropertyAssetsDbContext(new DbContextOptionsBuilder<PropertyAssetsDbContext>().UseNpgsql(connection.ConnectionString).Options);
            await using var maintenance = new MaintenanceDbContext(new DbContextOptionsBuilder<MaintenanceDbContext>().UseNpgsql(connection.ConnectionString).Options);
            await using var requests = new ServiceRequestsDbContext(new DbContextOptionsBuilder<ServiceRequestsDbContext>().UseNpgsql(connection.ConnectionString).Options);
            foreach (var db in new DbContext[] { assets, maintenance, requests })
            {
                Assert.Equal(database, db.Database.GetDbConnection().Database);
                await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript());
            }

            var now = DateTimeOffset.UtcNow;
            var staff = Guid.NewGuid();
            var other = Guid.NewGuid();
            var facility = new Facility("FAC", "Plant", now, "Utility", "Basement");
            var hiddenFacility = new Facility("HIDDEN", "Hidden facility", now);
            var equipment = new Equipment("PUMP", "Pump", now, facility.Id, "Water", locationDescription: "Bay 2");
            var hidden = new Equipment("HIDDEN", "Hidden pump", now, facility.Id, "Water");
            var standalone = new Equipment("GEN", "Generator", now, equipmentType: "Power");
            assets.Buildings.Add(new Building("CURRENT", "Current property", "Address", now));
            assets.Facilities.AddRange(facility, hiddenFacility);
            assets.Equipment.AddRange(equipment, hidden, standalone);
            await assets.SaveChangesAsync();
            var task = new MaintenanceTask("TASK", "Inspect", other, now, equipmentId: equipment.Id);
            var assignment = new MaintenanceAssignment(task.Id, staff, other, now);
            maintenance.MaintenanceTasks.Add(task);
            maintenance.MaintenanceAssignments.Add(assignment);
            await maintenance.SaveChangesAsync();
            var request = new ServiceRequest("REQUEST", Guid.NewGuid(), Guid.NewGuid(), "Inspect", "Generator", now, equipmentId: standalone.Id);
            requests.ServiceRequests.Add(request);
            requests.ServiceRequestAssignments.Add(new ServiceRequestAssignment(request.Id, staff, other, now));
            await requests.SaveChangesAsync();

            var maintenanceSource = new MaintenanceAssignedAssetSource(maintenance);
            var requestSource = new ServiceRequestAssignedAssetSource(requests);
            var references = (await maintenanceSource.GetActiveAssignmentsAsync(staff, default))
                .Concat(await requestSource.GetActiveAssignmentsAsync(staff, default)).ToArray();
            Assert.Equal(2, references.Length);
            Assert.Empty(await maintenanceSource.GetActiveAssignmentsAsync(other, default));
            Assert.Empty(await requestSource.GetActiveAssignmentsAsync(other, default));
            var store = new EfPropertyAssetsStore(assets, new ReadAccess(references.Select(r => r.EquipmentId!.Value).ToArray()));
            Assert.Equal(facility.Id, Assert.Single((await store.FacilitiesAsync(new FacilityFilterQuery(FacilityType: "Utility"), default)).Items).Id);
            Assert.Equal(equipment.Id, Assert.Single((await store.EquipmentAsync(new EquipmentFilterQuery(SearchKeyword: "pump", EquipmentType: "Water"), default)).Items).Id);
            Assert.Equal(standalone.Id, Assert.Single((await store.EquipmentAsync(new EquipmentFilterQuery(BuildingLevelOnly: true), default)).Items).Id);
            var detail = await store.FacilityAsync(facility.Id, false, true, default);
            Assert.NotNull(detail);
            Assert.Equal(equipment.Id, Assert.Single(detail.Equipment).Id);
            Assert.Null(await store.FacilityAsync(hiddenFacility.Id, false, true, default));
            Assert.Null(await store.EquipmentAsync(hidden.Id, false, default));
            var overview = await store.CurrentBuildingOverviewAsync(default);
            Assert.NotNull(overview);
            Assert.Equal(1, overview.Facilities.Total);
            Assert.Equal(2, overview.Equipment.Total);
            assignment.Reassign(now);
            await maintenance.SaveChangesAsync();
            Assert.Empty(await maintenanceSource.GetActiveAssignmentsAsync(staff, default));
        }
        finally
        {
            // Never target configured databases or names not generated above.
            Assert.StartsWith("propflow_fe04_test_", database);
            Assert.Equal(50, database.Length);
            NpgsqlConnection.ClearAllPools();
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class ReadAccess(Guid[] equipmentIds) : IAssetReadAccess
    {
        public Task<AssetReadScope> GetScopeAsync(CancellationToken ct) => Task.FromResult(new AssetReadScope(false, true, [], equipmentIds));
    }
}
