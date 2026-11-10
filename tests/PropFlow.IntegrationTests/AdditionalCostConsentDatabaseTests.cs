using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PropFlow.Api.Composition;
using PropFlow.Modules.Administration.Domain.Roles;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Infrastructure;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Application.ReviewManagerServiceRequests;
using PropFlow.Modules.ServiceRequests.Application.SubmitServiceRequest;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Infrastructure.SubmitServiceRequest;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Application.WorkAssignedServiceRequests;
using AccountStatus = PropFlow.Modules.Authentication.Domain.Users.AccountStatus;

namespace PropFlow.IntegrationTests;

public sealed class CostConsentDatabaseFixture : IAsyncLifetime
{
    private readonly string databaseName = "propflow_fe05_cost_" + Guid.NewGuid().ToString("N");
    private string connection = "", adminConnection = "";
    public async Task InitializeAsync()
    {
        var source = new NpgsqlConnectionStringBuilder(TestDatabaseConfiguration.GetConnectionString());
        if (source.Database != "propflow_test") throw new InvalidOperationException("Unexpected test database source.");
        source.Database = "postgres"; adminConnection = source.ConnectionString;
        await using var admin = new NpgsqlConnection(adminConnection); await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin); await create.ExecuteNonQueryAsync();
        source.Database = databaseName; connection = source.ConnectionString;
        await using var db = CreateDb(); await db.Database.MigrateAsync();
    }
    public ServiceRequestsDbContext CreateDb() => new(new DbContextOptionsBuilder<ServiceRequestsDbContext>()
        .UseNpgsql(connection, options => options.MigrationsHistoryTable("__EFMigrationsHistory", "service_requests")).Options);
    public async Task DisposeAsync()
    {
        if (!databaseName.StartsWith("propflow_fe05_cost_", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(adminConnection)) return;
        await using var admin = new NpgsqlConnection(adminConnection); await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync();
    }
}

[Trait("Feature", "FE-05")]
public sealed class AdditionalCostConsentDatabaseTests(CostConsentDatabaseFixture database) : IClassFixture<CostConsentDatabaseFixture>
{
    [Fact]
    public async Task Common_area_work_cannot_propose_a_resident_charge()
    {
        var setup = await Seed(propose: false, serviceArea: ServiceRequestAreaCodes.CommonArea);
        await using var db = database.CreateDb();
        var error = await Assert.ThrowsAsync<ServiceRequestCostFailure>(() =>
            Service(db, setup).ProposeAsync(setup.Staff, setup.Id, new(50000, "Thay vật tư"), default));
        Assert.Equal("common_area_not_chargeable", error.Code);
        Assert.False(await db.ResidentEvents.AnyAsync(row => row.SourceId == setup.Id));
    }

    [Fact]
    public async Task Submission_and_assignment_publish_to_the_authorized_reviewer_and_the_assignee_only()
    {
        await using var db = database.CreateDb();
        var connection = db.Database.GetConnectionString()!;
        DbContextOptions<T> Options<T>(string schema) where T : DbContext =>
            new DbContextOptionsBuilder<T>().UseNpgsql(connection, options => options.MigrationsHistoryTable("__EFMigrationsHistory", schema)).Options;
        await using var accounts = new AuthenticationDbContext(Options<AuthenticationDbContext>("auth"));
        await using var administration = new AdministrationDbContext(Options<AdministrationDbContext>("administration"));
        await using var propertyAssets = new PropertyAssetsDbContext(Options<PropertyAssetsDbContext>("property_assets"));
        await using var apartments = new ApartmentsDbContext(Options<ApartmentsDbContext>("apartments"));
        await using var residents = new ResidentsDbContext(Options<ResidentsDbContext>("residents"));
        await using var communication = new CommunicationDbContext(Options<CommunicationDbContext>("communication"));
        await accounts.GetService<IMigrator>().MigrateAsync("20260914101250_CorrectAuthenticationEmailOtpVerification");
        await propertyAssets.Database.MigrateAsync();
        await apartments.Database.MigrateAsync();
        await residents.GetService<IMigrator>().MigrateAsync("20260914063807_InitialResidents");
        await accounts.Database.MigrateAsync();
        await administration.Database.MigrateAsync();
        await residents.Database.MigrateAsync();
        await communication.Database.MigrateAsync();

        var now = DateTimeOffset.UtcNow;
        UserAccount Account(string name, AccountStatus status = AccountStatus.ACTIVE) =>
            new(Guid.NewGuid().ToString("N"), "test-password-hash", name, now, status: status);
        var owner = Account("Nguyễn Cư Dân");
        var manager = Account("Trần Quản Lý");
        var inactiveManager = Account("Quản lý ngừng hoạt động", AccountStatus.PENDING);
        var assignee = Account("Nhân viên được giao");
        var otherStaff = Account("Nhân viên khác");
        accounts.AddRange(owner, manager, inactiveManager, assignee, otherStaff);
        await accounts.SaveChangesAsync();
        var managerRole = await administration.Roles.SingleAsync(role => role.Code == SystemRoleCodes.Manager);
        administration.UserRoleAssignments.AddRange(new UserRoleAssignment(manager.Id, managerRole.Id, now),
            new UserRoleAssignment(inactiveManager.Id, managerRole.Id, now));
        await administration.SaveChangesAsync();
        var resident = new Resident("CD-" + Guid.NewGuid().ToString("N")[..12], owner.DisplayName, now, owner.Id);
        residents.Residents.Add(resident);
        await residents.SaveChangesAsync();
        var scope = new Scope(new(Guid.Empty, owner.Id, resident.Id, assignee.Id, Guid.Empty));
        var handler = new SubmitServiceRequestHandler(new ServiceRequestSubmissionStore(db), scope, scope, TimeProvider.System);
        var publisher = new FinanceNoticePublisher(communication);
        var dispatcher = new ServiceRequestCostNoticeDispatcher(db, residents, publisher, TimeProvider.System,
            NullLogger<ServiceRequestCostNoticeDispatcher>.Instance, new WorkflowNotificationDelivery(accounts, administration, publisher));
        var category = await db.ServiceRequestCategories.Where(row => row.IsActive).Select(row => row.Code).FirstAsync();
        var command = new CreateResidentServiceRequestRequest(category, "Yêu cầu mới", "Xin kiểm tra sự cố trong căn hộ.", "LIVING_ROOM", "NORMAL", null, null);

        Assert.Equal(SubmitServiceRequestOutcome.InvalidInput, (await handler.HandleAsync(owner.Id, command with { Title = "" }, default)).Outcome);
        await dispatcher.DispatchAsync(default);
        Assert.Empty(await communication.Notifications.ToArrayAsync());
        await using (var rolledBack = await db.Database.BeginTransactionAsync())
        {
            Assert.Equal(SubmitServiceRequestOutcome.Created, (await handler.HandleAsync(owner.Id, command, default)).Outcome);
            await rolledBack.RollbackAsync();
        }
        db.ChangeTracker.Clear();
        await dispatcher.DispatchAsync(default);
        Assert.Empty(await communication.Notifications.ToArrayAsync());

        var created = await handler.HandleAsync(owner.Id, command, default);
        Assert.Equal(SubmitServiceRequestOutcome.Created, created.Outcome);
        var request = created.Response!;
        var store = new ManagerServiceRequestStore(db);
        Assert.Equal(ManagerServiceRequestOutcome.Conflict, await store.AssignAsync(request.Id, manager.Id, assignee.Id, null, now, default));
        await dispatcher.DispatchAsync(default);
        var submission = Assert.Single(await communication.Notifications.ToArrayAsync());
        Assert.Equal(manager.Id, submission.RecipientUserId);
        Assert.Equal(owner.Id, submission.SenderUserId);
        Assert.Equal(request.Id, submission.SourceId);
        Assert.Equal($"/manager/service-requests/{request.Id}", submission.ActionPath);
        Assert.Contains(owner.DisplayName, submission.Message);
        Assert.False(submission.IsRead);

        var persisted = await db.ServiceRequests.SingleAsync(row => row.Id == request.Id);
        persisted.MarkUnderReview(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        await using (var rolledBack = await db.Database.BeginTransactionAsync())
        {
            Assert.Equal(ManagerServiceRequestOutcome.Success,
                await store.AssignAsync(request.Id, manager.Id, assignee.Id, null, DateTimeOffset.UtcNow, default));
            await rolledBack.RollbackAsync();
        }
        db.ChangeTracker.Clear();
        await dispatcher.DispatchAsync(default);
        Assert.Single(await communication.Notifications.ToArrayAsync());
        Assert.Equal(ManagerServiceRequestOutcome.Success,
            await store.AssignAsync(request.Id, manager.Id, assignee.Id, null, DateTimeOffset.UtcNow, default));
        Assert.Equal(ManagerServiceRequestOutcome.Conflict,
            await store.AssignAsync(request.Id, manager.Id, assignee.Id, null, DateTimeOffset.UtcNow, default));
        await dispatcher.DispatchAsync(default);
        await dispatcher.DispatchAsync(default);
        var assignment = Assert.Single(await communication.Notifications.Where(row => row.Id != submission.Id).ToArrayAsync());
        Assert.Equal(assignee.Id, assignment.RecipientUserId);
        Assert.Equal(manager.Id, assignment.SenderUserId);
        Assert.Equal($"/staff/service-requests/{request.RequestNumber}", assignment.ActionPath);
        Assert.Contains(manager.DisplayName, assignment.Message);
        Assert.False(assignment.IsRead);
    }

    [Fact]
    public async Task ConcurrentOppositeDecisionsPersistExactlyOneResidentDecision()
    {
        var setup = await Seed();
        var results = await Task.WhenAll(Respond(setup, true), Respond(setup, false));
        Assert.Equal(1, results.Count(x => x == "OK")); Assert.Equal(1, results.Count(x => x == "409"));
        await using var db = database.CreateDb();
        var request = await db.ServiceRequests.Include(x => x.Assignments).SingleAsync(x => x.Id == setup.Id);
        Assert.Contains(request.AdditionalCostConsentStatus, new[] { "ACCEPTED", "REJECTED" });
        Assert.Equal(1, await db.ServiceRequestActivities.CountAsync(x => x.ServiceRequestId == setup.Id &&
            (x.ActivityType == ServiceActivityType.ADDITIONAL_COST_ACCEPTED || x.ActivityType == ServiceActivityType.ADDITIONAL_COST_REJECTED)));
        if (request.AdditionalCostConsentStatus == "REJECTED")
        { Assert.Equal(ServiceRequestStatus.CANCELLED, request.Status); Assert.All(request.Assignments, x => Assert.Equal(AssignmentStatus.CANCELLED, x.Status)); }
        Assert.Null(request.ServiceInvoiceId);
    }

    [Fact]
    public async Task ConcurrentReplacementNeverAuthorizesTheNewAmountUsingAnOldResponse()
    {
        var setup = await Seed();
        async Task Replace()
        {
            await using var db = database.CreateDb();
            await Service(db, setup).ProposeAsync(setup.Staff, setup.Id, new(70000, "Báo giá thay đổi", setup.Proposal), default);
        }
        await Task.WhenAll(Replace(), Respond(setup, true));
        await using var check = database.CreateDb();
        var request = await check.ServiceRequests.SingleAsync(x => x.Id == setup.Id);
        Assert.Equal(70000, request.ProposedAdditionalCost); Assert.Equal("PENDING", request.AdditionalCostConsentStatus);
        Assert.NotEqual(setup.Proposal, request.AdditionalCostProposalId);
        Assert.Equal(StaffServiceRequestOutcome.Conflict, await new StaffServiceRequestStore(check).CompleteWorkAsync(setup.Staff,
            setup.Id, "Không được làm theo báo giá chưa duyệt", 70000, "Báo giá thay đổi", DateTimeOffset.UtcNow, default));
    }

    [Fact]
    public async Task ProposalAndNotificationAreCommittedTogetherAndStayVisibleInSqlProjections()
    {
        var setup = await Seed();
        await using var db = database.CreateDb();
        Assert.Single(await db.ResidentEvents.Where(x => x.SourceId == setup.Id).ToArrayAsync());
        var detail = await new StaffServiceRequestStore(db).GetByIdAsync(setup.Staff, setup.Id, default);
        Assert.Equal("PENDING", detail!.AdditionalCostConsent!.Status);
        var error = await Assert.ThrowsAsync<ServiceRequestCostFailure>(() => Service(db, setup).RespondAsync(Guid.NewGuid(), setup.Id, new(setup.Proposal, true), default));
        Assert.Equal(404, error.Status);
        var current = await db.ServiceRequests.AsNoTracking().SingleAsync(x => x.Id == setup.Id);
        Assert.Equal("PENDING", current.AdditionalCostConsentStatus);
    }

    private async Task<string> Respond(Setup setup, bool accepted)
    {
        await using var db = database.CreateDb();
        try { await Service(db, setup).RespondAsync(setup.Owner, setup.Id, new(setup.Proposal, accepted), default); return "OK"; }
        catch (ServiceRequestCostFailure error) { return error.Status.ToString(); }
    }
    private static AdditionalCostConsentService Service(ServiceRequestsDbContext db, Setup setup) => new(db, new Scope(setup), new Scope(setup), TimeProvider.System);
    private async Task<Setup> Seed(bool propose = true, string? serviceArea = null)
    {
        await using var db = database.CreateDb(); var now = DateTimeOffset.UtcNow;
        var setup = new Setup(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.Empty);
        var request = new ServiceRequest("YC-" + Guid.NewGuid().ToString("N")[..20], setup.Resident, Guid.NewGuid(), "Sửa thiết bị", "Kiểm tra và đề xuất chi phí", now,
            serviceAreaCode: serviceArea, submittedByUserId: setup.Owner);
        request.MarkUnderReview(now); request.MarkAssigned(now); request.MarkInProgress(now);
        var assignment = new ServiceRequestAssignment(request.Id, setup.Staff, Guid.NewGuid(), now); assignment.Start(now);
        db.AddRange(request, assignment); await db.SaveChangesAsync(); setup = setup with { Id = request.Id };
        if (!propose) return setup;
        var proposal = await Service(db, setup).ProposeAsync(setup.Staff, setup.Id, new(50000, "Thay vật tư"), default);
        return setup with { Proposal = proposal.ProposalId };
    }
    private sealed record Setup(Guid Id, Guid Owner, Guid Resident, Guid Staff, Guid Proposal);
    private sealed class Scope(Setup setup) : IResidentResidenceSource, ICurrentBuildingTimeZone
    {
        public Task<ActiveResidentResidence?> FindActiveAsync(Guid userId, DateOnly date, CancellationToken ct) =>
            Task.FromResult<ActiveResidentResidence?>(new(userId == setup.Owner ? setup.Resident : Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
        public Task<string> GetAsync(CancellationToken ct) => Task.FromResult("Asia/Ho_Chi_Minh");
    }
}
