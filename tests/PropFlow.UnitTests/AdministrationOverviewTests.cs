using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;
using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Apartments.Infrastructure;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Reporting.Application.AdministrationOverview;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.Residents.Domain.ResidentApartments;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Infrastructure;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.UnitTests;

public sealed class AdministrationOverviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 22, 23, 30, 0, TimeSpan.Zero);
    }
    private sealed class FixedApartments(Guid building, Guid[] ids) : IApartmentOverviewSource
    {
        public Task<IReadOnlyList<ActiveApartmentIds>> GetActiveApartmentsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ActiveApartmentIds>>([new ActiveApartmentIds(building, ids)]);
    }
    private sealed class FixedBuildings(Guid building) : IBuildingTimeZones
    {
        public Task<IReadOnlyList<BuildingTimeZone>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BuildingTimeZone>>([new BuildingTimeZone(building, "Asia/Ho_Chi_Minh")]);
    }
    private sealed class CapturingResidents : IResidentOverviewSource
    {
        public DateOnly Date { get; private set; }
        public Task<ResidentOverviewCounts> GetCountsAsync(IReadOnlyList<ApartmentOccupancyAtDate> apartments, CancellationToken ct)
        {
            Date = Assert.Single(apartments).Date;
            return Task.FromResult(new ResidentOverviewCounts(7, 1));
        }
    }
    private sealed class FixedRequests : IServiceRequestOverviewSource
    {
        public Task<int> CountOpenAsync(CancellationToken ct) => Task.FromResult(5);
    }

    [Fact]
    public async Task Reporting_composes_source_counts_using_building_local_date()
    {
        var building = Guid.NewGuid();
        var residents = new CapturingResidents();
        var query = new AdministrationOverviewQuery(new FixedApartments(building, [Guid.NewGuid(), Guid.NewGuid()]),
            new FixedBuildings(building), residents, new FixedRequests(), new FixedClock());

        var result = await query.GetAsync(default);

        Assert.Equal(new DateOnly(2026, 9, 23), residents.Date);
        Assert.Equal(2, result.TotalApartments);
        Assert.Equal(1, result.VacantApartments);
        Assert.Equal(7, result.TotalResidents);
        Assert.Equal(5, result.OpenServiceRequests);
    }

    [Fact]
    public async Task Apartments_source_returns_only_active_units()
    {
        await using var db = new ApartmentsDbContext(new DbContextOptionsBuilder<ApartmentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var building = Guid.NewGuid();
        var active = new ApartmentUnit(building, "101", 1, Now);
        var inactive = new ApartmentUnit(building, "102", 1, Now);
        inactive.Deactivate(null, Now);
        db.ApartmentUnits.AddRange(active, inactive);
        await db.SaveChangesAsync();

        var groups = await new ApartmentOverviewSource(db).GetActiveApartmentsAsync(default);
        Assert.Equal(new[] { active.Id }, Assert.Single(groups).ApartmentIds);
    }

    [Fact]
    public async Task Residents_source_counts_active_profiles_and_effective_occupancy_only()
    {
        await using var db = new ResidentsDbContext(new DbContextOptionsBuilder<ResidentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var current = new Resident("R1", "Current", Now);
        var inactive = new Resident("R2", "Inactive", Now);
        inactive.Deactivate(null, Now);
        var movedOut = new Resident("R3", "Moved out", Now);
        typeof(Resident).GetProperty(nameof(Resident.Status))!.SetValue(movedOut, ResidentStatus.MOVED_OUT);
        var linkedAccount = new Resident("R4", "Linked account", Now, userId: Guid.NewGuid());
        db.Residents.AddRange(current, inactive, movedOut, linkedAccount);

        var occupiedApartment = Guid.NewGuid();
        var vacantApartment = Guid.NewGuid();
        var expiredApartment = Guid.NewGuid();
        var futureApartment = Guid.NewGuid();
        var date = new DateOnly(2026, 9, 22);
        db.ResidentApartments.AddRange(
            new ResidentApartment(current.Id, occupiedApartment, "OWNER", date, Now),
            new ResidentApartment(current.Id, expiredApartment, "OWNER", date.AddDays(-10), Now, endDate: date.AddDays(-1)),
            new ResidentApartment(current.Id, futureApartment, "OWNER", date.AddDays(1), Now));
        await db.SaveChangesAsync();

        var counts = await new ResidentOverviewSource(db).GetCountsAsync(
            [new ApartmentOccupancyAtDate(date, [occupiedApartment, vacantApartment, expiredApartment, futureApartment])], default);
        Assert.Equal(2, counts.TotalActiveResidents);
        Assert.Equal(1, counts.OccupiedActiveApartments);
    }

    [Theory]
    [InlineData(ServiceRequestStatus.SUBMITTED, 1)]
    [InlineData(ServiceRequestStatus.UNDER_REVIEW, 1)]
    [InlineData(ServiceRequestStatus.ASSIGNED, 1)]
    [InlineData(ServiceRequestStatus.IN_PROGRESS, 1)]
    [InlineData(ServiceRequestStatus.RESOLVED, 1)]
    [InlineData(ServiceRequestStatus.CLOSED, 0)]
    [InlineData(ServiceRequestStatus.CANCELLED, 0)]
    public async Task Requests_source_counts_only_non_terminal_statuses(ServiceRequestStatus status, int expected)
    {
        await using var db = new ServiceRequestsDbContext(new DbContextOptionsBuilder<ServiceRequestsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var request = new ServiceRequest(Guid.NewGuid().ToString("N"), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Title", "Description", Now);
        typeof(ServiceRequest).GetProperty(nameof(ServiceRequest.Status))!.SetValue(request, status);
        db.ServiceRequests.Add(request);
        await db.SaveChangesAsync();

        Assert.Equal(expected, await new ServiceRequestOverviewSource(db).CountOpenAsync(default));
    }
}
