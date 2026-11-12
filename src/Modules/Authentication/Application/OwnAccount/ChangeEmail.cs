using System.Net.Mail;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.PrivilegedActions;
using PropFlow.Modules.Authentication.Domain.Users;

namespace PropFlow.Modules.Authentication.Application;

public sealed partial class AuthUseCases
{
    // Preserve the stored purpose so pending codes and resend limits survive deployment.
    private const string EmailChangePurpose = "ADMIN_EMAIL_CHANGE";

    // FE-01.6: change the authenticated user's own account email.
    public async Task<ChallengeResponse> RequestEmailChangeAsync(Guid id, ChangeEmailRequest request, CancellationToken ct)
    {
        var target = EmailChangeTarget(request.NewEmail);
        var outgoing = await store.SerializedAsync("user:" + id, async () =>
        {
            var user = await EmailChangeUserAsync(id, ct);
            var valid = secrets.VerifyPassword(user, request.CurrentPassword);
            if (!valid || user.IsLockedOutAt(Now))
            {
                if (!valid && !user.IsLockedOutAt(Now))
                    user.RecordLoginFailure(policy.LockoutFailures, TimeSpan.FromMinutes(policy.LockoutMinutes), Now);
                return null;
            }
            if (target == user.Email) throw new AuthFailure(400, "email_unchanged", "Vui lòng nhập email khác email hiện tại.");
            if (await store.EmailAsync(target, ct) is not null)
                throw new AuthFailure(409, "email_conflict", "Không thể sử dụng email này.");

            var previous = await store.PrivilegedChallengesAsync(id, EmailChangePurpose, ct);
            CheckSendRate(previous.Select(item => item.CreatedAt));
            foreach (var active in previous.Where(item => item.IsActiveAt(Now))) active.Cancel(Now);
            var code = secrets.NewOtp();
            var binding = EmailChangeBinding(user, target);
            var challenge = new PrivilegedActionChallenge(id, EmailChangePurpose, secrets.HashToken(binding),
                secrets.BindOtp(code, binding), Now.AddMinutes(policy.OtpMinutes), Now);
            store.Add(challenge);
            return new OutgoingChallenge(new(challenge.Id, challenge.ExpiresAt, Now.AddSeconds(policy.ResendSeconds),
                "Vui lòng nhập mã xác minh đã gửi đến email mới. Email hiện tại chưa thay đổi."), target, code);
        }, ct);
        if (outgoing is null)
            throw new AuthFailure(400, "email_change_failed", "Không thể đổi email. Kiểm tra mật khẩu hiện tại hoặc thử lại sau.");

        try { await email.SendEmailChangeCodeAsync(outgoing.Address!, outgoing.Code!, policy.OtpMinutes, ct); }
        catch
        {
            // Delivery did not enter the queue: do not leave a usable challenge behind.
            await store.SerializedAsync("user:" + id, async () =>
            {
                (await store.PrivilegedChallengeAsync(outgoing.Response.ChallengeId, CancellationToken.None))?.Cancel(Now);
                return true;
            }, CancellationToken.None);
            throw;
        }
        return outgoing.Response;
    }

    public async Task VerifyEmailChangeAsync(Guid id, VerifyEmailChangeRequest request, CancellationToken ct)
    {
        var target = EmailChangeTarget(request.NewEmail);
        var success = await store.SerializedAsync("user:" + id, async () =>
        {
            var user = await EmailChangeUserAsync(id, ct);
            if (user.IsLockedOutAt(Now)) return false;
            var challenge = await store.PrivilegedChallengeAsync(request.ChallengeId, ct);
            if (challenge is null || challenge.UserId != id || challenge.Purpose != EmailChangePurpose ||
                !challenge.IsActiveAt(Now) || challenge.AttemptCount >= policy.OtpAttempts) return false;

            // Binding invalidates requests if either email or the password changes in the meantime.
            var binding = EmailChangeBinding(user, target);
            if (challenge.PayloadHash != secrets.HashToken(binding) || !secrets.MatchesBoundOtp(request.Code, challenge.CodeHash, binding))
            {
                challenge.RecordFailedAttempt(Now, policy.OtpAttempts);
                return false;
            }
            if (await store.EmailAsync(target, ct) is not null)
                throw new AuthFailure(409, "email_conflict", "Không thể sử dụng email này.");

            user.ChangeEmail(target, id, Now);
            user.MarkEmailVerified(Now);
            challenge.Consume(Now);
            foreach (var reset in await store.RecoveriesAsync(id, ct)) reset.Cancel(Now);
            foreach (var other in await store.PrivilegedChallengesAsync(id, EmailChangePurpose, ct)) other.Cancel(Now);
            await RevokeAllAsync(id, "email_changed", ct);
            return true;
        }, ct);
        if (!success) throw InvalidChallenge();
    }

    private async Task<UserAccount> EmailChangeUserAsync(Guid id, CancellationToken ct)
    {
        // Serialize the eligibility check with account-status writes until the email transaction commits.
        var user = await store.UserForUpdateAsync(id, ct);
        if (user is null || user.Status != AccountStatus.ACTIVE)
            throw InvalidCredentials();
        var grant = await access.GetAsync(id, ct);
        if (grant?.Role is not ("ADMIN" or "MANAGER" or "STAFF" or "ACCOUNTANT"))
            throw new AuthFailure(403, "access_unavailable", "Tài khoản chưa được cấp quyền truy cập. Vui lòng liên hệ quản trị viên.");
        return user;
    }

    private string EmailChangeBinding(UserAccount user, string target) =>
        $"{user.Id:N}|{EmailChangePurpose}|{user.Email}|{target}|{secrets.HashToken(user.PasswordHash)}";

    private static string EmailChangeTarget(string value)
    {
        var target = value.Trim();
        if (target.Length > 255 || !MailAddress.TryCreate(target, out var parsed) || parsed.Address != target)
            throw new AuthFailure(400, "invalid_email", "Email mới không hợp lệ.");
        return NormalizeEmail(target);
    }
}
