using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;
using PropFlow.Shared.Messaging;

namespace PropFlow.Modules.Maintenance.Infrastructure.Persistence;
internal static class MaintenanceStatusEvents
{
    public static void Capture(MaintenanceDbContext db)
    {
        db.ChangeTracker.DetectChanges();
        var pending = db.ChangeTracker.Entries<OutboxEvent>().Where(x => x.State == EntityState.Added).Select(x => x.Entity).ToList();
        foreach (var entry in db.ChangeTracker.Entries<MaintenanceTask>().Where(x => x.State == EntityState.Modified).ToArray()) {
            var task = entry.Entity;
            if (task.EquipmentId is null && task.FacilityId is null) continue;
            var previous = entry.Property(x => x.Status).OriginalValue;
            if (previous == task.Status) continue;
            var title = task.Status switch {
                MaintenanceTaskStatus.IN_PROGRESS => "Bắt đầu sửa chữa/bảo trì",
                MaintenanceTaskStatus.COMPLETED => "Công việc sửa chữa/bảo trì đã hoàn thành",
                MaintenanceTaskStatus.CLOSED => "Công việc sửa chữa/bảo trì đã được nghiệm thu",
                MaintenanceTaskStatus.CANCELLED => "Công việc sửa chữa/bảo trì đã được hủy",
                _ => null
            };
            if (title is null) continue;
            var message = $"Công việc {task.TaskNumber}: {title.ToLowerInvariant()}."
                + (task.Status == MaintenanceTaskStatus.COMPLETED ? " Đang chờ ban quản lý nghiệm thu; thông báo hoạt động trở lại sẽ được gửi riêng." : "");
            var payload = JsonSerializer.Serialize(new { Title = title, Message = message, task.EquipmentId, task.FacilityId, PreviousStatus = previous.ToString(), Status = task.Status.ToString() });
            if (pending.Any(x => x.SourceId == task.Id && x.Payload == payload)) continue;
            var notice = new OutboxEvent("MaintenanceWorkStatusChanged", task.Id, payload, DateTimeOffset.UtcNow);
            db.ResidentEvents.Add(notice); pending.Add(notice);
        }
    }
}
