using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PropFlow.Modules.Maintenance.Application;

public interface IMaintenanceNotifier
{
    Task NotifyTaskUpdatedAsync(Guid taskId, IReadOnlyCollection<Guid> recipientStaffUserIds, CancellationToken ct = default);
}
