using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Domain.Permissions;
using PropFlow.Modules.Administration.Domain.Roles;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Application;
using PropFlow.Shared.Messaging;

namespace PropFlow.Api.Composition;

public sealed class WorkflowNotificationDelivery(AuthenticationDbContext accounts, AdministrationDbContext administration,
    IFinanceNotificationStore notifications)
{
    public async Task PublishSubmissionAsync(OutboxEvent item, Guid sender, string? residentName, CancellationToken ct)
    {
        // FE-05 sends new requests to the current building's shared management queue.
        var reviewers = await administration.UserRoleAssignments.AsNoTracking()
            .Where(x => x.Role.IsActive && x.Role.Code == SystemRoleCodes.Manager &&
                administration.RolePermissions.Any(p => p.RoleId == x.RoleId && p.Permission.IsActive &&
                    p.Permission.Code == SystemPermissionCodes.ManageOperations))
            .Select(x => x.UserId).Distinct().ToArrayAsync(ct);
        var recipients = await accounts.UserAccounts.AsNoTracking()
            .Where(x => reviewers.Contains(x.Id) && x.Status == AccountStatus.ACTIVE)
            .Select(x => x.Id).ToArrayAsync(ct);
        foreach (var recipient in recipients)
            await notifications.PublishAsync(new(item.Id, recipient, null, "Yêu cầu dịch vụ mới",
                $"Bạn có một yêu cầu mới từ {residentName ?? "cư dân"}.", "", "SERVICE_REQUEST", item.SourceId,
                $"/manager/service-requests/{item.SourceId}", SenderUserId: sender), ct);
    }

    public async Task PublishAssignmentAsync(OutboxEvent item, Guid sender, Guid recipient, string sourceType, string path, CancellationToken ct)
    {
        var managerName = await accounts.UserAccounts.AsNoTracking().Where(x => x.Id == sender)
            .Select(x => x.DisplayName).SingleOrDefaultAsync(ct);
        var author = string.IsNullOrWhiteSpace(managerName) ? "Ban Quản lý" : $"quản lý {managerName}";
        await notifications.PublishAsync(new(item.Id, recipient, null, "Công việc mới được giao",
            $"Bạn có một công việc mới từ {author}.", "", sourceType, item.SourceId, path, SenderUserId: sender), ct);
    }
}
