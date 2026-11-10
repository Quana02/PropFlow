using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Users;

namespace PropFlow.Modules.Authentication.Application;

public sealed partial class AuthUseCases
{
    private async Task<UserAccount> ActiveUserAsync(Guid id, CancellationToken ct)
    {
        var user = await store.UserAsync(id, ct);
        if (user == null || user.Status != AccountStatus.ACTIVE) throw InvalidCredentials();
        return user;
    }

    // FE-01.6
    public async Task<AccountResponse> GetAccountAsync(Guid id, CancellationToken ct) => await AccountAsync(await ActiveUserAsync(id, ct), ct);

    public Task<AccountResponse> UpdateAccountAsync(Guid id, UpdateProfileRequest request, CancellationToken ct) =>
        store.SerializedAsync("user:" + id, async () =>
        {
            var user = await ActiveUserAsync(id, ct);
            await ApplyProfileAsync(user, request, id, ct);
            return await AccountAsync(user, ct);
        }, ct);

    private async Task ApplyProfileAsync(UserAccount user, UpdateProfileRequest request, Guid actorId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            throw new AuthFailure(400, "invalid_profile", "Tên hiển thị không được để trống.");
        var phone = Phone(request.PhoneNumber);
        if (phone is not null && await store.PhoneInUseAsync(phone, user.Id, ct))
            throw new AuthFailure(409, "phone_conflict", "Không thể sử dụng số điện thoại này.");
        user.UpdateProfile(request.DisplayName, phone, actorId, Now);
    }

    // FE-01.5
    public async Task ChangePasswordAsync(Guid id, ChangePasswordRequest request, CancellationToken ct)
    {
        Password(request.NewPassword);
        var success = await store.SerializedAsync("user:" + id, async () =>
        {
            var user = await ActiveUserAsync(id, ct);
            var valid = secrets.VerifyPassword(user, request.CurrentPassword);
            if (!valid || user.IsLockedOutAt(Now))
            {
                if (!valid && !user.IsLockedOutAt(Now)) user.RecordLoginFailure(policy.LockoutFailures, TimeSpan.FromMinutes(policy.LockoutMinutes), Now);
                return false;
            }
            user.ChangePasswordHash(secrets.HashPassword(user, request.NewPassword), id, Now);
            await RevokeAllAsync(id, "password_changed", ct);
            foreach (var reset in await store.RecoveriesAsync(id, ct)) reset.Cancel(Now);
            return true;
        }, ct);
        if (!success) throw new AuthFailure(400, "password_change_failed", "Không thể đổi mật khẩu. Kiểm tra mật khẩu hiện tại hoặc thử lại sau.");
    }
}
