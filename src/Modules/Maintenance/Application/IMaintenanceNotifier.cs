using System;
using System.Threading;
using System.Threading.Tasks;

namespace PropFlow.Modules.Maintenance.Application;

public interface IMaintenanceNotifier
{
    Task NotifyTaskUpdatedAsync(Guid taskId, CancellationToken ct = default);
}
