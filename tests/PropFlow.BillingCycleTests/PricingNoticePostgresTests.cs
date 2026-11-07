using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Modules.Billing.Application.Finance;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Billing.Infrastructure.Finance;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Infrastructure;
using PropFlow.Modules.Communication.Application;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Http.Connections;

namespace PropFlow.BillingCycleTests;
public sealed partial class BillingCyclePostgresTests
{
    [Fact]
    public async Task Pricing_changes_commit_with_prices_broadcast_to_all_active_residents_and_deduplicate()
    {
        await using var b = Billing(); await using var p = Payments(); await using var c = Communication();
        await using var auth = new AuthenticationDbContext(new DbContextOptionsBuilder<AuthenticationDbContext>().UseNpgsql(connection,
            x => x.MigrationsHistoryTable("__EFMigrationsHistory", "auth")).Options);
        await using var administration = new AdministrationDbContext(new DbContextOptionsBuilder<AdministrationDbContext>().UseNpgsql(connection,
            x => x.MigrationsHistoryTable("__EFMigrationsHistory", "administration")).Options);
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
        var residentRole = await administration.Roles.SingleAsync(x => x.Code == "RESIDENT");
        var managerRole = await administration.Roles.SingleAsync(x => x.Code == "MANAGER");
        var first = new UserAccount("owner-test", "test-only", "Owner", now, status: AccountStatus.ACTIVE);
        var second = new UserAccount("tenant-test", "test-only", "Tenant", now, status: AccountStatus.ACTIVE);
        var pending = new UserAccount("pending-test", "test-only", "Pending", now);
        var manager = new UserAccount("manager-test", "test-only", "Manager", now, status: AccountStatus.ACTIVE);
        auth.AddRange(first, second, pending, manager); await auth.SaveChangesAsync();
        administration.AddRange(new UserRoleAssignment(first.Id, residentRole.Id, now),
            new UserRoleAssignment(second.Id, residentRole.Id, now), new UserRoleAssignment(pending.Id, residentRole.Id, now),
            new UserRoleAssignment(manager.Id, managerRole.Id, now));
        await administration.SaveChangesAsync();

        await Cycle(b, p, c).SaveRateAsync(new(10000, Month.AddMonths(1), null, 3000, 12000), actor, default);
        var directory = new TypeDirectory(new BillingHousehold(apartment, "A-101", 70, first.Id, "Owner", null));
        var rental = new RentalPricing(b, directory);
        var price = new RentalPricingInput(apartment, "MONTHLY", 5000000, Month.AddMonths(1));
        await rental.SaveAsync(price, actor, default);
        await rental.SaveAsync(price, actor, default); // unchanged save must not announce twice
        Assert.Equal(2, await b.PricingChanges.CountAsync());
        Assert.Empty(await c.Notifications.ToArrayAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => Cycle(b,p,c).SaveRateAsync(new(0, Month.AddMonths(2)), actor, default));
        Assert.Equal(2, await b.PricingChanges.CountAsync());

        var realtime = new CapturePricingRealtime();
        var store = new FinanceNoticePublisher(c, realtime);
        var publisher = new PropFlow.Api.Composition.PricingChangePublisher(auth, administration, store);
        var dispatcher = new PricingChangeDispatcher(b, publisher, NullLogger<PricingChangeDispatcher>.Instance);
        await dispatcher.DispatchAsync(default); await dispatcher.DispatchAsync(default);
        var notices = await c.Notifications.ToArrayAsync();
        Assert.Equal(4, notices.Length);
        Assert.All(notices, n => { Assert.Equal("PRICING", n.SourceType); Assert.False(n.IsRead); Assert.Equal("/resident?tab=notifications", n.ActionPath); });
        Assert.All(notices, n => Assert.Contains(n.RecipientUserId, new[] { first.Id, second.Id }));
        Assert.Equal(2, notices.Count(x => x.Message.Contains("12.000 đ/m³")));
        Assert.Equal(2, notices.Count(x => x.Message.Contains("5.000.000 đ/tháng")));
        Assert.Empty(await c.FinanceEmails.ToArrayAsync());
        Assert.Equal(4, realtime.Created.Count);
        Assert.All(realtime.Created, x => Assert.Equal("PRICING", x.SourceType));
        foreach (var item in await b.PricingChanges.ToArrayAsync()) {
            Assert.Equal("Delivered", item.Status);
            await publisher.PublishAsync(new(item.Id, item.Title, item.Message, item.CreatedAt), default);
        }
        Assert.Equal(4, await c.Notifications.CountAsync());
        Assert.Equal(4, realtime.Created.Count);
        var controller = new PropFlow.Modules.Communication.Presentation.ResidentNotificationsController(c) {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext {
                User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim("sub", first.Id.ToString())], "test")) } }
        };
        var list = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(await controller.List(default));
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(list.Value));
        Assert.Equal(2, json.RootElement.GetArrayLength());
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await controller.Read(notices.First(x => x.RecipientUserId == second.Id).Id, default));
        Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(await controller.Read(notices.First(x => x.RecipientUserId == first.Id).Id, default));

        // Timers are disabled in this host: successful saves must push directly after commit.
        await using var factory = new Factory(connection, first.Id, apartment);
        using var managerClient = factory.Client("MANAGER", actor, "MANAGE_OPERATIONS");
        HubConnection Connect(Guid id) => new HubConnectionBuilder().WithUrl("https://localhost/hubs/resident-notifications", options => {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            options.Headers["X-Role"] = "RESIDENT"; options.Headers["X-User"] = id.ToString();
        }).Build();
        await using var residentConnection = Connect(first.Id);
        await using var secondConnection = Connect(second.Id);
        await using var outsiderConnection = Connect(Guid.NewGuid());
        var received = Channel.CreateUnbounded<FinanceNotificationUpdate>();
        var secondReceived = Channel.CreateUnbounded<FinanceNotificationUpdate>();
        var outsiderCount = 0;
        residentConnection.On<FinanceNotificationUpdate>("FinanceNotificationCreated", n => received.Writer.TryWrite(n));
        secondConnection.On<FinanceNotificationUpdate>("FinanceNotificationCreated", n => secondReceived.Writer.TryWrite(n));
        outsiderConnection.On<FinanceNotificationUpdate>("FinanceNotificationCreated", _ => Interlocked.Increment(ref outsiderCount));
        await residentConnection.StartAsync(); await secondConnection.StartAsync(); await outsiderConnection.StartAsync();
        var serviceResponse = await managerClient.PostAsJsonAsync("/api/v1/billing/cycle/rates", new ServiceRateInput(11000, Month.AddMonths(2), null, 3500, 13000));
        Assert.Equal(HttpStatusCode.OK, serviceResponse.StatusCode);
        var servicePush = await received.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("PRICING", servicePush.SourceType);
        Assert.Contains("13.000 đ/m³", servicePush.Message); Assert.Contains("3.500 đ/kWh", servicePush.Message);
        Assert.Equal(servicePush.Title, (await secondReceived.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10))).Title);
        var rentalInput = new RentalPricingInput(apartment, "MONTHLY", 6000000, Month.AddMonths(2));
        Assert.Equal(HttpStatusCode.OK, (await managerClient.PostAsJsonAsync("/api/v1/billing/rental-pricing", rentalInput)).StatusCode);
        var rentalPush = await received.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("PRICING", rentalPush.SourceType); Assert.Contains("6.000.000 đ/tháng", rentalPush.Message);
        Assert.Equal(rentalPush.Title, (await secondReceived.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10))).Title);
        Assert.Equal(HttpStatusCode.OK, (await managerClient.PostAsJsonAsync("/api/v1/billing/rental-pricing", rentalInput)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await managerClient.PostAsJsonAsync("/api/v1/billing/cycle/rates", new ServiceRateInput(0, Month.AddMonths(3)))).StatusCode);
        Assert.False(received.Reader.TryRead(out _)); Assert.False(secondReceived.Reader.TryRead(out _));
        Assert.Equal(0, outsiderCount);
        Assert.Equal(8, await c.Notifications.CountAsync());
        Assert.Empty(await c.FinanceEmails.ToArrayAsync());
        Assert.Equal(4, await b.PricingChanges.CountAsync(x => x.Status == "Delivered"));
    }

    [Fact]
    public async Task Pricing_delivery_failure_preserves_event_for_retry()
    {
        await using var b = Billing(); await using var p = Payments(); await using var c = Communication();
        await Cycle(b,p,c).SaveRateAsync(new(10000, Month.AddMonths(1), null, 3000, 10000), actor, default);
        var failing = new FailingPricingPublisher();
        var delivery = new PricingChangeDispatcher(b, failing, NullLogger<PricingChangeDispatcher>.Instance);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await delivery.TryDispatchAsync(cancelled.Token);
        Assert.Equal(1, await b.FeeRateRules.CountAsync(x => x.UnitRate == 10000 && x.BillingFrequencyCode == "MONTHLY" && x.CalculationMethodCode == "AREA"));
        Assert.Equal(0, (await b.PricingChanges.SingleAsync()).Attempts);
        await delivery.TryDispatchAsync(default);
        var item = await b.PricingChanges.SingleAsync();
        Assert.Equal("Pending", item.Status); Assert.Equal(1, item.Attempts);
        Assert.True(item.NextAttemptAt > item.CreatedAt); Assert.Null(item.DeliveredAt);
        failing.Fail = false;
        await new PricingChangeDispatcher(b, failing, NullLogger<PricingChangeDispatcher>.Instance,
            new Clock(DateTimeOffset.UtcNow.AddHours(1))).DispatchAsync(default);
        Assert.Equal("Delivered", item.Status); Assert.Equal(2, failing.Attempts);
    }
    private sealed class FailingPricingPublisher : IPricingChangePublisher
    {
        public bool Fail = true; public int Attempts;
        public Task PublishAsync(PricingChangeNotice notice, CancellationToken ct) {
            Attempts++; if (Fail) throw new InvalidOperationException("Simulated delivery failure"); return Task.CompletedTask;
        }
    }
    private sealed class CapturePricingRealtime : IFinanceNotificationRealtime
    {
        public List<FinanceNotificationUpdate> Created { get; } = [];
        public Task CreatedAsync(Guid recipientUserId, FinanceNotificationUpdate notice, CancellationToken ct) {
            Created.Add(notice); return Task.CompletedTask;
        }
    }
}
