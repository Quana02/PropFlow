using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Maintenance.Application;
using PropFlow.Modules.Maintenance.Domain.MaintenanceAssignments;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;

namespace PropFlow.Modules.Maintenance.Presentation.Hubs;

public class MaintenanceNotifier(IHubContext<MaintenanceHub> hubContext, MaintenanceDbContext db) : IMaintenanceNotifier
{
    public Task NotifyAssetStatusChangedAsync(CancellationToken ct = default) =>
        hubContext.Clients.Group(MaintenanceHub.AssetStatusGroup)
            .SendAsync("AssetStatusChanged", cancellationToken: ct);

    public async Task NotifyTaskUpdatedAsync(Guid taskId, IReadOnlyCollection<Guid> recipientStaffUserIds, CancellationToken ct = default)
    {
        var candidates = recipientStaffUserIds.Where(userId => userId != Guid.Empty).Distinct().ToArray();
        var currentStaff = candidates.Length == 0 ? [] : await db.MaintenanceAssignments.AsNoTracking()
            .Where(assignment => assignment.MaintenanceTaskId == taskId && candidates.Contains(assignment.StaffUserId)
                && (assignment.Status == AssignmentStatus.ASSIGNED || assignment.Status == AssignmentStatus.IN_PROGRESS
                    || assignment.Status == AssignmentStatus.COMPLETED))
            .Select(assignment => assignment.StaffUserId).Distinct().ToArrayAsync(ct);
        var recipientGroups = currentStaff.Select(MaintenanceHub.UserUpdatesGroup).ToArray();
        var removedGroups = candidates.Except(currentStaff).Select(MaintenanceHub.UserUpdatesGroup).ToArray();

        // Task groups serve manager detail pages. Staff receive only events addressed to their own group.
        var groupNotification = hubContext.Clients.Group(MaintenanceHub.TaskGroup(taskId))
            .SendAsync("MaintenanceTaskUpdated", taskId, cancellationToken: ct);
        var recipientNotification = recipientGroups.Length == 0
            ? Task.CompletedTask
            : hubContext.Clients.Groups(recipientGroups)
                .SendAsync("MaintenanceTaskUpdated", taskId, cancellationToken: ct);

        var assignmentNotification = removedGroups.Length == 0
            ? Task.CompletedTask
            : hubContext.Clients.Groups(removedGroups).SendAsync("MaintenanceAssignmentsChanged", cancellationToken: ct);

        await Task.WhenAll(groupNotification, recipientNotification, assignmentNotification);
    }
}
