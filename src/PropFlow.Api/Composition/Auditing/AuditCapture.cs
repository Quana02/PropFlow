using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using PropFlow.Modules.Administration.Contracts;

namespace PropFlow.Api.Composition.Auditing;

public sealed class AuditCapture(IHttpContextAccessor accessor, TimeProvider clock)
{
    private int recordedChanges;
    public int RecordedChanges => Volatile.Read(ref recordedChanges);
    public void Record(int count) => Interlocked.Add(ref recordedChanges, count);
    public HttpContext? Http => accessor.HttpContext;
    public Guid? Actor => Guid.TryParse(Http?.Items["AuthAuditUserId"]?.ToString() ?? Http?.User.FindFirst("sub")?.Value, out var id) ? id : null;

    // Values are opt-in. Other changed fields are named but their contents are never copied.
    private static readonly HashSet<string> VisibleValues = new(StringComparer.Ordinal)
    {
        "Status", "IsActive", "HouseholdRole", "ResidencyType", "RelationshipToHead", "StartDate", "EndDate",
        "UnitNumber", "FloorNumber", "Area", "Bedrooms", "Bathrooms", "DefaultArea", "DefaultBedrooms", "DefaultBathrooms", "UsableAreaM2", "BedroomCount", "BathroomCount", "DefaultUsableAreaM2", "DefaultBedroomCount", "DefaultBathroomCount",
        "ApartmentUnitId", "ResidentId", "HouseholdHeadResidencyId", "AssignedStaffId", "Priority", "PaymentMethod"
    };
    private static readonly HashSet<string> IgnoredFields = new(StringComparer.Ordinal)
        { "Id", "CreatedAt", "UpdatedAt", "CreatedBy", "UpdatedBy", "RowVersion", "Version", "xmin" };

    public SystemAuditEvent? FromEntity(EntityEntry entity)
    {
        var type = entity.Metadata.ClrType;
        var module = type.Namespace?.Split('.').SkipWhile(x => x != "Modules").Skip(1).FirstOrDefault();
        if (module is null || type.Namespace?.Contains(".Domain.", StringComparison.Ordinal) != true) return null;
        // Security records, delivery queues and internal account changes have dedicated event capture.
        if (module == "Authentication" || type.Name.Contains("Outbox", StringComparison.Ordinal)
            || (module == "Administration" && type.Name != "SystemConfiguration")) return null;
        if (entity.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) return null;
        var changes = entity.Properties.Where(p => !IgnoredFields.Contains(p.Metadata.Name)
            && (entity.State != EntityState.Modified || p.IsModified && !Equals(p.OriginalValue, p.CurrentValue)))
            .Select(p => new AuditFieldChange(p.Metadata.Name,
                entity.State == EntityState.Added ? null : SafeValue(p.Metadata.Name, p.OriginalValue),
                entity.State == EntityState.Deleted ? null : SafeValue(p.Metadata.Name, p.CurrentValue))).ToArray();
        if (changes.Length == 0) return null;
        var operation = entity.State switch { EntityState.Added => "created", EntityState.Deleted => "deleted", _ => "updated" };
        var idValue = entity.Properties.FirstOrDefault(p => p.Metadata.Name == "Id")?.CurrentValue;
        return new(module, operation, operation switch { "created" => "Tạo dữ liệu", "deleted" => "Xóa dữ liệu", _ => "Cập nhật dữ liệu" },
            type.Name, idValue is Guid id ? id : null, Actor, "SUCCESS", clock.GetUtcNow(), Http?.TraceIdentifier, changes);
    }

    private static string? SafeValue(string field, object? value) => value is null ? null
        : !VisibleValues.Contains(field) ? "[Đã ẩn nội dung]"
        : value switch { DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), DateTimeOffset d => d.ToUniversalTime().ToString("O"),
            bool b => b ? "Có" : "Không", IFormattable f => f.ToString(null, CultureInfo.InvariantCulture), _ => value.ToString() };
}
