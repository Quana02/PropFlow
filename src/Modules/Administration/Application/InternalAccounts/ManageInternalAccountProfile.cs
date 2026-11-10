using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Administration.Domain.Roles;
using PropFlow.Modules.Authentication.Contracts;

namespace PropFlow.Modules.Administration.Application.InternalAccounts;

public sealed record ManagedInternalAccountProfile(InternalAccountProfile Account, string Role);

public interface IInternalAccountProfileStore
{
    Task<string?> ReadRoleAsync(Guid userId, bool lockAssignment, CancellationToken ct);
    Task AppendAuditAsync(SystemAuditEvent entry, CancellationToken ct);
}

public sealed class ManageInternalAccountProfile(
    IInternalAccountProfileStore store,
    IInternalAccountProfiles profiles,
    IAdministrationTransaction transaction,
    TimeProvider clock)
{
    public const string AuditAction = "internal_account_profile_updated";
    public const string AuditLabel = "Cập nhật hồ sơ tài khoản nội bộ";

    public async Task<ManagedInternalAccountProfile?> ReadAsync(Guid userId, CancellationToken ct)
    {
        var role = await store.ReadRoleAsync(userId, lockAssignment: false, ct);
        if (!IsInternal(role)) return null;
        var account = await profiles.ReadAsync(userId, ct);
        return account is null ? null : new(account, role!);
    }

    public async Task<ManagedInternalAccountProfile?> UpdateAsync(
        Guid userId, UpdateProfileRequest request, Guid actorId, string traceId, CancellationToken ct)
    {
        ManagedInternalAccountProfile? result = null;
        await transaction.ExecuteAsync(async innerCt =>
        {
            var role = await store.ReadRoleAsync(userId, lockAssignment: true, innerCt);
            if (!IsInternal(role)) return;
            var update = await profiles.UpdateAsync(userId, request, actorId, innerCt);
            if (update is null) return;

            var changes = update.ChangedFields.Select(field => new AuditFieldChange(field,
                "[Đã ẩn nội dung]", "[Đã ẩn nội dung]")).ToArray();
            await store.AppendAuditAsync(new("Administration", AuditAction, AuditLabel, "InternalAccount",
                userId, actorId, "SUCCESS", clock.GetUtcNow(), traceId, changes, 200), innerCt);
            result = new(update.Account, role!);
        }, ct);
        return result;
    }

    private static bool IsInternal(string? role) => role is SystemRoleCodes.Manager or SystemRoleCodes.Staff or SystemRoleCodes.Accountant;
}
