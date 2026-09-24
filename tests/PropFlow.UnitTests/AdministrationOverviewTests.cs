using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;
using PropFlow.Modules.Apartments.Infrastructure;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Infrastructure;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;
using PropFlow.Modules.Reporting.Application.AdministrationOverview;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.Residents.Domain.ResidentApartments;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Infrastructure;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;
using PropFlow.Web.Client.Features.Administration;

namespace PropFlow.UnitTests;

public sealed class AdministrationOverviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 22, 23, 30, 0, TimeSpan.Zero);
    }

    private sealed class FixedApartments(Guid[] ids) : IApartmentOverviewSource
    {
        public Task<IReadOnlyList<Guid>> GetActiveApartmentIdsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Guid>>(ids);
    }

    private sealed class FixedBuildings : IBuildingTimeZones
    {
        public Task<string> GetSystemTimeZoneAsync(CancellationToken ct) =>
            Task.FromResult("Asia/Ho_Chi_Minh");
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
        public DateOnly From { get; private set; }
        public DateOnly Through { get; private set; }
        public Task<ServiceRequestOverviewData> GetOverviewAsync(DateTimeOffset instant, int trendDays,
            string timeZoneId, CancellationToken ct)
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            Through = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
            From = Through.AddDays(-(trendDays - 1));
            return Task.FromResult(new ServiceRequestOverviewData(5,
                [new(nameof(ServiceRequestStatus.SUBMITTED), 5)], [new(Through, 2)]));
        }
    }

    [Fact]
    public async Task Reporting_composes_counts_and_charts_using_building_local_date()
    {
        var residents = new CapturingResidents();
        var requests = new FixedRequests();
        var query = new AdministrationOverviewQuery(new FixedApartments([Guid.NewGuid(), Guid.NewGuid()]),
            new FixedBuildings(), residents, requests, new FixedClock());

        var result = await query.GetAsync(default);

        Assert.Equal(new DateOnly(2026, 9, 23), residents.Date);
        Assert.Equal(new DateOnly(2026, 8, 25), requests.From);
        Assert.Equal(new DateOnly(2026, 9, 23), requests.Through);
        Assert.Equal(2, result.TotalApartments);
        Assert.Equal(1, result.OccupiedApartments);
        Assert.Equal(1, result.VacantApartments);
        Assert.Equal(7, result.TotalResidents);
        Assert.Equal(5, result.OpenServiceRequests);
    }

    [Fact]
    public async Task Building_time_zone_source_throws_when_no_building()
    {
        await using var db = new PropertyAssetsDbContext(new DbContextOptionsBuilder<PropertyAssetsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new BuildingTimeZones(db).GetSystemTimeZoneAsync(default));
    }

    [Fact]
    public async Task Building_time_zone_source_returns_timezone_for_single_building()
    {
        await using var db = new PropertyAssetsDbContext(new DbContextOptionsBuilder<PropertyAssetsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Buildings.Add(new Building("B1", "Building 1", "Address 1", Now, "Asia/Ho_Chi_Minh"));
        await db.SaveChangesAsync();

        var tz = await new BuildingTimeZones(db).GetSystemTimeZoneAsync(default);

        Assert.Equal("Asia/Ho_Chi_Minh", tz);
    }

    [Fact]
    public async Task Building_time_zone_source_throws_when_multiple_buildings()
    {
        await using var db = new PropertyAssetsDbContext(new DbContextOptionsBuilder<PropertyAssetsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Buildings.AddRange(
            new Building("B1", "Building 1", "Address 1", Now, "Asia/Ho_Chi_Minh"),
            new Building("B2", "Building 2", "Address 2", Now, "UTC"));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new BuildingTimeZones(db).GetSystemTimeZoneAsync(default));
    }

    [Fact]
    public async Task Apartments_source_returns_only_active_units()
    {
        await using var db = new ApartmentsDbContext(new DbContextOptionsBuilder<ApartmentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var buildingId = Guid.NewGuid();
        var active = new ApartmentUnit("101", 1, Now);
        var inactive = new ApartmentUnit("102", 1, Now);
        inactive.Deactivate(null, Now);
        db.ApartmentUnits.AddRange(active, inactive);
        await db.SaveChangesAsync();

        var groups = await new ApartmentOverviewSource(db).GetActiveApartmentIdsAsync(default);
        Assert.Equal([active.Id], groups);
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

    [Fact]
    public async Task Requests_source_returns_all_statuses_and_zero_filled_local_daily_trend()
    {
        await using var db = new ServiceRequestsDbContext(new DbContextOptionsBuilder<ServiceRequestsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var statuses = Enum.GetValues<ServiceRequestStatus>();
        var buildingId = Guid.NewGuid();
        foreach (var status in statuses)
        {
            var request = CreateRequest(new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero));
            typeof(ServiceRequest).GetProperty(nameof(ServiceRequest.Status))!.SetValue(request, status);
            db.ServiceRequests.Add(request);
        }
        db.ServiceRequests.Add(CreateRequest(new DateTimeOffset(2026, 9, 20, 18, 30, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();

        var result = await new ServiceRequestOverviewSource(db).GetOverviewAsync(
            new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero), 4,
            "Asia/Ho_Chi_Minh", default);

        Assert.Equal(statuses.Length, result.StatusCounts.Count);
        Assert.Equal(6, result.OpenCount);
        Assert.Equal([0, 1, 7, 0], result.CreatedTrend.Select(item => item.Count));
        Assert.Equal(new DateOnly(2026, 9, 20), result.CreatedTrend[0].Date);
        Assert.Equal(new DateOnly(2026, 9, 23), result.CreatedTrend[^1].Date);
    }

    [Theory]
    [InlineData(null, "overview")]
    [InlineData("overview", "overview")]
    [InlineData("accounts", "accounts")]
    [InlineData("activity", "activity")]
    [InlineData("invalid", "overview")]
    public void Admin_dashboard_tab_falls_back_to_overview(string? requested, string expected) =>
        Assert.Equal(expected, AdminDashboardTabs.Normalize(requested));

    private static ServiceRequest CreateRequest(DateTimeOffset createdAt) =>
        new(Guid.NewGuid().ToString("N"), Guid.NewGuid(), Guid.NewGuid(), "Title", "Description", createdAt);
}
