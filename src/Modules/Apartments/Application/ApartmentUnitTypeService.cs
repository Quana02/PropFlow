using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;

namespace PropFlow.Modules.Apartments.Application;

public sealed class ApartmentUnitTypeService(ApartmentsDbContext db, TimeProvider clock)
{
    public async Task<IReadOnlyList<ApartmentUnitType>> ListAsync(CancellationToken ct) =>
        await db.ApartmentUnitTypes.AsNoTracking().OrderBy(x => x.Name).ToArrayAsync(ct);

    public async Task<ApartmentUnitType> SaveAsync(Guid? id, string name, decimal? area, int? bedrooms, int? bathrooms, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80)
            throw new ApartmentUnitTypeException(400, "APARTMENT_UNIT_TYPE_NAME_INVALID", "Tên loại căn hộ là bắt buộc và tối đa 80 ký tự.");
        var normalized = name.Trim().ToLowerInvariant();
        if (await db.ApartmentUnitTypes.AnyAsync(x => x.Id != id && x.Name.Trim().ToLower() == normalized, ct)) throw Duplicate();
        var type = id.HasValue ? await db.ApartmentUnitTypes.SingleOrDefaultAsync(x => x.Id == id, ct) : null;
        if (id.HasValue && type is null) throw Missing();
        try
        {
            if (type is null)
            {
                type = new ApartmentUnitType(name, clock.GetUtcNow(), area, bedrooms, bathrooms);
                db.ApartmentUnitTypes.Add(type);
            }
            else
            {
                type.UpdateDefaults(area, bedrooms, bathrooms, clock.GetUtcNow());
                type.Rename(name, clock.GetUtcNow());
            }
        }
        catch (ArgumentException exception) { throw new ApartmentUnitTypeException(400, "APARTMENT_UNIT_TYPE_DEFAULTS_INVALID", exception.Message); }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) { throw Duplicate(); }
        return type;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var type = await db.ApartmentUnitTypes.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
        if (await db.ApartmentUnits.AnyAsync(x => x.ApartmentUnitTypeId == id, ct)) throw InUse();
        db.ApartmentUnitTypes.Remove(type);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation }) { throw InUse(); }
    }

    private static ApartmentUnitTypeException Duplicate() => new(409, "APARTMENT_UNIT_TYPE_DUPLICATE", "Tên loại căn hộ đã tồn tại.");
    private static ApartmentUnitTypeException InUse() => new(409, "APARTMENT_TYPE_IN_USE", "Loại căn hộ đang được sử dụng, không thể xóa.");
    private static ApartmentUnitTypeException Missing() => new(404, "APARTMENT_UNIT_TYPE_NOT_FOUND", "Không tìm thấy loại căn hộ.");
}

public sealed class ApartmentUnitTypeException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
