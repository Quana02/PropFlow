using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Contracts;

namespace PropFlow.Modules.Apartments.Application;

/// <summary>Apartment-owned add-owner use case. It intentionally cannot transfer or remove other owners.</summary>
public sealed class ApartmentOwnershipCommand(ApartmentsDbContext db, IResidentApartmentReadSource residents, TimeProvider clock) : IApartmentOwnershipCommand
{
    public async Task AddOwnerAsync(Guid apartmentUnitId, Guid residentId, DateOnly effectiveDate, Guid? actorId, CancellationToken cancellationToken)
    {
        IDbContextTransaction? ownedTransaction = null;
        if (db.Database.CurrentTransaction is null)
            ownedTransaction = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var apartment = apartmentUnitId == Guid.Empty
                ? null
                : await db.ApartmentUnits
                    .FromSqlInterpolated($"SELECT * FROM apartments.apartment_units WHERE id = {apartmentUnitId} FOR UPDATE")
                    .SingleOrDefaultAsync(cancellationToken);
            if (apartment is null)
                throw new ApartmentOwnershipException(404, "apartment_not_found", "Không tìm thấy căn hộ.");
            if (apartment.Status != MasterDataStatus.ACTIVE)
                throw new ApartmentOwnershipException(409, "APARTMENT_INACTIVE", "Không thể thêm chủ sở hữu cho căn hộ đang ngừng quản lý.");
            if (residentId == Guid.Empty || !await residents.ResidentExistsAsync(residentId, cancellationToken))
                throw new ApartmentOwnershipException(400, "owner_resident_invalid", "Không tìm thấy hồ sơ cư dân được chọn.");
            if (await db.ApartmentOwnerships.AnyAsync(x => x.ApartmentUnitId == apartmentUnitId && x.OwnerResidentId == residentId && x.EndDate == null, cancellationToken))
                throw new ApartmentOwnershipException(409, "apartment_owner_already_current", "Cư dân này đang là chủ sở hữu hiện tại của căn hộ.");

            db.ApartmentOwnerships.Add(new ApartmentOwnership(apartmentUnitId, residentId, effectiveDate, clock.GetUtcNow(), actorId));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // The database partial unique index is the final authority for concurrent requests.
                throw new ApartmentOwnershipException(409, "apartment_owner_already_current", "Cư dân này đang là chủ sở hữu hiện tại của căn hộ.");
            }

            if (ownedTransaction is not null)
                await ownedTransaction.CommitAsync(cancellationToken);
        }
        finally
        {
            if (ownedTransaction is not null)
                await ownedTransaction.DisposeAsync();
        }
    }

    public async Task EndOwnershipAsync(Guid apartmentUnitId, Guid ownershipId, DateOnly endDate, Guid? actorId, CancellationToken cancellationToken)
    {
        var ownership = await db.ApartmentOwnerships.SingleOrDefaultAsync(
            x => x.Id == ownershipId && x.ApartmentUnitId == apartmentUnitId,
            cancellationToken);
        if (ownership is null)
            throw new ApartmentOwnershipException(404, "apartment_ownership_not_found", "Không tìm thấy quyền sở hữu.");
        if (ownership.EndDate is not null)
            throw new ApartmentOwnershipException(409, "apartment_ownership_not_current", "Quyền sở hữu này đã kết thúc.");
        if (await residents.HasActiveOwnerOccupiedResidencyAsync(ownership.OwnerResidentId, apartmentUnitId,
                DateOnly.FromDateTime(clock.GetLocalNow().DateTime), cancellationToken))
            throw new ApartmentOwnershipException(409, "OWNER_HAS_ACTIVE_OWNER_OCCUPIED_RESIDENCY",
                "Không thể kết thúc quyền sở hữu khi cư dân vẫn đang có quan hệ ‘Chủ sở hữu đang cư trú’ tại căn hộ này. Hãy thay đổi hoặc kết thúc quan hệ cư trú trước.");

        try
        {
            ownership.End(endDate, clock.GetUtcNow(), actorId);
        }
        catch (ArgumentException)
        {
            throw new ApartmentOwnershipException(400, "apartment_ownership_end_date_invalid", "Ngày kết thúc không hợp lệ.");
        }

        try
        {
            var affected = await db.ApartmentOwnerships
                .Where(x => x.Id == ownershipId && x.ApartmentUnitId == apartmentUnitId && x.EndDate == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.EndDate, endDate)
                    .SetProperty(x => x.UpdatedAt, clock.GetUtcNow())
                    .SetProperty(x => x.UpdatedBy, actorId), cancellationToken);
            if (affected == 0)
                throw new ApartmentOwnershipException(409, "apartment_ownership_not_current", "Quyền sở hữu này đã kết thúc.");
        }
        finally
        {
            db.Entry(ownership).State = EntityState.Detached;
        }
    }
}

