using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PropFlow.Modules.Administration.Application.InternalAccounts;
using PropFlow.Modules.Administration.Contracts;

namespace PropFlow.Modules.Administration.Infrastructure.Persistence;

public sealed class InternalAccountProfileStore(AdministrationDbContext db, ISystemAuditWriter audit) : IInternalAccountProfileStore
{
    public Task<string?> ReadRoleAsync(Guid userId, bool lockAssignment, CancellationToken ct)
    {
        var assignments = lockAssignment
            ? db.UserRoleAssignments.FromSqlInterpolated($"""
                SELECT * FROM administration.user_role_assignments WHERE user_id = {userId} FOR UPDATE
                """)
            : db.UserRoleAssignments;
        return assignments.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => x.Role.Code).SingleOrDefaultAsync(ct);
    }

    public Task AppendAuditAsync(SystemAuditEvent entry, CancellationToken ct)
    {
        var transaction = db.Database.CurrentTransaction ?? throw new InvalidOperationException("Profile audit requires a transaction.");
        return audit.AppendInTransactionAsync(entry, db.Database.GetDbConnection(), transaction.GetDbTransaction(), ct);
    }
}
