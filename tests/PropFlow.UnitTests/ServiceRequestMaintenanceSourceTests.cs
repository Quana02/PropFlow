using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Presentation;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-05")]
[Trait("UseCase", "FE-07.4 integration")]
public sealed class ServiceRequestMaintenanceSourceTests
{
    [Fact]
    public async Task GetAsync_ReturnsLinkableReferenceForActiveServiceRequest()
    {
        var now = new DateTimeOffset(2026, 10, 10, 3, 0, 0, TimeSpan.Zero);
        await using var db = Db();
        var request = Request("YC-20261010-LINK", "Rò nước tầng 5", now);
        db.Add(request);
        await db.SaveChangesAsync();

        var result = await new ServiceRequestMaintenanceSource(db)
            .GetAsync(request.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(request.Id, result.Id);
        Assert.Equal("YC-20261010-LINK", result.RequestNumber);
        Assert.Equal("Rò nước tầng 5", result.Title);
        Assert.Equal("SUBMITTED", result.Status);
        Assert.True(result.IsLinkable);
        Assert.Null(result.IneligibilityCode);
    }

    [Fact]
    public async Task GetAsync_ReturnsIneligibleReferenceForTerminalServiceRequest()
    {
        var now = new DateTimeOffset(2026, 10, 10, 3, 0, 0, TimeSpan.Zero);
        await using var db = Db();
        var request = Request("YC-20261010-RESOLVED", "Sự cố đã xử lý", now);
        request.MarkUnderReview(now.AddMinutes(1));
        request.MarkAssigned(now.AddMinutes(2));
        request.Resolve(now.AddMinutes(3));
        db.Add(request);
        await db.SaveChangesAsync();

        var result = await new ServiceRequestMaintenanceSource(db)
            .GetAsync(request.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("RESOLVED", result.Status);
        Assert.False(result.IsLinkable);
        Assert.Equal("service_request_not_linkable", result.IneligibilityCode);
    }

    [Fact]
    public async Task SearchAsync_FindsLinkableRequestsByNumberOrTitleAndExcludesTerminalRequests()
    {
        var now = new DateTimeOffset(2026, 10, 10, 3, 0, 0, TimeSpan.Zero);
        await using var db = Db();
        var numberMatch = Request("YC-20261010-MAYBOM", "Kiểm tra kỹ thuật", now);
        var titleMatch = Request("YC-20261010-OTHER", "Máy bơm MAYBOM phát tiếng ồn", now.AddMinutes(1));
        var closedMatch = Request("YC-20261010-CLOSED", "Máy bơm MAYBOM đã xử lý", now.AddMinutes(2));
        closedMatch.MarkUnderReview(now.AddMinutes(3));
        closedMatch.MarkAssigned(now.AddMinutes(4));
        closedMatch.Resolve(now.AddMinutes(5));
        closedMatch.Close(Guid.NewGuid(), now.AddMinutes(6));
        db.AddRange(numberMatch, titleMatch, closedMatch);
        await db.SaveChangesAsync();

        var results = await new ServiceRequestMaintenanceSource(db)
            .SearchAsync("  maybom  ", 20, CancellationToken.None);

        Assert.Equal([titleMatch.Id, numberMatch.Id], results.Select(item => item.Id));
        Assert.All(results, item => Assert.True(item.IsLinkable));
    }

    private static ServiceRequestsDbContext Db() =>
        new(new DbContextOptionsBuilder<ServiceRequestsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static ServiceRequest Request(string number, string title, DateTimeOffset now) =>
        new(
            number,
            Guid.NewGuid(),
            Guid.NewGuid(),
            title,
            "Mô tả yêu cầu hợp lệ để phục vụ kiểm thử.",
            now,
            apartmentUnitId: Guid.NewGuid(),
            facilityId: Guid.NewGuid(),
            equipmentId: Guid.NewGuid(),
            finalPriorityCode: "URGENT");
}

[Trait("Feature", "FE-05")]
[Trait("UseCase", "FE-07.4 integration")]
public sealed class ManagerServiceRequestsControllerTests
{
    [Fact]
    public void Controller_RequiresManagerServiceRequestPolicy()
    {
        var attribute = Assert.Single(
            typeof(ManagerServiceRequestsController)
                .GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>());

        Assert.Equal(ServiceRequestsAuthorizationPolicies.Manage, attribute.Policy);
    }

    [Fact]
    public async Task Search_ReturnsCandidatesFromPublicContract()
    {
        var expected = Reference();
        var source = new FakeSource(expected);
        var controller = new ManagerServiceRequestsController(source);

        var result = await controller.Search("  máy bơm  ", 10, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal([expected], Assert.IsAssignableFrom<IReadOnlyList<ServiceRequestMaintenanceReference>>(ok.Value));
        Assert.Equal("máy bơm", source.Search);
        Assert.Equal(10, source.Limit);
    }

    private static ServiceRequestMaintenanceReference Reference() =>
        new(
            Guid.NewGuid(),
            "YC-20261010-001",
            "Máy bơm phát tiếng ồn",
            "SUBMITTED",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "URGENT",
            new DateTimeOffset(2026, 10, 10, 3, 0, 0, TimeSpan.Zero),
            true,
            null);

    private sealed class FakeSource(ServiceRequestMaintenanceReference reference)
        : IServiceRequestMaintenanceSource
    {
        public string? Search { get; private set; }
        public int? Limit { get; private set; }

        public Task<ServiceRequestMaintenanceReference?> GetAsync(Guid serviceRequestId, CancellationToken ct) =>
            Task.FromResult<ServiceRequestMaintenanceReference?>(reference);

        public Task<IReadOnlyList<ServiceRequestMaintenanceReference>> SearchAsync(
            string? search,
            int limit,
            CancellationToken ct)
        {
            Search = search;
            Limit = limit;
            return Task.FromResult<IReadOnlyList<ServiceRequestMaintenanceReference>>([reference]);
        }
    }
}
