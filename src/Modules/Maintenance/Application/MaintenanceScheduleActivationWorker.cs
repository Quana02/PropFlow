using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PropFlow.Modules.Maintenance.Application;

public sealed class MaintenanceScheduleActivationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<MaintenanceScheduleActivationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ActivateDueSchedulesAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ActivateDueSchedulesAsync(stoppingToken);
    }

    private async Task ActivateDueSchedulesAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<MaintenanceService>().ActivateDueSchedulesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError(exception, "Không thể kích hoạt các lịch bảo trì đến hạn.");
        }
    }
}
