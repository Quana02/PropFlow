using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Application;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Presentation.Hubs;

namespace PropFlow.Api.Composition;

public sealed class ServiceRequestRealtimeNotifier(
    IHubContext<ServiceRequestHub> hub,
    ServiceRequestsDbContext serviceRequests,
    ResidentsDbContext residents,
    ILogger<ServiceRequestRealtimeNotifier> logger) : IServiceRequestRealtimeNotifier
{
    public async Task NotifyChangedAsync(
        Guid serviceRequestId,
        Guid? residentUserId = null,
        CancellationToken ct = default)
    {
        try
        {
            var request = await serviceRequests.ServiceRequests.AsNoTracking()
                .Where(item => item.Id == serviceRequestId)
                .Select(item => new { item.ResidentId })
                .SingleOrDefaultAsync(ct);
            if (request is null) return;

            residentUserId ??= await residents.Residents.AsNoTracking()
                .Where(resident => resident.Id == request.ResidentId)
                .Select(resident => resident.UserId)
                .SingleOrDefaultAsync(ct);

            // Include assignment history intentionally: after reassignment, the previous Staff must
            // refresh too so the request disappears from their assigned-work list.
            var recipientUserIds = await serviceRequests.ServiceRequestAssignments.AsNoTracking()
                .Where(assignment => assignment.ServiceRequestId == serviceRequestId)
                .Select(assignment => assignment.StaffUserId)
                .Distinct()
                .ToListAsync(ct);
            if (residentUserId is { } userId && userId != Guid.Empty)
                recipientUserIds.Add(userId);

            var managerNotification = hub.Clients.Group(ServiceRequestHub.ManagerGroup)
                .SendAsync(ServiceRequestHub.UpdatedEvent, serviceRequestId, cancellationToken: ct);
            var userNotification = recipientUserIds.Count == 0
                ? Task.CompletedTask
                : hub.Clients.Users(recipientUserIds.Distinct().Select(id => id.ToString()).ToArray())
                    .SendAsync(ServiceRequestHub.UpdatedEvent, serviceRequestId, cancellationToken: ct);

            await Task.WhenAll(managerNotification, userNotification);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The database mutation has already committed; cancellation must not turn it into an API failure.
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Could not publish realtime update for service request {ServiceRequestId}.",
                serviceRequestId);
        }
    }
}
