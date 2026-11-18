using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using PropFlow.Api.Composition;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Apartments.Application;
using PropFlow.Modules.Maintenance.Application;
using PropFlow.Modules.Maintenance.Domain.MaintenanceAssignments;
using PropFlow.Modules.Maintenance.Domain.MaintenanceResults;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTaskActivities;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Domain.Equipment;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Shared;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

// Real HTTP routing, authorization, scope composition, assignment readers, services and EF queries.
// Read-only scenarios use in-memory data. Building updates exercise the PostgreSQL lock.
public sealed class Fe04StaffHttpTests
{
    private static readonly Guid Staff = Guid.NewGuid();
    private static readonly Guid OtherStaff = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Theory]
    [InlineData("start")]
    [InlineData("progress")]
    [InlineData("result")]
    [Trait("Feature", "FE-07")]
    public async Task Maintenance_mutation_accepts_current_staff_and_denies_former_staff_without_writes(string action)
    {
        await using var factory = new Factory();
        await factory.SeedAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MaintenanceDbContext>();
        var storage = (SyntheticMaintenanceEvidence)scope.ServiceProvider.GetRequiredService<IMaintenanceEvidenceStorage>();
        var task = new MaintenanceTask("TASK-MUTATION-CONTROL", "Kiểm tra máy bơm", OtherStaff, Now.AddMinutes(-5));
        var former = new MaintenanceAssignment(task.Id, OtherStaff, Staff, Now.AddMinutes(-5));
        former.Reassign(Now.AddMinutes(-4));
        var current = new MaintenanceAssignment(task.Id, Staff, OtherStaff, Now.AddMinutes(-4));
        task.MarkAssigned(Now.AddMinutes(-4));
        if (action != "start")
        {
            task.Start(Now.AddMinutes(-3));
            current.Start(Now.AddMinutes(-3));
        }
        db.AddRange(task, former, current);
        await db.SaveChangesAsync();

        async Task<(MaintenanceTaskStatus Status, int Activities, int Results, int Images, int Writes, int Outbox)> Snapshot()
        {
            await using var snapshotScope = factory.Services.CreateAsyncScope();
            var snapshot = snapshotScope.ServiceProvider.GetRequiredService<MaintenanceDbContext>();
            var results = snapshot.MaintenanceResults.Where(result => result.MaintenanceTaskId == task.Id);
            return (await snapshot.MaintenanceTasks.Where(item => item.Id == task.Id).Select(item => item.Status).SingleAsync(),
                await snapshot.MaintenanceTaskActivities.CountAsync(item => item.MaintenanceTaskId == task.Id),
                await results.CountAsync(),
                await snapshot.MaintenanceResultAttachments.CountAsync(image => results.Any(result => result.Id == image.MaintenanceResultId)),
                storage.StoreCount,
                await snapshot.ResidentEvents.CountAsync());
        }

        HttpContent? Payload()
        {
            if (action == "start") return null;
            if (action == "progress") return JsonContent.Create(new UpdateMaintenanceProgressCommand("Đã kiểm tra áp lực"));
            return CreateResultEvidence();
        }

        var before = await Snapshot();
        var path = $"/api/v1/maintenance/my-tasks/{task.Id}/{action}";
        using var deniedClient = factory.Client(id: OtherStaff);
        using var deniedPayload = Payload();
        using var denied = await deniedClient.PostAsync(path, deniedPayload);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(before, await Snapshot());

        using var allowedClient = factory.Client(id: Staff);
        using var allowedPayload = Payload();
        using var allowed = await allowedClient.PostAsync(path, allowedPayload);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var after = await Snapshot();
        Assert.Equal(before.Activities + 1, after.Activities);
        Assert.Equal(action == "result" ? 1 : 0, after.Results);
        Assert.Equal(action == "result" ? 1 : 0, after.Images);
        Assert.Equal(after.Images, after.Writes);
    }

    [Fact]
    [Trait("Feature", "FE-07")]
    public async Task Reassignment_queued_before_result_upload_revokes_the_previous_assignee_before_storage()
    {
        await using var factory = new Factory(usePostgresForMaintenance: true);
        await factory.SeedAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MaintenanceDbContext>();
        var storage = (SyntheticMaintenanceEvidence)scope.ServiceProvider.GetRequiredService<IMaintenanceEvidenceStorage>();
        var task = new MaintenanceTask("TASK-REASSIGN-RACE", "Kiểm tra máy bơm", Guid.NewGuid(), Now.AddMinutes(-2));
        var previous = new MaintenanceAssignment(task.Id, Staff, task.CreatedBy, Now.AddMinutes(-1));
        task.MarkAssigned(Now.AddMinutes(-1));
        task.Start(Now);
        previous.Start(Now);
        db.AddRange(task, previous);
        await db.SaveChangesAsync();

        using var manager = factory.Client("MANAGER", "MANAGE_OPERATIONS", task.CreatedBy);
        using var former = factory.Client();
        using var current = factory.Client(id: OtherStaff);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var reassignPath = $"/api/v1/maintenance/tasks/{task.Id}/assignment";
        var resultPath = $"/api/v1/maintenance/my-tasks/{task.Id}/result";
        var assignment = new AssignMaintenanceTaskCommand(OtherStaff);
        var outboxBefore = await db.ResidentEvents.CountAsync(timeout.Token);
        using var deniedAssignment = await former.PostAsJsonAsync(reassignPath, assignment, timeout.Token);
        Assert.Equal(HttpStatusCode.Forbidden, deniedAssignment.StatusCode);
        Assert.Single(await db.MaintenanceAssignments.AsNoTracking()
            .Where(item => item.MaintenanceTaskId == task.Id).ToArrayAsync(timeout.Token),
            item => item.StaffUserId == Staff && item.IsActive);
        Assert.False(await db.MaintenanceTaskActivities.AnyAsync(item => item.MaintenanceTaskId == task.Id, timeout.Token));
        Assert.Equal(outboxBefore, await db.ResidentEvents.CountAsync(timeout.Token));

        using var stalePayload = CreateResultEvidence();
        using var validPayload = CreateResultEvidence();
        Task<HttpResponseMessage> reassignment;
        Task<HttpResponseMessage> upload;
        await using (var blocker = await db.Database.BeginTransactionAsync(timeout.Token))
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})",
                [IdentifierCodeNormalizer.AdvisoryLockKey(MaintenanceTaskLock.Scope(task.Id))], timeout.Token);
            reassignment = manager.PostAsJsonAsync(reassignPath, assignment, timeout.Token);
            await WaitForPendingTaskLocks(1);
            upload = former.PostAsync(resultPath, stalePayload, timeout.Token);
            await WaitForPendingTaskLocks(2);
            Assert.Equal(0, storage.StoreCount);
            await blocker.CommitAsync(timeout.Token);
        }

        using var reassigned = await reassignment;
        using var deniedUpload = await upload;
        Assert.Equal(HttpStatusCode.OK, reassigned.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deniedUpload.StatusCode);
        Assert.Equal(0, storage.StoreCount);
        Assert.False(await db.MaintenanceResults.AnyAsync(item => item.MaintenanceTaskId == task.Id));
        Assert.False(await db.MaintenanceResultAttachments.AnyAsync());
        var activity = Assert.Single(await db.MaintenanceTaskActivities.AsNoTracking()
            .Where(item => item.MaintenanceTaskId == task.Id).ToArrayAsync());
        Assert.Equal(MaintenanceActivityType.REASSIGNED, activity.ActivityType);
        var assignments = await db.MaintenanceAssignments.AsNoTracking()
            .Where(item => item.MaintenanceTaskId == task.Id).ToArrayAsync();
        Assert.Equal(2, assignments.Length);
        Assert.Equal(OtherStaff, Assert.Single(assignments, item => item.IsActive).StaffUserId);

        using var accepted = await current.PostAsync(resultPath, validPayload, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var result = await db.MaintenanceResults.AsNoTracking().SingleAsync(item => item.MaintenanceTaskId == task.Id);
        Assert.Equal(OtherStaff, result.SubmittedBy);
        Assert.Equal(result.Id, (await db.MaintenanceResultAttachments.AsNoTracking().SingleAsync()).MaintenanceResultId);
        Assert.Equal(1, storage.StoreCount);

        async Task WaitForPendingTaskLocks(int count)
        {
            while (await db.Database.SqlQueryRaw<int>("""
                SELECT count(*)::int AS "Value"
                FROM pg_locks
                WHERE locktype = 'advisory' AND NOT granted
                  AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
                """).SingleAsync(timeout.Token) != count)
                await Task.Delay(20, timeout.Token);
        }
    }

    private static MultipartFormDataContent CreateResultEvidence()
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent("Đã hoàn tất kiểm tra"), "summary");
        var photo = new ByteArrayContent(SyntheticMaintenanceEvidence.Image);
        photo.Headers.ContentType = new("image/png");
        form.Add(photo, "evidenceImages", "fixture.png");
        return form;
    }

    [Fact]
    [Trait("UseCase", "FE-07.9")]
    public async Task MaintenanceEvidence_RejectsUnrelatedStaffBeforeStorageAndDisablesCaching()
    {
        await using var factory = new Factory();
        await factory.SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MaintenanceDbContext>();
        var storage = (SyntheticMaintenanceEvidence)scope.ServiceProvider.GetRequiredService<IMaintenanceEvidenceStorage>();
        var assignment = await db.MaintenanceAssignments.SingleAsync(item => item.Id == factory.MaintenanceAssignmentId);
        var taskId = assignment.MaintenanceTaskId;
        var result = new MaintenanceResult(taskId, 1, Staff, "Kết quả kiểm thử", Now);
        var evidence = new MaintenanceResultAttachment(result.Id, "fixture", "fixture.png", "image/png",
            SyntheticMaintenanceEvidence.Image.Length, Staff, Now);
        db.AddRange(result, evidence);
        await db.SaveChangesAsync();
        var suffix = $"/{taskId}/results/{result.Id}/attachments/{evidence.Id}";

        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost") });
        using var anonymousResponse = await anonymous.GetAsync("/api/v1/maintenance/my-tasks" + suffix);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var unrelated = factory.Client(id: OtherStaff);
        using var denied = await unrelated.GetAsync("/api/v1/maintenance/my-tasks" + suffix);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var deniedActivities = await unrelated.GetAsync($"/api/v1/maintenance/my-tasks/{taskId}/activities");
        Assert.Equal(HttpStatusCode.Forbidden, deniedActivities.StatusCode);
        Assert.Equal(0, storage.ReadCount);

        using var staff = factory.Client();
        using var wrongAttachment = await staff.GetAsync($"/api/v1/maintenance/my-tasks/{taskId}/results/{result.Id}/attachments/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, wrongAttachment.StatusCode);
        Assert.Equal(0, storage.ReadCount);
        using var manager = factory.Client("MANAGER", "MANAGE_OPERATIONS");
        using var staffFile = await staff.GetAsync("/api/v1/maintenance/my-tasks" + suffix);
        using var managerFile = await manager.GetAsync("/api/v1/maintenance/tasks" + suffix);
        foreach (var file in new[] { staffFile, managerFile })
        {
            Assert.Equal(HttpStatusCode.OK, file.StatusCode);
            Assert.True(file.Headers.CacheControl?.NoStore);
            Assert.Equal(SyntheticMaintenanceEvidence.Image, await file.Content.ReadAsByteArrayAsync());
        }
        Assert.Equal(2, storage.ReadCount);

        assignment.Reassign(Now.AddMinutes(1));
        var replacement = new MaintenanceAssignment(taskId, OtherStaff, Staff, Now.AddMinutes(1));
        var lateResult = new MaintenanceResult(taskId, 2, OtherStaff, "Minh chứng của đợt sau", Now.AddMinutes(2));
        var lateEvidence = new MaintenanceResultAttachment(lateResult.Id, "fixture", "late.png", "image/png",
            SyntheticMaintenanceEvidence.Image.Length, OtherStaff, Now.AddMinutes(2));
        db.AddRange(replacement, lateResult, lateEvidence);
        await db.SaveChangesAsync();

        using var history = await staff.GetAsync($"/api/v1/maintenance/my-history/{taskId}");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var historical = await history.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(result.Id, Assert.Single(historical.GetProperty("results").EnumerateArray()).GetProperty("id").GetGuid());
        using var oldPhoto = await staff.GetAsync("/api/v1/maintenance/my-tasks" + suffix);
        Assert.Equal(HttpStatusCode.OK, oldPhoto.StatusCode);
        using var newPhoto = await staff.GetAsync($"/api/v1/maintenance/my-tasks/{taskId}/results/{lateResult.Id}/attachments/{lateEvidence.Id}");
        Assert.Equal(HttpStatusCode.NotFound, newPhoto.StatusCode);
        Assert.Equal(3, storage.ReadCount);
    }

    [Fact]
    [Trait("UseCase", "FE-07.9")]
    public async Task MaintenanceRealtime_StopsSendingTaskEventsToFormerAssignee()
    {
        await using var factory = new Factory();
        await factory.SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MaintenanceDbContext>();
        var assignment = await db.MaintenanceAssignments.SingleAsync(item => item.Id == factory.MaintenanceAssignmentId);
        var taskId = assignment.MaintenanceTaskId;
        var notifier = scope.ServiceProvider.GetRequiredService<IMaintenanceNotifier>();

        HubConnection Connect(string role, string permission, Guid actor) => new HubConnectionBuilder()
            .WithUrl("https://localhost/hubs/maintenance", options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Headers["X-Test-Role"] = role;
                options.Headers["X-Test-Permission"] = permission;
                options.Headers["X-Test-User"] = actor.ToString();
            }).Build();

        await using var previous = Connect("STAFF", "PERFORM_ASSIGNED_OPERATIONS", Staff);
        await using var current = Connect("STAFF", "PERFORM_ASSIGNED_OPERATIONS", OtherStaff);
        await using var manager = Connect("MANAGER", "MANAGE_OPERATIONS", Guid.NewGuid());
        var currentReceived = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var managerReceived = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var previousRefreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var previousEvents = System.Threading.Channels.Channel.CreateUnbounded<Guid>();
        previous.On<Guid>("MaintenanceTaskUpdated", id => previousEvents.Writer.TryWrite(id));
        previous.On("MaintenanceAssignmentsChanged", () => previousRefreshed.TrySetResult());
        current.On<Guid>("MaintenanceTaskUpdated", id => currentReceived.TrySetResult(id));
        manager.On<Guid>("MaintenanceTaskUpdated", id => managerReceived.TrySetResult(id));
        await previous.StartAsync();
        await current.StartAsync();
        await manager.StartAsync();
        await previous.InvokeAsync("JoinMaintenanceUpdates");
        await current.InvokeAsync("JoinMaintenanceUpdates");
        await manager.InvokeAsync("JoinTask", taskId);
        await Assert.ThrowsAsync<HubException>(() => current.InvokeAsync("JoinTask", taskId));

        // Exercise any task-group subscription that the old implementation allowed before revocation.
        try { await previous.InvokeAsync("JoinTask", taskId); }
        catch (HubException) { }

        assignment.Reassign(Now.AddMinutes(1));
        db.MaintenanceAssignments.Add(new MaintenanceAssignment(taskId, OtherStaff, Guid.NewGuid(), Now.AddMinutes(1)));
        await db.SaveChangesAsync();

        await notifier.NotifyTaskUpdatedAsync(taskId, [Staff, OtherStaff]);
        Assert.Equal(taskId, await currentReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(taskId, await managerReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await previousRefreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(previousEvents.Reader.TryRead(out _));
        await Assert.ThrowsAsync<HubException>(() => previous.InvokeAsync("JoinTask", taskId));
    }

    private sealed class Factory(bool usePostgresForAssets = false, bool usePostgresForMaintenance = false) : WebApplicationFactory<Program>
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
                services.RemoveAll<IRealtimeSessionValidation>();
                services.AddSingleton<IRealtimeSessionValidation>(new TestRealtimeSessionValidation("Fe04Test"));
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
                services.AddScoped(_ => new PropertyAssetsDbContext(usePostgresForAssets && assetsConnectionString is { } connection
                    ? new DbContextOptionsBuilder<PropertyAssetsDbContext>().UseNpgsql(connection).Options
                    : new DbContextOptionsBuilder<PropertyAssetsDbContext>().UseInMemoryDatabase(database + "assets").Options));
                services.AddScoped(_ => new MaintenanceDbContext(usePostgresForMaintenance
                    ? new DbContextOptionsBuilder<MaintenanceDbContext>().UseNpgsql(assetsConnectionString).Options
                    : new DbContextOptionsBuilder<MaintenanceDbContext>().UseInMemoryDatabase(database + "maintenance").Options));
                services.AddScoped(_ => new ServiceRequestsDbContext(new DbContextOptionsBuilder<ServiceRequestsDbContext>().UseInMemoryDatabase(database + "requests").Options));
                services.RemoveAll<IApartmentStatisticsReader>();
                services.AddSingleton<IApartmentStatisticsReader, ApartmentStatistics>();
                services.RemoveAll<IMaintenanceEvidenceStorage>();
                services.AddSingleton<IMaintenanceEvidenceStorage, SyntheticMaintenanceEvidence>();
                services.RemoveAll<IMaintenanceStaffDirectory>();
                services.AddSingleton<IMaintenanceStaffDirectory, MaintenanceStaff>();
            });
        }

        public async Task SeedAsync()
        {
            if (usePostgresForAssets || usePostgresForMaintenance)
            {
                var connection = new NpgsqlConnectionStringBuilder(TestDatabaseConfiguration.GetConnectionString()) { Database = "postgres", Pooling = false };
                var name = "propflow_fe04_http_test_" + Guid.NewGuid().ToString("N");
                await using var admin = new NpgsqlConnection(connection.ConnectionString);
                await admin.OpenAsync();
                await TestDatabaseSafety.CreateAsync(admin, name);
                assetsDatabase = name;
                connection.Database = name;
                assetsConnectionString = connection.ConnectionString;
                await TestDatabaseSafety.VerifyFixtureAsync(assetsConnectionString, name, TestDatabaseSafety.ProcessOwner,
                    TestDatabaseConfiguration.GetConnectionString());
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
            if (usePostgresForMaintenance)
                await maintenance.Database.ExecuteSqlRawAsync(maintenance.Database.GenerateCreateScript());
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
                    await TestDatabaseSafety.DropAsync(admin, name);
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

    private sealed class MaintenanceStaff : IMaintenanceStaffDirectory
    {
        private static readonly MaintenanceStaffRecord[] StaffMembers =
        [
            new(Staff, "staff-a", "Nhân viên A"),
            new(OtherStaff, "staff-b", "Nhân viên B")
        ];

        public Task<IReadOnlyList<MaintenanceStaffRecord>> GetActiveStaffAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MaintenanceStaffRecord>>(StaffMembers);

        public Task<MaintenanceStaffRecord?> GetActiveStaffAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult(StaffMembers.SingleOrDefault(staff => staff.UserId == userId));
    }

    private sealed class SyntheticMaintenanceEvidence : IMaintenanceEvidenceStorage
    {
        public static readonly byte[] Image = [137, 80, 78, 71, 13, 10, 26, 10];
        public int ReadCount { get; private set; }
        public int StoreCount { get; private set; }

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct)
        {
            ReadCount++;
            Assert.Equal("fixture", storageKey);
            return Task.FromResult<Stream>(new MemoryStream(Image));
        }

        public Task<StoredMaintenanceEvidence> StoreAsync(Guid resultId, MaintenanceEvidenceUpload upload, CancellationToken ct)
        {
            StoreCount++;
            return Task.FromResult(new StoredMaintenanceEvidence("fixture", upload.OriginalFileName, "image/png", upload.Length));
        }

        public Task DeleteAsync(string storageKey, CancellationToken ct) => throw new NotSupportedException();
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
