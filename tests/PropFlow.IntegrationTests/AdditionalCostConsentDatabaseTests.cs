using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Application.WorkAssignedServiceRequests;

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
    private async Task<Setup> Seed()
    {
        await using var db = database.CreateDb(); var now = DateTimeOffset.UtcNow;
        var setup = new Setup(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.Empty);
        var request = new ServiceRequest("YC-" + Guid.NewGuid().ToString("N")[..20], setup.Resident, Guid.NewGuid(), "Sửa thiết bị", "Kiểm tra và đề xuất chi phí", now, submittedByUserId: setup.Owner);
        request.MarkUnderReview(now); request.MarkAssigned(now); request.MarkInProgress(now);
        var assignment = new ServiceRequestAssignment(request.Id, setup.Staff, Guid.NewGuid(), now); assignment.Start(now);
        db.AddRange(request, assignment); await db.SaveChangesAsync(); setup = setup with { Id = request.Id };
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
