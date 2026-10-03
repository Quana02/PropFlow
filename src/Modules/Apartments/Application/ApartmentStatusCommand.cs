using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Contracts;

namespace PropFlow.Modules.Apartments.Application;

public interface IApartmentStatusCommand
{
    Task SetAsync(Guid apartmentUnitId, MasterDataStatus targetStatus, DateOnly effectiveDate, Guid? actorId, CancellationToken cancellationToken);
}

public sealed class ApartmentStatusCommand(
    ApartmentsDbContext db,
    IResidentApartmentReadSource residents,
    TimeProvider clock) : IApartmentStatusCommand
{
    public async Task SetAsync(Guid apartmentUnitId, MasterDataStatus targetStatus, DateOnly effectiveDate, Guid? actorId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var apartment = await db.ApartmentUnits
            .FromSqlInterpolated($"SELECT * FROM apartments.apartment_units WHERE id = {apartmentUnitId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (apartment is null)
            throw new ApartmentStatusException(404, "APARTMENT_NOT_FOUND", "Không tìm thấy căn hộ.");

        if (targetStatus == MasterDataStatus.INACTIVE)
        {
            if (await db.ApartmentOwnerships.AnyAsync(
                    x => x.ApartmentUnitId == apartmentUnitId && x.EndDate == null,
                    cancellationToken))
                throw new ApartmentStatusException(
                    409,
                    "APARTMENT_HAS_CURRENT_OWNERS",
                    "Không thể ngừng quản lý căn hộ vì vẫn còn chủ sở hữu hiện hành.");

            if (await residents.HasActiveResidenciesAsync(apartmentUnitId, effectiveDate, cancellationToken))
                throw new ApartmentStatusException(
                    409,
                    "APARTMENT_HAS_ACTIVE_RESIDENTS",
                    "Căn hộ vẫn còn cư dân đang cư trú. Hãy kết thúc hoặc chuyển các quan hệ cư trú trước.");
        }

        if (targetStatus == MasterDataStatus.ACTIVE)
            apartment.Activate(actorId, clock.GetUtcNow());
        else
            apartment.Deactivate(actorId, clock.GetUtcNow());

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}

public sealed class ApartmentStatusException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
