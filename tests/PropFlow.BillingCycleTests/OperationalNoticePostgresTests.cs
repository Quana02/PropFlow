using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Http.Connections;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Infrastructure;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;
using Equipment = PropFlow.Modules.PropertyAssets.Domain.Equipment.Equipment;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;
using PropFlow.Modules.Communication.Application;

namespace PropFlow.BillingCycleTests;
public sealed partial class BillingCyclePostgresTests
{
    private PropertyAssetsDbContext OperationalAssets() => new(new DbContextOptionsBuilder<PropertyAssetsDbContext>()
        .UseNpgsql(connection, x => x.MigrationsHistoryTable("__EFMigrationsHistory", "property_assets")).Options);
    private MaintenanceDbContext OperationalMaintenance() => new(new DbContextOptionsBuilder<MaintenanceDbContext>()
        .UseNpgsql(connection, x => x.MigrationsHistoryTable("__EFMigrationsHistory", "maintenance")).Options);

    [Fact]
    public async Task Operational_status_events_capture_manager_and_automatic_paths_atomically_without_duplicates()
    {
        await using var assets = OperationalAssets(); await assets.Database.MigrateAsync();
        await using var maintenance = OperationalMaintenance(); await maintenance.Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var facility = new Facility("F-NOTICE", "Khu sảnh", now, locationDescription: "Tháp A");
        var equipment = new Equipment("EQ-NOTICE", "Thang máy A", now, facility.Id, locationDescription: "Tháp A");
        assets.AddRange(facility, equipment); await assets.SaveChangesAsync();
        Assert.Single(await assets.ResidentEvents.ToArrayAsync());
        Assert.Contains("Tiện ích công cộng mới", System.Text.Json.JsonDocument.Parse((await assets.ResidentEvents.SingleAsync()).Payload).RootElement.GetProperty("Title").GetString());
        var source = new MaintenanceAssetSource(assets);
        await source.MarkUnderMaintenanceAsync((Guid?)facility.Id, equipment.Id, actor, default);
        Assert.Equal(2, await assets.ResidentEvents.CountAsync());
        await source.MarkUnderMaintenanceAsync((Guid?)facility.Id, equipment.Id, actor, default);
        Assert.Equal(2, await assets.ResidentEvents.CountAsync());
        await using (var rollback = await assets.Database.BeginTransactionAsync()) {
            equipment.MarkActive(actor, now.AddSeconds(1)); await assets.SaveChangesAsync();
            Assert.Equal(3, await assets.ResidentEvents.CountAsync()); await rollback.RollbackAsync();
        }
        assets.ChangeTracker.Clear();
        Assert.Equal(2, await assets.ResidentEvents.CountAsync());
        await source.MarkActiveAsync((Guid?)facility.Id, equipment.Id, actor, default);
        Assert.Equal(3, await assets.ResidentEvents.CountAsync());
        Assert.Contains(await assets.ResidentEvents.ToArrayAsync(), n => n.Payload.Contains("Thang") && n.Name == "AssetAvailabilityChanged");

        var work = new MaintenanceTask("WORK-NOTICE", "Private task description is not broadcast", actor, now, equipmentId: equipment.Id);
        maintenance.Add(work); await maintenance.SaveChangesAsync();
        work.MarkAssigned(now); await maintenance.SaveChangesAsync();
        Assert.Empty(await maintenance.ResidentEvents.ToArrayAsync());
        work.Start(now); await maintenance.SaveChangesAsync();
        work.Start(now); await maintenance.SaveChangesAsync();
        Assert.Single(await maintenance.ResidentEvents.ToArrayAsync());
        work.Complete(now.AddSeconds(1)); await maintenance.SaveChangesAsync();
        work.Close(actor, now.AddSeconds(2)); await maintenance.SaveChangesAsync();
        var events = await maintenance.ResidentEvents.ToArrayAsync();
        Assert.Equal(3, events.Length);
        Assert.All(events, n => Assert.DoesNotContain("Private task", n.Payload));
    }

    [Fact]
    public async Task Operational_manager_change_pushes_to_all_connected_residents_without_page_reload()
    {
        await using var assets = OperationalAssets(); await assets.Database.MigrateAsync();
        await using var maintenance = OperationalMaintenance(); await maintenance.Database.MigrateAsync();
        await using var auth = new AuthenticationDbContext(new DbContextOptionsBuilder<AuthenticationDbContext>()
            .UseNpgsql(connection, x => x.MigrationsHistoryTable("__EFMigrationsHistory", "auth")).Options);
        await using var administration = new AdministrationDbContext(new DbContextOptionsBuilder<AdministrationDbContext>()
            .UseNpgsql(connection, x => x.MigrationsHistoryTable("__EFMigrationsHistory", "administration")).Options);
        await using var apartmentDb = new PropFlow.Modules.Apartments.Infrastructure.Persistence.ApartmentsDbContext(
            new DbContextOptionsBuilder<PropFlow.Modules.Apartments.Infrastructure.Persistence.ApartmentsDbContext>()
            .UseNpgsql(connection, x => x.MigrationsHistoryTable("__EFMigrationsHistory", "apartments")).Options);
        await using var residentDb = new PropFlow.Modules.Residents.Infrastructure.Persistence.ResidentsDbContext(
            new DbContextOptionsBuilder<PropFlow.Modules.Residents.Infrastructure.Persistence.ResidentsDbContext>()
            .UseNpgsql(connection, x => x.MigrationsHistoryTable("__EFMigrationsHistory", "residents")).Options);
        await apartmentDb.Database.MigrateAsync();
        await auth.GetService<IMigrator>().MigrateAsync("20260914101250_CorrectAuthenticationEmailOtpVerification");
        await residentDb.GetService<IMigrator>().MigrateAsync("20260914063807_InitialResidents");
        await auth.Database.MigrateAsync(); await residentDb.Database.MigrateAsync(); await administration.Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var role = await administration.Roles.SingleAsync(x => x.Code == "RESIDENT");
        var first = new UserAccount("resident-a", "test", "A", now, status: AccountStatus.ACTIVE);
        var second = new UserAccount("resident-b", "test", "B", now, status: AccountStatus.ACTIVE);
        var pending = new UserAccount("pending-c", "test", "C", now);
        auth.AddRange(first, second, pending); await auth.SaveChangesAsync();
        administration.AddRange(new UserRoleAssignment(first.Id, role.Id, now), new UserRoleAssignment(second.Id, role.Id, now), new UserRoleAssignment(pending.Id, role.Id, now));
        await administration.SaveChangesAsync();
        var equipment = new Equipment("EQ-PUSH", "Thang máy realtime", now, locationDescription: "Sảnh A");
        assets.Add(equipment); await assets.SaveChangesAsync();

        await using var factory = new Factory(connection, first.Id, apartment);
        using var manager = factory.Client("MANAGER", actor, "MANAGE_OPERATIONS");
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsync("/hubs/resident-notifications/negotiate?negotiateVersion=1", null)).StatusCode);
        HubConnection Connect(Guid id) => new HubConnectionBuilder().WithUrl("https://localhost/hubs/resident-notifications", options => {
            options.Transports = HttpTransportType.LongPolling; options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            options.Headers["X-Role"] = "RESIDENT"; options.Headers["X-User"] = id.ToString();
        }).Build();
        await using var firstHub = Connect(first.Id); await using var secondHub = Connect(second.Id); await using var excludedHub = Connect(pending.Id);
        var a = new TaskCompletionSource<FinanceNotificationUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        var b = new TaskCompletionSource<FinanceNotificationUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        var counts = new int[3];
        firstHub.On<FinanceNotificationUpdate>("FinanceNotificationCreated", n => { Interlocked.Increment(ref counts[0]); a.TrySetResult(n); });
        secondHub.On<FinanceNotificationUpdate>("FinanceNotificationCreated", n => { Interlocked.Increment(ref counts[1]); b.TrySetResult(n); });
        excludedHub.On<FinanceNotificationUpdate>("FinanceNotificationCreated", _ => Interlocked.Increment(ref counts[2]));
        await firstHub.StartAsync(); await secondHub.StartAsync(); await excludedHub.StartAsync();
        var result = await manager.PatchAsync($"api/v1/equipments/{equipment.Id}/status", JsonContent.Create(new { status = "UNDER_MAINTENANCE" }));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        using var scope = factory.Services.CreateScope();
        using var publication = factory.Services.CreateScope();
        var scopedAssets = scope.ServiceProvider.GetRequiredService<PropertyAssetsDbContext>();
        var deliveryClock = new Clock(DateTimeOffset.UtcNow);
        var dispatcher = new PropFlow.Api.Composition.OperationalNoticeDispatcher(scopedAssets,
            scope.ServiceProvider.GetRequiredService<MaintenanceDbContext>(),
            scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>(),
            scope.ServiceProvider.GetRequiredService<AdministrationDbContext>(),
            new FailSecondOperationalDelivery(publication.ServiceProvider.GetRequiredService<IFinanceNotificationStore>()),
            scope.ServiceProvider.GetRequiredService<PropFlow.Modules.PropertyAssets.Contracts.IMaintenanceAssetSource>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PropFlow.Api.Composition.OperationalNoticeDispatcher>.Instance, deliveryClock);
        await dispatcher.DispatchAssetsAsync(default);
        var retry = await scopedAssets.ResidentEvents.SingleAsync();
        Assert.Equal("Pending", retry.Status); Assert.Equal(1, retry.Attempts);
        deliveryClock.Now = deliveryClock.Now.AddMinutes(1);
        await dispatcher.DispatchAssetsAsync(default);
        Assert.Equal("Delivered", retry.Status);
        var received = await Task.WhenAll(a.Task.WaitAsync(TimeSpan.FromSeconds(10)), b.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.All(received, n => { Assert.Equal("ASSET_STATUS", n.SourceType); Assert.Contains("Thang máy realtime", n.Message); Assert.Contains("bảo trì", n.Message); });
        await dispatcher.DispatchAssetsAsync(default);
        await Task.Delay(100);
        Assert.Equal(new[] { 1, 1, 0 }, counts);
        await using var communication = Communication();
        Assert.Equal(2, await communication.Notifications.CountAsync());
        Assert.Empty(await communication.FinanceEmails.ToArrayAsync());

        var work = new MaintenanceTask("WORK-PUSH", "Internal title", actor, now, equipmentId: equipment.Id);
        maintenance.Add(work); await maintenance.SaveChangesAsync(); work.MarkAssigned(now); await maintenance.SaveChangesAsync();
        work.Start(now); await maintenance.SaveChangesAsync();
        await dispatcher.DispatchMaintenanceAsync(default);
        Assert.Equal(4, await communication.Notifications.CountAsync());
        Assert.Contains(await communication.Notifications.ToArrayAsync(), n => n.SourceType == "MAINTENANCE" && n.Message.Contains("Thang máy realtime"));

        var facilityPush = new TaskCompletionSource<FinanceNotificationUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = secondHub.On<FinanceNotificationUpdate>("FinanceNotificationCreated", n => {
            if (n.Title.Contains("Tiện ích công cộng mới")) facilityPush.TrySetResult(n);
        });
        var facility = new Facility("POOL-PUSH", "Hồ bơi cư dân", now, locationDescription: "Tầng 5");
        assets.Add(facility); await assets.SaveChangesAsync();
        await dispatcher.DispatchAssetsAsync(default);
        var created = await facilityPush.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("Hồ bơi cư dân", created.Message);
        Assert.Contains("Tầng 5", created.Message);
        Assert.Equal(6, await communication.Notifications.CountAsync());
        var expectedCount = 6;
        foreach (var target in new[] { ("facilities", facility.Id), ("equipments", equipment.Id) }) {
            foreach (var status in new[] { "OUT_OF_SERVICE", "ACTIVE", "UNDER_MAINTENANCE", "ACTIVE" }) {
                var pushed = new TaskCompletionSource<FinanceNotificationUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var changeSubscription = secondHub.On<FinanceNotificationUpdate>("FinanceNotificationCreated", n => pushed.TrySetResult(n));
                var changed = await manager.PatchAsync($"api/v1/{target.Item1}/{target.Id}/status", JsonContent.Create(new { status }));
                Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
                await dispatcher.DispatchAssetsAsync(default);
                var notice = await pushed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal("ASSET_STATUS", notice.SourceType);
                Assert.Contains(status switch {
                    "OUT_OF_SERVICE" => "ngừng phục vụ",
                    "UNDER_MAINTENANCE" => "bảo trì",
                    _ => "hoạt động trở lại"
                }, notice.Message);
                expectedCount += 2;
                Assert.Equal(expectedCount, await communication.Notifications.CountAsync());
                // Repeating the same selection from the manager menu must not announce again.
                Assert.Equal(HttpStatusCode.OK, (await manager.PatchAsync($"api/v1/{target.Item1}/{target.Id}/status", JsonContent.Create(new { status }))).StatusCode);
                await dispatcher.DispatchAssetsAsync(default);
                Assert.Equal(expectedCount, await communication.Notifications.CountAsync());
            }
        }
    }
    private sealed class FailSecondOperationalDelivery(IFinanceNotificationStore inner) : IFinanceNotificationStore
    {
        private int calls;
        public Task PublishAsync(FinanceNotification notification, CancellationToken ct) {
            if (++calls == 2) throw new InvalidOperationException("Simulated partial broadcast failure");
            return inner.PublishAsync(notification, ct);
        }
    }
}


