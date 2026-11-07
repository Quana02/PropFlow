using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using PropFlow.Modules.Apartments.Application;
using PropFlow.Modules.Maintenance.Domain.MaintenanceAssignments;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Domain.Equipment;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

// Real HTTP routing, authorization, scope composition, assignment readers, services and EF queries.
// Read-only scenarios use in-memory data. Building updates exercise the PostgreSQL lock.
public sealed class Fe04StaffHttpTests
{
    private static readonly Guid Staff = Guid.NewGuid();
    private static readonly Guid OtherStaff = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private sealed class Factory(bool usePostgresForAssets = false) : WebApplicationFactory<Program>
    {
        private readonly string database = Guid.NewGuid().ToString();
        private string? assetsConnectionString;
        private string? assetsDatabase;
        public Facility Parent { get; } = new("F-PARENT", "Plant room", Now, locationDescription: "Basement");
        public Facility AssignedFacility { get; } = new("F-ASSIGNED", "Lobby", Now, locationDescription: "Ground floor");
        public Facility HiddenFacility { get; } = new("F-HIDDEN", "Private plant", Now, locationDescription: "Roof");
        public Equipment Linked { get; private set; } = null!;
        public Equipment Standalone { get; } = new("E-BUILDING", "Generator", Now, locationDescription: "Roof");
        public Equipment Hidden { get; private set; } = null!;
        public Equipment FacilityOnlyChild { get; private set; } = null!;
        public Equipment Ended { get; } = new("E-ENDED", "Former task asset", Now);
        public Guid MaintenanceAssignmentId { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                using var rsa = RSA.Create(2048);
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PropFlowDatabase"] = "Host=localhost;Database=unused;Username=unused;Password=unused",
                    ["Authentication:Issuer"] = "fe04-tests", ["Authentication:Audience"] = "fe04-tests",
                    ["Authentication:PrivateKeyPem"] = rsa.ExportRSAPrivateKeyPem(),
                    ["Authentication:OtpHashKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                    ["Authentication:Mail:Host"] = "invalid.test", ["Authentication:Mail:From"] = "test@example.invalid",
                    ["Logging:LogLevel:Default"] = "None"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "Fe04Test";
                    options.DefaultChallengeScheme = "Fe04Test";
                    options.DefaultForbidScheme = "Fe04Test";
                }).AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Fe04Test", _ => { });
                services.RemoveAll<PropertyAssetsDbContext>();
                services.RemoveAll<MaintenanceDbContext>();
                services.RemoveAll<ServiceRequestsDbContext>();
                services.AddScoped(_ => new PropertyAssetsDbContext(assetsConnectionString is { } connection
                    ? new DbContextOptionsBuilder<PropertyAssetsDbContext>().UseNpgsql(connection).Options
                    : new DbContextOptionsBuilder<PropertyAssetsDbContext>().UseInMemoryDatabase(database + "assets").Options));
                services.AddScoped(_ => new MaintenanceDbContext(new DbContextOptionsBuilder<MaintenanceDbContext>().UseInMemoryDatabase(database + "maintenance").Options));
                services.AddScoped(_ => new ServiceRequestsDbContext(new DbContextOptionsBuilder<ServiceRequestsDbContext>().UseInMemoryDatabase(database + "requests").Options));
                services.RemoveAll<IApartmentStatisticsReader>();
                services.AddSingleton<IApartmentStatisticsReader, ApartmentStatistics>();
            });
        }

        public async Task SeedAsync()
        {
            if (usePostgresForAssets)
            {
                var connection = new NpgsqlConnectionStringBuilder(TestDatabaseConfiguration.GetConnectionString()) { Database = "postgres", Pooling = false };
                var name = "propflow_fe04_http_test_" + Guid.NewGuid().ToString("N");
                await using var admin = new NpgsqlConnection(connection.ConnectionString);
                await admin.OpenAsync();
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
                await create.ExecuteNonQueryAsync();
                assetsDatabase = name;
                connection.Database = name;
                assetsConnectionString = connection.ConnectionString;
            }

            using var scope = Services.CreateScope();
            var assets = scope.ServiceProvider.GetRequiredService<PropertyAssetsDbContext>();
            if (usePostgresForAssets)
                await assets.Database.ExecuteSqlRawAsync(assets.Database.GenerateCreateScript());
            Linked = new Equipment("E-LINKED", "Pump", Now, Parent.Id, serialNumber: "SN-01", locationDescription: "Basement bay 2");
            Hidden = new Equipment("E-HIDDEN", "Other pump", Now.AddMinutes(1), Parent.Id);
            FacilityOnlyChild = new Equipment("E-FACILITY", "Lobby sensor", Now, AssignedFacility.Id);
            assets.Buildings.Add(new Building("Current condominium", "123 Main Street", Now));
            assets.Facilities.AddRange(Parent, AssignedFacility, HiddenFacility);
            assets.Equipment.AddRange(Linked, Standalone, Hidden, FacilityOnlyChild, Ended);
            await assets.SaveChangesAsync();

            var maintenance = scope.ServiceProvider.GetRequiredService<MaintenanceDbContext>();
            var task = new MaintenanceTask("TASK-1", "Inspect pump", OtherStaff, Now, equipmentId: Linked.Id);
            var assignment = new MaintenanceAssignment(task.Id, Staff, OtherStaff, Now);
            assignment.Start(Now);
            MaintenanceAssignmentId = assignment.Id;
            maintenance.MaintenanceTasks.Add(task);
            maintenance.MaintenanceAssignments.Add(assignment);
            var otherTask = new MaintenanceTask("TASK-2", "Other staff", OtherStaff, Now, facilityId: HiddenFacility.Id, equipmentId: Hidden.Id);
            maintenance.MaintenanceTasks.Add(otherTask);
            maintenance.MaintenanceAssignments.Add(new MaintenanceAssignment(otherTask.Id, OtherStaff, OtherStaff, Now));
            var endedTask = new MaintenanceTask("TASK-3", "Ended work", OtherStaff, Now, equipmentId: Ended.Id);
            var endedAssignment = new MaintenanceAssignment(endedTask.Id, Staff, OtherStaff, Now);
            endedAssignment.Complete(Now);
            maintenance.MaintenanceTasks.Add(endedTask);
            maintenance.MaintenanceAssignments.Add(endedAssignment);
            await maintenance.SaveChangesAsync();

            var requests = scope.ServiceProvider.GetRequiredService<ServiceRequestsDbContext>();
            var request = new ServiceRequest("REQ-1", Guid.NewGuid(), Guid.NewGuid(), "Inspect generator", "Inspection", Now, equipmentId: Standalone.Id);
            var facilityRequest = new ServiceRequest("REQ-2", Guid.NewGuid(), Guid.NewGuid(), "Inspect lobby", "Inspection", Now, facilityId: AssignedFacility.Id);
            requests.ServiceRequests.AddRange(request, facilityRequest);
            requests.ServiceRequestAssignments.AddRange(new ServiceRequestAssignment(request.Id, Staff, OtherStaff, Now), new ServiceRequestAssignment(facilityRequest.Id, Staff, OtherStaff, Now));
            await requests.SaveChangesAsync();
        }

        public override async ValueTask DisposeAsync()
        {
            try { await base.DisposeAsync(); }
            finally
            {
                if (assetsDatabase is { } name)
                {
                    Assert.Matches("^propflow_fe04_http_test_[0-9a-f]{32}$", name);
                    var connection = new NpgsqlConnectionStringBuilder(assetsConnectionString) { Database = "postgres" };
                    await using var admin = new NpgsqlConnection(connection.ConnectionString);
                    await admin.OpenAsync();
                    await using var drop = new NpgsqlCommand($"DROP DATABASE \"{name}\"", admin);
                    await drop.ExecuteNonQueryAsync();
                    assetsDatabase = null;
                }
            }
        }

        public HttpClient Client(string role = "STAFF", string? permission = "PERFORM_ASSIGNED_OPERATIONS", Guid? id = null)
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-User", (id ?? Staff).ToString());
            if (permission is not null) client.DefaultRequestHeaders.Add("X-Test-Permission", permission);
            return client;
        }
    }

    private sealed class ApartmentStatistics : IApartmentStatisticsReader
    {
        public Task<int> GetHighestApartmentFloorAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> GetTotalApartmentsAsync(CancellationToken cancellationToken = default) => Task.FromResult(42);
    }

    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim> { new(ClaimTypes.Role, role.ToString()), new("sub", Request.Headers["X-Test-User"].ToString()) };
            if (Request.Headers.TryGetValue("X-Test-Permission", out var permission)) claims.Add(new("permission", permission.ToString()));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }

    [Fact]
    public async Task Staff_reads_assigned_assets_locations_and_scoped_relationships()
    {
        await using var factory = new Factory();
        await factory.SeedAsync();
        using var client = factory.Client();
        var building = await Read(client, "buildings/current");
        Assert.Equal("Current condominium", building.GetProperty("name").GetString());
        Assert.False(building.TryGetProperty("totalApartments", out _));
        Assert.False(building.TryGetProperty("createdAt", out _));
        Assert.Equal(2, building.GetProperty("facilities").GetProperty("total").GetInt32());
        Assert.Equal(2, building.GetProperty("equipment").GetProperty("total").GetInt32());
        var facility = await Read(client, $"facilities/{factory.Parent.Id}");
        Assert.Equal("Basement", facility.GetProperty("locationDescription").GetString());
        Assert.Equal(1, facility.GetProperty("totalEquipment").GetInt32());
        var linked = await Read(client, $"equipments/{factory.Linked.Id}");
        Assert.Equal(factory.Parent.Id, linked.GetProperty("facilityId").GetGuid());
        Assert.Equal("Plant room", linked.GetProperty("facilityName").GetString());
        Assert.Equal("SN-01", linked.GetProperty("serialNumber").GetString());
        Assert.Equal("Basement bay 2", linked.GetProperty("locationDescription").GetString());
        var standalone = await Read(client, $"equipments/{factory.Standalone.Id}");
        Assert.Equal(JsonValueKind.Null, standalone.GetProperty("facilityId").ValueKind);
        foreach (var path in new[] { $"equipments/{factory.Hidden.Id}", $"equipments/{factory.Ended.Id}", $"equipments/{factory.FacilityOnlyChild.Id}", $"facilities/{factory.HiddenFacility.Id}" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/" + path)).StatusCode);
    }

    [Fact]
    public async Task Search_filters_counts_and_pages_intersect_assignment_scope()
    {
        await using var factory = new Factory();
        await factory.SeedAsync();
        using var client = factory.Client();
        var page = await Read(client, "equipments?pageSize=1");
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, page.GetProperty("items").GetArrayLength());
        var second = await Read(client, "equipments?pageSize=1&pageIndex=2");
        Assert.NotEqual(page.GetProperty("items")[0].GetProperty("id").GetGuid(), second.GetProperty("items")[0].GetProperty("id").GetGuid());
        await SingleId(client, "equipments?searchKeyword=pump&status=ACTIVE&equipmentType=Water", factory.Linked.Id);
        await SingleId(client, $"equipments?facilityId={factory.Parent.Id}", factory.Linked.Id);
        await SingleId(client, "equipments?buildingLevelOnly=true", factory.Standalone.Id);
        await SingleId(client, "facilities?searchKeyword=plant&facilityType=Utility&status=ACTIVE", factory.Parent.Id);
        foreach (var query in new[] { "equipments?searchKeyword=Other", $"equipments?facilityId={factory.AssignedFacility.Id}", $"equipments?facilityId={factory.HiddenFacility.Id}", $"equipments?buildingLevelOnly=true&facilityId={factory.Parent.Id}", "facilities?searchKeyword=Private" })
            Assert.Equal(0, (await Read(client, query)).GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("reassign")]
    [InlineData("complete")]
    [InlineData("cancel")]
    public async Task Revocation_and_no_assignments_do_not_fall_back_to_all_assets(string end)
    {
        await using var factory = new Factory();
        await factory.SeedAsync();
        using var client = factory.Client();
        await Read(client, $"equipments/{factory.Linked.Id}");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MaintenanceDbContext>();
            var assignment = (await db.MaintenanceAssignments.FindAsync(factory.MaintenanceAssignmentId))!;
            if (end == "reassign") assignment.Reassign(Now);
            else if (end == "complete") assignment.Complete(Now);
            else assignment.Cancel(Now);
            await db.SaveChangesAsync();
            var requests = scope.ServiceProvider.GetRequiredService<ServiceRequestsDbContext>();
            foreach (var requestAssignment in await requests.ServiceRequestAssignments.ToListAsync())
            {
                if (end == "reassign") requestAssignment.EndAsReassigned(Now);
                else if (end == "complete") requestAssignment.Complete(Now);
                else requestAssignment.Cancel(Now);
            }
            await requests.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/equipments/{factory.Linked.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/facilities/{factory.Parent.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/equipments/{factory.Standalone.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/facilities/{factory.AssignedFacility.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/buildings/current")).StatusCode);
        using var unassigned = factory.Client(id: Guid.NewGuid());
        Assert.Equal(0, (await Read(unassigned, "equipments")).GetProperty("totalCount").GetInt32());
        Assert.Equal(0, (await Read(unassigned, "facilities")).GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await unassigned.GetAsync("/api/v1/buildings/current")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_are_challenged_and_query_parameters_cannot_impersonate_another_staff_member()
    {
        await using var factory = new Factory();
        await factory.SeedAsync();
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        foreach (var path in new[] { "buildings/current", "facilities", "equipments" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/" + path)).StatusCode);
        using var client = factory.Client();
        var page = await Read(client, $"equipments?staffUserId={OtherStaff}&searchKeyword=Other");
        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsJsonAsync("/api/v1/buildings/current", new { name = "Other building" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/buildings")).StatusCode);
    }

    [Theory]
    [InlineData("STAFF", null)]
    [InlineData("STAFF", "MANAGE_OPERATIONS")]
    [InlineData("MANAGER", null)]
    [InlineData("ADMIN", "PERFORM_ASSIGNED_OPERATIONS")]
    [InlineData("RESIDENT", "PERFORM_ASSIGNED_OPERATIONS")]
    [InlineData("ACCOUNTANT", "PERFORM_ASSIGNED_OPERATIONS")]
    public async Task Read_requires_both_supported_role_and_permission(string role, string? permission)
    {
        await using var factory = new Factory();
        using var client = factory.Client(role, permission);
        foreach (var path in new[] { "buildings/current", "facilities", "equipments", $"facilities/{Guid.NewGuid()}", $"equipments/{Guid.NewGuid()}" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/" + path)).StatusCode);
    }

    [Fact]
    public async Task Staff_cannot_write_even_with_manager_permission_or_direct_http_calls()
    {
        await using var factory = new Factory();
        await factory.SeedAsync();
        foreach (var permission in new[] { "PERFORM_ASSIGNED_OPERATIONS", "MANAGE_OPERATIONS" })
        {
            using var client = factory.Client(permission: permission);
            foreach (var (method, path) in new[]
            {
                (HttpMethod.Put, "buildings/current"), (HttpMethod.Post, "facilities"),
                (HttpMethod.Put, $"facilities/{factory.Parent.Id}"), (HttpMethod.Patch, $"facilities/{factory.Parent.Id}/status"),
                (HttpMethod.Post, "equipments"), (HttpMethod.Put, $"equipments/{factory.Linked.Id}"),
                (HttpMethod.Patch, $"equipments/{factory.Linked.Id}/status")
            })
            {
                using var request = new HttpRequestMessage(method, "/api/v1/" + path) { Content = JsonContent.Create(new { name = "Tampered", status = 1 }) };
                Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
            }
            foreach (var path in new[] { "buildings/current", $"facilities/{factory.Parent.Id}", $"equipments/{factory.Linked.Id}" })
                Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync("/api/v1/" + path)).StatusCode);
        }
        using var manager = factory.Client("MANAGER", "MANAGE_OPERATIONS");
        Assert.Equal("Pump", (await Read(manager, $"equipments/{factory.Linked.Id}")).GetProperty("name").GetString());
        Assert.Equal(0, (await Read(manager, $"equipments/{factory.Linked.Id}")).GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Manager_keeps_full_read_create_update_and_status_workflows()
    {
        await using var factory = new Factory(usePostgresForAssets: true);
        await factory.SeedAsync();
        using var client = factory.Client("MANAGER", "MANAGE_OPERATIONS");
        Assert.Equal(5, (await Read(client, "equipments")).GetProperty("totalCount").GetInt32());
        Assert.Equal(3, (await Read(client, "facilities")).GetProperty("totalCount").GetInt32());
        Assert.Equal(42, (await Read(client, "buildings/current")).GetProperty("totalApartments").GetInt32());
        Assert.Equal(2, (await Read(client, $"facilities/{factory.Parent.Id}")).GetProperty("totalEquipment").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/v1/buildings/current", new { name = "Updated", address = "Address", timeZoneId = "UTC", numberOfFloors = 10 })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/facilities", new { name = "New facility", locationDescription = "Ground floor" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/v1/facilities/{factory.Parent.Id}", new { name = "Updated plant" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"/api/v1/facilities/{factory.Parent.Id}/status", new { status = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/equipments", new { name = "New equipment" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/v1/equipments/{factory.Linked.Id}", new { name = "Updated pump", facilityId = factory.Parent.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"/api/v1/equipments/{factory.Linked.Id}/status", new { status = 1 })).StatusCode);
    }

    private static async Task<JsonElement> Read(HttpClient client, string path)
    {
        var response = await client.GetAsync("/api/v1/" + path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    [Fact]
    public async Task Staff_reads_manager_updates_from_same_asset_records_without_scope_expansion()
    {
        await using var factory = new Factory(usePostgresForAssets: true);
        await factory.SeedAsync();
        using var staff = factory.Client();
        using var manager = factory.Client("MANAGER", "MANAGE_OPERATIONS");
        await Read(staff, $"equipments/{factory.Linked.Id}");
        Assert.Equal(HttpStatusCode.OK, (await manager.PutAsJsonAsync($"/api/v1/equipments/{factory.Linked.Id}",
            new { name = "Updated pump", facilityId = factory.Parent.Id, locationDescription = "New bay" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.PatchAsJsonAsync($"/api/v1/equipments/{factory.Linked.Id}/status", new { status = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.PutAsJsonAsync($"/api/v1/facilities/{factory.Parent.Id}",
            new { name = "Updated plant", locationDescription = "New floor" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.PutAsJsonAsync("/api/v1/buildings/current",
            new { name = "Updated property", address = "New address", timeZoneId = "UTC", numberOfFloors = 5 })).StatusCode);
        var equipment = await Read(staff, $"equipments/{factory.Linked.Id}");
        Assert.Equal("Updated pump", equipment.GetProperty("name").GetString());
        Assert.Equal("Updated plant", equipment.GetProperty("facilityName").GetString());
        Assert.Equal("New bay", equipment.GetProperty("locationDescription").GetString());
        Assert.Equal(1, equipment.GetProperty("status").GetInt32());
        Assert.Equal("New floor", (await Read(staff, $"facilities/{factory.Parent.Id}")).GetProperty("locationDescription").GetString());
        Assert.Equal("Updated property", (await Read(staff, "buildings/current")).GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/api/v1/equipments/{factory.Hidden.Id}")).StatusCode);
    }

    private static async Task SingleId(HttpClient client, string path, Guid expected)
    {
        var page = await Read(client, path);
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(expected, page.GetProperty("items")[0].GetProperty("id").GetGuid());
    }
}
