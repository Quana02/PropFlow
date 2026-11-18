using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using System.Text.Json;
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
using PropFlow.Shared.Messaging;
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
        await TestDatabaseSafety.CreateAsync(admin, databaseName);
        source.Database = databaseName; connection = source.ConnectionString;
        await TestDatabaseSafety.VerifyFixtureAsync(connection, databaseName, TestDatabaseSafety.ProcessOwner,
            TestDatabaseConfiguration.GetConnectionString());
        await using var db = CreateDb(); await db.Database.MigrateAsync();
    }
    public ServiceRequestsDbContext CreateDb() => new(new DbContextOptionsBuilder<ServiceRequestsDbContext>()
        .UseNpgsql(connection, options => options.MigrationsHistoryTable("__EFMigrationsHistory", "service_requests")).Options);
    public async Task DisposeAsync()
    {
        if (!databaseName.StartsWith("propflow_fe05_cost_", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(adminConnection)) return;
        await using var admin = new NpgsqlConnection(adminConnection); await admin.OpenAsync();
        await TestDatabaseSafety.DropAsync(admin, databaseName);
    }
}

[Trait("Feature", "FE-05")]
public sealed class AdditionalCostConsentDatabaseTests(CostConsentDatabaseFixture database) : IClassFixture<CostConsentDatabaseFixture>
{
    [Fact]
    public async Task UpgradeRoutesOnlyUnansweredLegacyQuotesBackToManagerAndRetiresQueuedResidentNotices()
    {
        var isolated = new CostConsentDatabaseFixture();
        await isolated.InitializeAsync();
        try
        {
            await using var db = isolated.CreateDb();
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261101095403_RequireResidentConsentForAdditionalCosts");
            var now = DateTimeOffset.UtcNow;
            ServiceRequest Legacy(string suffix, bool accepted = false, bool invoiced = false)
            {
                var request = new ServiceRequest("LEGACY-" + suffix, Guid.NewGuid(), Guid.NewGuid(), "Sửa vòi nước", "Kiểm tra vòi nước", now);
                request.MarkUnderReview(now); request.SetBaseServiceCost(10000, now); request.MarkAssigned(now); request.MarkInProgress(now);
                request.ProposeAdditionalCost(50000, "Thay van", now);
                request.SubmitAdditionalCostForResident(request.AdditionalCostProposalId!.Value, now);
                if (accepted) request.RespondToAdditionalCost(request.AdditionalCostProposalId.Value, Guid.NewGuid(), true, now);
                if (invoiced) { request.ApproveAdditionalCost(50000, now); request.Resolve(now); request.LinkServiceInvoice(Guid.NewGuid(), now); }
                return request;
            }
            var pending = Legacy("PENDING"); var accepted = Legacy("ACCEPTED", true); var issued = Legacy("ISSUED", true, true);
            var invoiceId = issued.ServiceInvoiceId;
            var oldNotice = new OutboxEvent("ServiceRequestAdditionalCostProposed", pending.Id,
                JsonSerializer.Serialize(new AdditionalCostNotice(pending.ResidentId, pending.RequestNumber, 50000, "Thay van", pending.AdditionalCostProposalId!.Value)), now);
            db.AddRange(pending, accepted, issued, oldNotice);
            await db.SaveChangesAsync();
            await migrator.MigrateAsync();
            db.ChangeTracker.Clear();
            var migrated = await db.ServiceRequests.AsNoTracking().SingleAsync(x => x.Id == pending.Id);
            Assert.Equal("AWAITING_MANAGER", migrated.AdditionalCostConsentStatus);
            Assert.Equal(10000, migrated.BaseServiceCost);
            Assert.Equal("ACCEPTED", (await db.ServiceRequests.AsNoTracking().SingleAsync(x => x.Id == accepted.Id)).AdditionalCostConsentStatus);
            Assert.Equal(invoiceId, (await db.ServiceRequests.AsNoTracking().SingleAsync(x => x.Id == issued.Id)).ServiceInvoiceId);
            Assert.Equal("Delivered", (await db.ResidentEvents.SingleAsync(x => x.Id == oldNotice.Id)).Status);
            Assert.Single(await db.ResidentEvents.Where(x => x.Name == "ServiceRequestAdditionalCostReviewRequested").ToArrayAsync());
        }
        finally { await isolated.DisposeAsync(); }
    }

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
        var staffRole = await administration.Roles.SingleAsync(role => role.Code == SystemRoleCodes.Staff);
        administration.UserRoleAssignments.AddRange(new UserRoleAssignment(manager.Id, managerRole.Id, now),
            new UserRoleAssignment(inactiveManager.Id, managerRole.Id, now),
            new UserRoleAssignment(assignee.Id, staffRole.Id, now),
            new UserRoleAssignment(otherStaff.Id, staffRole.Id, now));
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

        Assert.Equal(SubmitServiceRequestOutcome.InvalidInput, (await handler.HandleAsync(owner.Id, command with { Description = "" }, default)).Outcome);
        await dispatcher.DispatchAsync(default);
        Assert.Empty(await communication.Notifications.Where(row => row.SenderUserId == owner.Id).ToArrayAsync());
        await using (var rolledBack = await db.Database.BeginTransactionAsync())
        {
            Assert.Equal(SubmitServiceRequestOutcome.Created, (await handler.HandleAsync(owner.Id, command, default)).Outcome);
            await rolledBack.RollbackAsync();
        }
        db.ChangeTracker.Clear();
        await dispatcher.DispatchAsync(default);
        Assert.Empty(await communication.Notifications.Where(row => row.SenderUserId == owner.Id).ToArrayAsync());

        var created = await handler.HandleAsync(owner.Id, command, default);
        Assert.Equal(SubmitServiceRequestOutcome.Created, created.Outcome);
        var request = created.Response!;
        var store = new ManagerServiceRequestStore(db);
        Assert.Equal(ManagerServiceRequestOutcome.Conflict, await store.AssignAsync(request.Id, manager.Id, assignee.Id, null, now, default));
        await dispatcher.DispatchAsync(default);
        var submission = Assert.Single(await communication.Notifications.Where(row => row.SourceId == request.Id).ToArrayAsync());
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
        Assert.Single(await communication.Notifications.Where(row => row.SourceId == request.Id).ToArrayAsync());
        Assert.Equal(ManagerServiceRequestOutcome.Success,
            await store.AssignAsync(request.Id, manager.Id, assignee.Id, null, DateTimeOffset.UtcNow, default));
        Assert.Equal(ManagerServiceRequestOutcome.Conflict,
            await store.AssignAsync(request.Id, manager.Id, assignee.Id, null, DateTimeOffset.UtcNow, default));
        await dispatcher.DispatchAsync(default);
        await dispatcher.DispatchAsync(default);
        var assignment = Assert.Single(await communication.Notifications.Where(row => row.SourceId == request.Id && row.Id != submission.Id).ToArrayAsync());
        Assert.Equal(assignee.Id, assignment.RecipientUserId);
        Assert.Equal(manager.Id, assignment.SenderUserId);
        Assert.Equal($"/staff/service-requests/{request.RequestNumber}", assignment.ActionPath);
        Assert.Contains(manager.DisplayName, assignment.Message);
        Assert.False(assignment.IsRead);

        // The worker was offline while this request changed twice. Only the current
        // assignment may create another notice, even if the same person is assigned again.
        Assert.Equal(ManagerServiceRequestOutcome.Success,
            await store.AssignAsync(request.Id, manager.Id, otherStaff.Id, null, DateTimeOffset.UtcNow, default));
        Assert.Equal(ManagerServiceRequestOutcome.Success,
            await store.AssignAsync(request.Id, manager.Id, assignee.Id, null, DateTimeOffset.UtcNow, default));
        await dispatcher.DispatchAsync(default);
        await dispatcher.DispatchAsync(default);
        var delivered = await communication.Notifications.AsNoTracking().Where(row => row.SourceId == request.Id).ToArrayAsync();
        Assert.DoesNotContain(delivered, row => row.RecipientUserId == otherStaff.Id);
        Assert.Equal(2, delivered.Count(row => row.RecipientUserId == assignee.Id));
        Assert.Equal(3, delivered.Length);

        // Requeue a failed delivery from B, then replace A's current envelope with a
        // pre-upgrade payload. An old envelope for the same recipient must stay stale.
        var retryAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        Assert.Equal(ManagerServiceRequestOutcome.Success,
            await store.AssignAsync(request.Id, manager.Id, otherStaff.Id, null, retryAt, default));
        var failed = await db.ResidentEvents.SingleAsync(row => row.SourceId == request.Id && row.Status == "Pending");
        failed.Failed("FixtureTransientFailure", retryAt);
        Assert.Equal(ManagerServiceRequestOutcome.Success,
            await store.AssignAsync(request.Id, manager.Id, assignee.Id, null, retryAt.AddTicks(10), default));
        var modern = await db.ResidentEvents.SingleAsync(row => row.SourceId == request.Id && row.Status == "Pending" && row.Id != failed.Id);
        db.ResidentEvents.Remove(modern);
        OutboxEvent Legacy(DateTimeOffset timestamp) => new(ServiceRequestWorkflowNotice.Assigned, request.Id,
            JsonSerializer.Serialize(new { SenderUserId = manager.Id, RecipientUserId = assignee.Id, request.RequestNumber }), timestamp);
        var legacyCurrent = Legacy(modern.CreatedAt);
        var legacyStale = Legacy(modern.CreatedAt.AddTicks(-10));
        db.ResidentEvents.AddRange(legacyStale, legacyCurrent);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await dispatcher.DispatchAsync(default);
        await dispatcher.DispatchAsync(default);
        var afterLegacy = await communication.Notifications.AsNoTracking().Where(row => row.SourceId == request.Id).ToArrayAsync();
        Assert.Equal(4, afterLegacy.Length);
        Assert.Contains(afterLegacy, row => row.Id == assignment.Id);
        Assert.Equal(3, afterLegacy.Count(row => row.RecipientUserId == assignee.Id));
        Assert.Equal("Delivered", (await db.ResidentEvents.SingleAsync(row => row.Id == failed.Id)).Status);

        await VerifyReassignmentDuringDispatch(db, dispatcher, request.Id, manager.Id, assignee.Id, otherStaff.Id);
        await dispatcher.DispatchAsync(default);
        await dispatcher.DispatchAsync(default);
        var afterContention = await communication.Notifications.AsNoTracking().Where(row => row.SourceId == request.Id).ToArrayAsync();
        Assert.Equal(5, afterContention.Length);
        Assert.DoesNotContain(afterContention, row => row.RecipientUserId == otherStaff.Id);
        Assert.All(afterLegacy, old => Assert.Contains(afterContention, row => row.Id == old.Id));

        var sharedTimestamp = DateTimeOffset.UtcNow;
        foreach (var staff in new[] { otherStaff.Id, assignee.Id, otherStaff.Id, assignee.Id })
            Assert.Equal(ManagerServiceRequestOutcome.Success,
                await store.AssignAsync(request.Id, manager.Id, staff, null, sharedTimestamp, default));
        var ambiguousLegacy = Legacy(sharedTimestamp);
        db.ResidentEvents.Add(ambiguousLegacy);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await dispatcher.DispatchAsync(default);
        Assert.Equal("Delivered", (await db.ResidentEvents.SingleAsync(row => row.Id == ambiguousLegacy.Id)).Status);
        Assert.Equal(6, await communication.Notifications.CountAsync(row => row.SourceId == request.Id));

        Assert.Equal(ManagerServiceRequestOutcome.Success,
            await store.AssignAsync(request.Id, manager.Id, otherStaff.Id, null, DateTimeOffset.UtcNow, default));
        otherStaff.Disable(manager.Id, DateTimeOffset.UtcNow);
        await accounts.SaveChangesAsync();
        await dispatcher.DispatchAsync(default);
        Assert.Equal(ManagerServiceRequestOutcome.Success,
            await store.AssignAsync(request.Id, manager.Id, assignee.Id, null, DateTimeOffset.UtcNow, default));
        administration.UserRoleAssignments.Remove(await administration.UserRoleAssignments.SingleAsync(row => row.UserId == assignee.Id));
        await administration.SaveChangesAsync();
        await dispatcher.DispatchAsync(default);
        Assert.Equal(6, await communication.Notifications.CountAsync(row => row.SourceId == request.Id));

        var identity = new Setup(Guid.Empty, owner.Id, resident.Id, assignee.Id, Guid.Empty);
        var staged = await Seed(identity: identity, review: false);
        await dispatcher.DispatchAsync(default);
        var reviewNotice = Assert.Single(await communication.Notifications.AsNoTracking().Where(x => x.SourceId == staged.Id).ToArrayAsync());
        Assert.Equal(manager.Id, reviewNotice.RecipientUserId);
        Assert.Equal("Chi phí phát sinh chờ duyệt", reviewNotice.Title);
        Assert.Equal("409", await Respond(staged, true));
        await Service(db, staged).ApproveAsync(manager.Id, staged.Id, new(staged.Proposal), default);
        await Service(db, staged).ApproveAsync(manager.Id, staged.Id, new(staged.Proposal), default);
        await dispatcher.DispatchAsync(default);
        await dispatcher.DispatchAsync(default);
        var notices = await communication.Notifications.AsNoTracking().Where(x => x.SourceId == staged.Id).ToArrayAsync();
        Assert.Equal(2, notices.Length);
        Assert.Single(notices.Where(x => x.RecipientUserId == owner.Id && x.Title == "Yêu cầu xác nhận chi phí phát sinh"));

        var replacedProposal = await Seed(identity: identity);
        var resolvedProposal = await Seed(identity: identity);
        await Service(db, replacedProposal).ProposeAsync(assignee.Id, replacedProposal.Id,
            new(70000, "Báo giá mới", replacedProposal.Proposal), default);
        db.ChangeTracker.Clear();
        var replaced = await db.ServiceRequests.SingleAsync(x => x.Id == replacedProposal.Id);
        await Service(db, replacedProposal).ApproveAsync(manager.Id, replacedProposal.Id, new(replaced.AdditionalCostProposalId!.Value), default);
        Assert.Equal("OK", await Respond(resolvedProposal, false));
        await dispatcher.DispatchAsync(default);
        await dispatcher.DispatchAsync(default);
        var activeProposal = Assert.Single(await communication.Notifications.AsNoTracking()
            .Where(row => row.SourceId == replacedProposal.Id).ToArrayAsync());
        Assert.Equal(owner.Id, activeProposal.RecipientUserId);
        Assert.Contains("Báo giá mới", activeProposal.Message);
        Assert.False(await communication.Notifications.AnyAsync(row => row.SourceId == resolvedProposal.Id));
        Assert.False(await db.ResidentEvents.AnyAsync(row =>
            (row.SourceId == replacedProposal.Id || row.SourceId == resolvedProposal.Id) && row.Status != "Delivered"));
    }

    private async Task VerifyReassignmentDuringDispatch(ServiceRequestsDbContext worker, ServiceRequestCostNoticeDispatcher dispatcher,
        Guid requestId, Guid manager, Guid currentStaff, Guid temporaryStaff)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = deadline.Token;
        await using var writer = database.CreateDb();
        var store = new ManagerServiceRequestStore(writer);
        Assert.Equal(ManagerServiceRequestOutcome.Success,
            await store.AssignAsync(requestId, manager, temporaryStaff, null, DateTimeOffset.UtcNow, ct));
        await using var transaction = await writer.Database.BeginTransactionAsync(ct);
        Assert.Equal(ManagerServiceRequestOutcome.Success,
            await store.AssignAsync(requestId, manager, currentStaff, null, DateTimeOffset.UtcNow, ct));

        worker.ChangeTracker.Clear();
        await worker.Database.OpenConnectionAsync(ct);
        var workerPid = ((NpgsqlConnection)worker.Database.GetDbConnection()).ProcessID;
        var writerPid = ((NpgsqlConnection)writer.Database.GetDbConnection()).ProcessID;
        var dispatch = dispatcher.DispatchAsync(ct);
        try
        {
            await using var observer = new NpgsqlConnection(worker.Database.GetConnectionString());
            await observer.OpenAsync(ct);
            await using var observe = new NpgsqlCommand("SELECT @writer = ANY(pg_blocking_pids(@worker))", observer);
            observe.Parameters.AddWithValue("writer", writerPid);
            observe.Parameters.AddWithValue("worker", workerPid);
            while (!dispatch.IsCompleted && (bool)(await observe.ExecuteScalarAsync(ct))! == false)
                await Task.Delay(25, ct);
            Assert.False(dispatch.IsCompleted, "Dispatch should wait for the transaction changing its candidate assignment.");
            await transaction.CommitAsync(ct);
            await dispatch;
        }
        finally
        {
            if (!dispatch.IsCompleted)
            {
                await deadline.CancelAsync();
                try { await dispatch; }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            }
            await worker.Database.CloseConnectionAsync();
        }
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
        Assert.Equal(70000, request.ProposedAdditionalCost); Assert.Equal("AWAITING_MANAGER", request.AdditionalCostConsentStatus);
        Assert.NotEqual(setup.Proposal, request.AdditionalCostProposalId);
        Assert.Equal(StaffServiceRequestOutcome.Conflict, await new StaffServiceRequestStore(check).CompleteWorkAsync(setup.Staff,
            setup.Id, "Không được làm theo báo giá chưa duyệt", 70000, "Báo giá thay đổi", DateTimeOffset.UtcNow, default));
    }

    [Fact]
    public async Task ProposalAndNotificationAreCommittedTogetherAndStayVisibleInSqlProjections()
    {
        var setup = await Seed(review: false);
        await using var db = database.CreateDb();
        Assert.Single(await db.ResidentEvents.Where(x => x.SourceId == setup.Id).ToArrayAsync());
        Assert.Equal("AWAITING_MANAGER", (await new StaffServiceRequestStore(db).GetByIdAsync(setup.Staff, setup.Id, default))!.AdditionalCostConsent!.Status);
        Assert.Null((await new ResidentServiceRequestDetailStore(db).GetAsync(setup.Resident, setup.Id, default))!.AdditionalCostConsent);
        Assert.Equal("409", await Respond(setup, true));
        await Service(db, setup).ApproveAsync(Guid.NewGuid(), setup.Id, new(setup.Proposal), default);
        Assert.Equal(2, await db.ResidentEvents.CountAsync(x => x.SourceId == setup.Id));
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
    private async Task<Setup> Seed(bool propose = true, string? serviceArea = null, Setup? identity = null, bool review = true)
    {
        await using var db = database.CreateDb(); var now = DateTimeOffset.UtcNow;
        var setup = identity ?? new Setup(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.Empty);
        var request = new ServiceRequest("YC-" + Guid.NewGuid().ToString("N")[..20], setup.Resident, Guid.NewGuid(), "Sửa thiết bị", "Kiểm tra và đề xuất chi phí", now,
            serviceAreaCode: serviceArea, submittedByUserId: setup.Owner);
        request.MarkUnderReview(now); request.MarkAssigned(now); request.MarkInProgress(now);
        var assignment = new ServiceRequestAssignment(request.Id, setup.Staff, Guid.NewGuid(), now); assignment.Start(now);
        db.AddRange(request, assignment); await db.SaveChangesAsync(); setup = setup with { Id = request.Id };
        if (!propose) return setup;
        var proposal = await Service(db, setup).ProposeAsync(setup.Staff, setup.Id, new(50000, "Thay vật tư"), default);
        if (review) await Service(db, setup).ApproveAsync(Guid.NewGuid(), setup.Id, new(proposal.ProposalId), default);
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
