using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.PrivilegedActions;
using PropFlow.Modules.Authentication.Domain.Users;

namespace PropFlow.Modules.Authentication.Application;

public sealed partial class AuthUseCases
{
    private const string ManagedEmailPurpose = "MANAGED_RESIDENT_EMAIL_CHANGE";

    public async Task<ResidentAccountPage> ListResidentAccountsAsync(string? search, int pageIndex, int pageSize, CancellationToken ct)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var page = await residentAccounts.SearchAsync(search, pageIndex, pageSize, ct);
        var ids = page.Items.Where(row => row.UserId.HasValue).Select(row => row.UserId!.Value).ToArray();
        var residentIds = await access.GetResidentUserIdsAsync(ids, ct);
        var accounts = (await store.ReadResidentAccountSummariesAsync(residentIds, ct)).ToDictionary(user => user.Id);
        var now = Now;
        var items = page.Items.Select(row =>
        {
            accounts.TryGetValue(row.UserId.GetValueOrDefault(), out var user);
            var lockedOut = user?.Status == AccountStatus.ACTIVE && user.LockoutUntil > now;
            var status = lockedOut ? "TEMPORARILY_LOCKED" : user?.Status.ToString() ?? (row.UserId.HasValue ? "UNAVAILABLE" : null);
            return new ResidentAccountItem(row.ResidentId, row.ResidentCode, row.FullName, row.ContactEmail,
                row.Apartments, user?.Username, user?.Email, status,
                user?.EmailVerified == true, row.IsActive && user?.Status == AccountStatus.ACTIVE && !lockedOut);
        }).ToArray();
        return new(items, page.TotalCount, page.PageIndex, pageSize);
    }

    public async Task RequestResidentEmailAsync(Guid managerId, Guid residentId, RequestResidentEmailChange request, CancellationToken ct)
    {
        if (!request.IdentityCheckedInPerson || !Enum.IsDefined(request.Reason))
            throw new AuthFailure(400, "identity_check_required", "Cần đối chiếu danh tính trực tiếp và chọn lý do đổi email.");

        var target = EmailChangeTarget(request.NewEmail);
        var userId = await residentAccounts.FindActiveAccountAsync(residentId, ct) ?? throw ResidentAccountUnavailable();
        var outgoing = await store.SerializedAsync("user:" + userId, async () =>
        {
            var user = await store.UserForUpdateAsync(userId, ct);
            if (!await CanManageResidentEmailAsync(managerId, ct))
                throw new AuthFailure(403, "access_unavailable", "Bạn không có quyền yêu cầu đổi email tài khoản cư dân.");
            if (!await IsEligibleResidentAsync(user, residentId, ct))
                throw ResidentAccountUnavailable();
            if (target == user!.Email)
                throw new AuthFailure(400, "email_unchanged", "Vui lòng nhập email khác email hiện tại.");
            if (await store.EmailAsync(target, ct) is not null)
                throw EmailUnavailable();

            var previous = await store.PrivilegedChallengesAsync(userId, ManagedEmailPurpose, ct);
            CheckSendRate(previous.Select(item => item.CreatedAt));
            foreach (var active in previous.Where(item => item.IsActiveAt(Now)))
                active.CloseResidentRequest(Now, "SUPERSEDED");
            var code = secrets.NewOtp();
            var binding = ManagedEmailBinding(user, target, managerId, residentId);
            var challenge = new PrivilegedActionChallenge(userId, ManagedEmailPurpose, secrets.HashToken(binding),
                secrets.BindOtp(code, binding), Now.AddMinutes(policy.OtpMinutes), Now);
            challenge.RecordResidentRequest(managerId, residentId, request.Reason.ToString());
            store.Add(challenge);
            return (challenge.Id, Code: code);
        }, ct);

        try { await email.SendEmailChangeCodeAsync(target, outgoing.Code, policy.OtpMinutes, ct); }
        catch
        {
            await CloseManagedEmailAsync(userId, outgoing.Id, "DELIVERY_FAILED");
            throw;
        }
    }

    public async Task<ResidentEmailChangePending?> PendingResidentEmailAsync(Guid userId, CancellationToken ct)
    {
        var requests = await store.PrivilegedChallengesAsync(userId, ManagedEmailPurpose, ct);
        var current = requests.FirstOrDefault(item => item.IsActiveAt(Now));
        return current is null ? null : new(current.Id, current.ExpiresAt);
    }

    public async Task VerifyResidentEmailAsync(Guid userId, VerifyResidentEmailChange request, CancellationToken ct)
    {
        var target = EmailChangeTarget(request.NewEmail);
        try
        {
            var verified = await store.SerializedAsync("user:" + userId, async () =>
            {
                var user = await store.UserForUpdateAsync(userId, ct);
                var challenge = await store.PrivilegedChallengeAsync(request.ChallengeId, ct);
                if (challenge is null || challenge.UserId != userId || challenge.Purpose != ManagedEmailPurpose ||
                    !challenge.IsActiveAt(Now) || challenge.ResidentId is not { } residentId ||
                    challenge.RequestedBy is not { } managerId) return false;

                if (!await IsEligibleResidentAsync(user, residentId, ct) || !await CanManageResidentEmailAsync(managerId, ct))
                {
                    challenge.CloseResidentRequest(Now, "INELIGIBLE");
                    return false;
                }
                if (!secrets.VerifyPassword(user, request.CurrentPassword))
                {
                    user!.RecordLoginFailure(policy.LockoutFailures, TimeSpan.FromMinutes(policy.LockoutMinutes), Now);
                    challenge.RecordFailedAttempt(Now, policy.OtpAttempts);
                    return false;
                }
                var binding = ManagedEmailBinding(user!, target, managerId, residentId);
                if (challenge.PayloadHash != secrets.HashToken(binding) || !secrets.MatchesBoundOtp(request.Code, challenge.CodeHash, binding))
                {
                    challenge.RecordFailedAttempt(Now, policy.OtpAttempts);
                    return false;
                }
                if (await store.EmailAsync(target, ct) is not null)
                    throw EmailUnavailable();

                user!.ChangeEmail(target, managerId, Now);
                user.MarkEmailVerified(Now);
                challenge.Consume(Now);
                foreach (var reset in await store.RecoveriesAsync(userId, ct)) reset.Cancel(Now);
                foreach (var previous in await store.PrivilegedChallengesAsync(userId, EmailChangePurpose, ct)) previous.Cancel(Now);
                await RevokeAllAsync(userId, "email_changed", ct);
                return true;
            }, ct);
            if (!verified)
                throw new AuthFailure(400, "invalid_challenge", "Mật khẩu, email hoặc mã xác minh không hợp lệ, hoặc yêu cầu đã hết hiệu lực.");
        }
        catch (AuthFailure failure) when (failure.Code == "email_conflict")
        {
            await CloseManagedEmailAsync(userId, request.ChallengeId, "EMAIL_CONFLICT");
            throw;
        }
    }

    private Task CloseManagedEmailAsync(Guid userId, Guid challengeId, string outcome) =>
        store.SerializedAsync("user:" + userId, async () =>
        {
            var challenge = await store.PrivilegedChallengeAsync(challengeId, CancellationToken.None);
            if (challenge?.UserId == userId && challenge.Purpose == ManagedEmailPurpose)
                challenge.CloseResidentRequest(Now, outcome);
            return true;
        }, CancellationToken.None);

    private async Task<bool> CanManageResidentEmailAsync(Guid managerId, CancellationToken ct)
    {
        var manager = await store.UserForUpdateAsync(managerId, ct);
        if (manager?.Status != AccountStatus.ACTIVE || manager.IsLockedOutAt(Now)) return false;
        var grant = await access.GetAsync(managerId, ct);
        return grant?.Role == "MANAGER" && grant.Permissions.Contains(ResidentAccountPolicies.RequestEmailChange);
    }

    private async Task<bool> IsEligibleResidentAsync(UserAccount? user, Guid residentId, CancellationToken ct) =>
        user?.Status == AccountStatus.ACTIVE && !user.IsLockedOutAt(Now) &&
        (await access.GetAsync(user.Id, ct))?.Role == "RESIDENT" &&
        await residentAccounts.LockActiveLinkAsync(residentId, user.Id, ct);

    private string ManagedEmailBinding(UserAccount user, string target, Guid managerId, Guid residentId) =>
        $"{user.Id:N}|{ManagedEmailPurpose}|{managerId:N}|{residentId:N}|{user.Email}|{target}|{secrets.HashToken(user.PasswordHash)}";

    private static AuthFailure ResidentAccountUnavailable() =>
        new(404, "resident_account_unavailable", "Không tìm thấy tài khoản cư dân đủ điều kiện.");

    private static AuthFailure EmailUnavailable() => new(409, "email_conflict", "Không thể sử dụng email này.");
}
