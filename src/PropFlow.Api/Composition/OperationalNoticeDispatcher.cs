using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Application;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;
using PropFlow.Shared.Messaging;

namespace PropFlow.Api.Composition;
public sealed class OperationalNoticeDispatcher(PropertyAssetsDbContext assets, MaintenanceDbContext maintenance,
    AuthenticationDbContext accounts, AdministrationDbContext administration, IFinanceNotificationStore notifications,
    IMaintenanceAssetSource assetNames, ILogger<OperationalNoticeDispatcher> logger, TimeProvider? clock = null,
    WorkflowNotificationDelivery? workflow = null, IServiceScopeFactory? scopes = null)
{
    private DateTimeOffset Now => (clock ?? TimeProvider.System).GetUtcNow();
    public Task DispatchAssetsAsync(CancellationToken ct) => DispatchAsync(assets, assets.ResidentEvents, 71241004, ct);
    public Task DispatchMaintenanceAsync(CancellationToken ct) => DispatchAsync(maintenance, maintenance.ResidentEvents, 71241005, ct);
    private async Task DispatchAsync(DbContext db, DbSet<OutboxEvent> events, int lockId, CancellationToken ct)
    {
        // The source outbox and Communication may share the scoped connection.
        // Use a separate scope for idempotent delivery while holding the source lock.
        using var deliveryScope = scopes?.CreateScope();
        var deliveryServices = deliveryScope?.ServiceProvider;
        var publisher = deliveryServices?.GetRequiredService<IFinanceNotificationStore>() ?? notifications;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockId})", ct);
        var now = Now;
        var pending = await events.Where(x => x.Status == "Pending" && x.NextAttemptAt <= now).OrderBy(x => x.CreatedAt).Take(10).ToArrayAsync(ct);
        foreach (var item in pending) {
            try {
                if (item.Version == 1 && item.Name == MaintenanceAssignmentNotice.EventName)
                {
                    var notice = JsonSerializer.Deserialize<MaintenanceAssignmentNotice>(item.Payload)!;
                    var delivery = deliveryServices?.GetRequiredService<WorkflowNotificationDelivery>() ?? workflow
                        ?? throw new InvalidOperationException("Workflow notification delivery is unavailable.");
                    await delivery.PublishAssignmentAsync(item, notice.ManagerUserId, notice.StaffUserId,
                        "MAINTENANCE", $"/my-maintenance?taskId={item.SourceId}", ct);
                    item.Delivered(Now);
                    continue;
                }
                if (item.Version != 1 || item.Name is not ("AssetAvailabilityChanged" or "MaintenanceWorkStatusChanged"))
                    throw new InvalidOperationException("Unsupported operational notification event.");
                using var document = JsonDocument.Parse(item.Payload);
                var payload = document.RootElement;
                var title = payload.GetProperty("Title").GetString()!;
                var message = payload.GetProperty("Message").GetString()!;
                var maintenanceNotice = item.Name == "MaintenanceWorkStatusChanged";
                if (maintenanceNotice) {
                    var equipmentId = payload.GetProperty("EquipmentId"); var facilityId = payload.GetProperty("FacilityId");
                    var asset = equipmentId.ValueKind == JsonValueKind.String ? await assetNames.GetEquipmentAsync(equipmentId.GetGuid(), ct)
                        : facilityId.ValueKind == JsonValueKind.String ? await assetNames.GetFacilityAsync(facilityId.GetGuid(), ct) : null;
                    if (asset is not null) message = $"{asset.Name} ({asset.Code}){(string.IsNullOrWhiteSpace(asset.LocationDescription) ? "" : ", vị trí " + asset.LocationDescription)}. " + message;
                }
                await ResidentBroadcastDelivery.PublishAsync(
                    deliveryServices?.GetRequiredService<AuthenticationDbContext>() ?? accounts,
                    deliveryServices?.GetRequiredService<AdministrationDbContext>() ?? administration,
                    publisher, item.Id, item.SourceId,
                    maintenanceNotice ? "MAINTENANCE" : "ASSET_STATUS", title, message, ct);
                item.Delivered(Now);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) {
                item.Failed(ex.GetType().Name, Now);
                logger.LogWarning("Operational event {EventId}: {Status}, attempt {Attempt} ({ErrorType}).", item.Id, item.Status, item.Attempts, ex.GetType().Name);
            }
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
