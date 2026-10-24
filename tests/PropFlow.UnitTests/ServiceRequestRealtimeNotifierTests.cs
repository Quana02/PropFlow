using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Api.Composition;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Presentation.Hubs;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-05")]
public sealed class ServiceRequestRealtimeNotifierTests
{
    [Fact]
    public async Task NotifyChangedAsync_NotifiesManagersResidentAndEveryAssignedStaff()
    {
        await using var serviceRequests = new ServiceRequestsDbContext(
            new DbContextOptionsBuilder<ServiceRequestsDbContext>()
                .UseInMemoryDatabase($"service-requests-{Guid.NewGuid():N}")
                .Options);
        await using var residents = new ResidentsDbContext(
            new DbContextOptionsBuilder<ResidentsDbContext>()
                .UseInMemoryDatabase($"residents-{Guid.NewGuid():N}")
                .Options);

        var now = new DateTimeOffset(2026, 10, 20, 3, 0, 0, TimeSpan.Zero);
        var residentUserId = Guid.NewGuid();
        var resident = new Resident("RES-RT-001", "Cư dân realtime", now, residentUserId);
        var request = new ServiceRequest(
            "YC-RT-001", resident.Id, Guid.NewGuid(),
            "Kiểm tra realtime", "Kiểm tra người nhận SignalR sau khi phân công lại.", now);
        var previousStaffId = Guid.NewGuid();
        var currentStaffId = Guid.NewGuid();
        var previous = new ServiceRequestAssignment(request.Id, previousStaffId, Guid.NewGuid(), now);
        previous.EndAsReassigned(now.AddMinutes(1));
        var current = new ServiceRequestAssignment(request.Id, currentStaffId, Guid.NewGuid(), now.AddMinutes(1));

        residents.Residents.Add(resident);
        serviceRequests.AddRange(request, previous, current);
        await residents.SaveChangesAsync();
        await serviceRequests.SaveChangesAsync();

        var clients = new RecordingHubClients();
        var notifier = new ServiceRequestRealtimeNotifier(
            new RecordingHubContext(clients), serviceRequests, residents,
            NullLogger<ServiceRequestRealtimeNotifier>.Instance);

        await notifier.NotifyChangedAsync(request.Id);

        Assert.Equal(request.Id, Assert.Single(clients.ManagerMessages));
        Assert.Equal(
            new[] { currentStaffId, previousStaffId, residentUserId }.Order(),
            clients.UserRecipients.Select(Guid.Parse).Order());
        Assert.Equal(request.Id, Assert.Single(clients.UserMessages));
    }

    private sealed class RecordingHubContext(RecordingHubClients clients) : IHubContext<ServiceRequestHub>
    {
        public IHubClients Clients { get; } = clients;
        public IGroupManager Groups { get; } = new NoOpGroupManager();
    }

    private sealed class RecordingHubClients : IHubClients
    {
        public List<Guid> ManagerMessages { get; } = [];
        public List<Guid> UserMessages { get; } = [];
        public IReadOnlyList<string> UserRecipients { get; private set; } = [];

        public IClientProxy All => new RecordingClientProxy([]);
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new RecordingClientProxy([]);
        public IClientProxy Client(string connectionId) => new RecordingClientProxy([]);
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new RecordingClientProxy([]);
        public IClientProxy Group(string groupName) => groupName == ServiceRequestHub.ManagerGroup
            ? new RecordingClientProxy(ManagerMessages)
            : new RecordingClientProxy([]);
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new RecordingClientProxy([]);
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => new RecordingClientProxy([]);
        public IClientProxy User(string userId) => Users([userId]);

        public IClientProxy Users(IReadOnlyList<string> userIds)
        {
            UserRecipients = userIds;
            return new RecordingClientProxy(UserMessages);
        }
    }

    private sealed class RecordingClientProxy(List<Guid> messages) : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Assert.Equal(ServiceRequestHub.UpdatedEvent, method);
            messages.Add(Assert.IsType<Guid>(Assert.Single(args)));
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
