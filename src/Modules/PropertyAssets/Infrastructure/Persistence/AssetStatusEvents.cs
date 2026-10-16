using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;
using PropFlow.Shared.Messaging;
using EquipmentEntity = PropFlow.Modules.PropertyAssets.Domain.Equipment.Equipment;

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;
internal static class AssetStatusEvents
{
    public static void Capture(PropertyAssetsDbContext db)
    {
        db.ChangeTracker.DetectChanges();
        var pending = db.ChangeTracker.Entries<OutboxEvent>().Where(x => x.State == EntityState.Added).Select(x => x.Entity).ToList();
        foreach (var entry in db.ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified).ToArray()) {
            if (entry.Entity is not (EquipmentEntity or Facility)) continue;
            var status = entry.Property("Status");
            var current = status.CurrentValue!.ToString()!;
            var previous = entry.State == EntityState.Added ? null : status.OriginalValue!.ToString();
            var newFacility = entry.State == EntityState.Added && entry.Entity is Facility;
            if (previous == current || previous is null && current == "ACTIVE" && !newFacility) continue;
            var equipment = entry.Entity as EquipmentEntity; var facility = entry.Entity as Facility;
            var id = equipment?.Id ?? facility!.Id;
            var name = equipment?.Name ?? facility!.Name;
            var code = equipment?.Code ?? facility!.Code;
            var location = equipment?.LocationDescription ?? facility?.LocationDescription;
            var kind = equipment is null ? "Tiện ích công cộng" : "Thiết bị";
            var label = current switch { "ACTIVE" => "đã hoạt động trở lại", "UNDER_MAINTENANCE" => "đang sửa chữa/bảo trì", "OUT_OF_SERVICE" => "đã ngừng phục vụ", "UNAVAILABLE" => "tạm thời không khả dụng", _ => "tạm ngừng hoạt động" };
            var title = newFacility ? $"Tiện ích công cộng mới: {name}" : $"{kind} {name} {label}";
            if (title.Length > 200) title = title[..197] + "…";
            var description = newFacility
                ? $"đã được bổ sung. Trạng thái hiện tại: {(current == "ACTIVE" ? "đang hoạt động" : label)}"
                : label;
            var message = $"Ban quản lý thông báo: {kind.ToLowerInvariant()} {name} ({code}){(string.IsNullOrWhiteSpace(location) ? "" : ", vị trí " + location)} {description}."
                + (current == "ACTIVE" ? " Cư dân có thể sử dụng theo quy định." : " Vui lòng lưu ý và sử dụng phương án thay thế nếu cần.");
            var payload = JsonSerializer.Serialize(new { Title = title, Message = message, PreviousStatus = previous, Status = current });
            if (pending.Any(x => x.SourceId == id && x.Payload == payload)) continue;
            var notice = new OutboxEvent("AssetAvailabilityChanged", id, payload, DateTimeOffset.UtcNow);
            db.ResidentEvents.Add(notice); pending.Add(notice);
        }
    }
}
