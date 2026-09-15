using PropFlow.Modules.Authentication.Domain.Tokens;
using PropFlow.Modules.Authentication.Domain.ResidentVerifications;
using PropFlow.Modules.Authentication.Domain.Users;
using System.Reflection;

namespace PropFlow.UnitTests;

public class AuthenticationTests
{
    private readonly DateTimeOffset _now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void UserAccount_Constructor_GeneratesId_AndSetsDefaults()
    {
        var user = new UserAccount("john.doe", "hash123", "John Doe", _now, "john@example.com", "0912345678", AccountStatus.ACTIVE);

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("john.doe", user.Username);
        Assert.Equal("JOHN@EXAMPLE.COM", user.Email);
        Assert.Equal("0912345678", user.PhoneNumber);
        Assert.Equal("hash123", user.PasswordHash);
        Assert.Equal("John Doe", user.DisplayName);
        Assert.Equal(AccountStatus.ACTIVE, user.Status);
        Assert.False(user.EmailVerified);
        Assert.Equal(0, user.FailedLoginCount);
        Assert.Null(user.LockoutUntil);
        Assert.Equal(_now, user.CreatedAt);
    }

    [Fact]
    public void UserAccount_Constructor_DoesNotExposePhoneVerification()
    {
        var publicConstructors = typeof(UserAccount).GetConstructors();

        Assert.All(publicConstructors, ctor =>
        {
            Assert.DoesNotContain(ctor.GetParameters(), parameter => parameter.Name == "phoneVerified");
        });

        Assert.Null(typeof(UserAccount).GetProperty("PhoneVerified"));
        Assert.Null(typeof(UserAccount).GetMethod("MarkPhoneVerified", BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void UserAccount_MarkEmailVerified_IsDeterministic()
    {
        var user = new UserAccount("john.doe", "hash123", "John Doe", _now, "john@example.com");
        var verifiedAt = _now.AddMinutes(5);

        user.MarkEmailVerified(verifiedAt);

        Assert.True(user.EmailVerified);
        Assert.Equal(verifiedAt, user.UpdatedAt);
    }

    [Fact]
    public void UserAccount_ChangeEmail_ResetsVerifiedState_WhenEmailChanges()
    {
        var user = new UserAccount("john.doe", "hash123", "John Doe", _now, "john@example.com");
        var actor = Guid.NewGuid();
        var verifiedAt = _now.AddMinutes(1);
        user.MarkEmailVerified(verifiedAt);

        var changedAt = _now.AddMinutes(2);
        user.ChangeEmail("  john.new@example.com  ", actor, changedAt);

        Assert.Equal("JOHN.NEW@EXAMPLE.COM", user.Email);
        Assert.False(user.EmailVerified);
        Assert.Equal(actor, user.UpdatedBy);
        Assert.Equal(changedAt, user.UpdatedAt);
    }

    [Fact]
    public void UserAccount_ChangeEmail_KeepsVerifiedState_WhenNormalizedEmailIsSame()
    {
        var user = new UserAccount("john.doe", "hash123", "John Doe", _now, "john@example.com");
        user.MarkEmailVerified(_now.AddMinutes(1));

        user.ChangeEmail(" JOHN@example.com ", updatedBy: null, now: _now.AddMinutes(2));

        Assert.Equal("JOHN@EXAMPLE.COM", user.Email);
        Assert.True(user.EmailVerified);
    }

    [Fact]
    public void UserAccount_LockoutLogic_IsDeterministic()
    {
        var user = new UserAccount("john.doe", "hash123", "John Doe", _now, "john@example.com");

        // Record 4 failed logins (threshold 5)
        for (int i = 0; i < 4; i++)
        {
            user.RecordLoginFailure(maxAllowedFailures: 5, lockoutDuration: TimeSpan.FromMinutes(15), _now.AddMinutes(i));
            Assert.False(user.IsLockedOutAt(_now.AddMinutes(i)));
        }

        // 5th failed login triggers lockout for 15 minutes
        var lockTime = _now.AddMinutes(4);
        user.RecordLoginFailure(maxAllowedFailures: 5, lockoutDuration: TimeSpan.FromMinutes(15), lockTime);
        Assert.True(user.IsLockedOutAt(lockTime));
        Assert.True(user.IsLockedOutAt(lockTime.AddMinutes(14)));
        Assert.False(user.IsLockedOutAt(lockTime.AddMinutes(15))); // Lockout expired

        // Reset via successful login
        user.RecordLoginSuccess("127.0.0.1", lockTime.AddMinutes(16));
        Assert.Equal(0, user.FailedLoginCount);
        Assert.Null(user.LockoutUntil);
        Assert.False(user.IsLockedOutAt(lockTime.AddMinutes(16)));
    }

    [Fact]
    public void UserAccount_StatusMethods_WorkWithAuditInfo()
    {
        var user = new UserAccount("john.doe", "hash123", "John Doe", _now, "john@example.com");
        var actor = Guid.NewGuid();
        var lockTime = _now.AddHours(1);

        user.Lock(actor, lockTime);
        Assert.Equal(AccountStatus.LOCKED, user.Status);
        Assert.Equal(actor, user.UpdatedBy);
        Assert.Equal(lockTime, user.UpdatedAt);

        var suspendTime = lockTime.AddHours(1);
        user.Suspend(actor, suspendTime);
        Assert.Equal(AccountStatus.SUSPENDED, user.Status);
        Assert.Equal(suspendTime, user.UpdatedAt);

        var disableTime = suspendTime.AddHours(1);
        user.Disable(actor, disableTime);
        Assert.Equal(AccountStatus.DISABLED, user.Status);
        Assert.Equal(disableTime, user.UpdatedAt);

        var activateTime = disableTime.AddHours(1);
        user.Activate(actor, activateTime);
        Assert.Equal(AccountStatus.ACTIVE, user.Status);
        Assert.Equal(activateTime, user.UpdatedAt);
    }

    [Fact]
    public void UserAccount_PublicApi_DoesNotExposeGenericStatusMutation()
    {
        var publicMethods = typeof(UserAccount)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name);

        Assert.DoesNotContain("SetStatus", publicMethods);
        Assert.DoesNotContain("ChangeStatus", publicMethods);
        Assert.DoesNotContain("UpdateStatus", publicMethods);
    }

    [Fact]
    public void RefreshToken_Revoke_TransitionsCorrectly_AndSetsFields()
    {
        var userId = Guid.NewGuid();
        var expiresAt = _now.AddDays(7);
        var token = new RefreshToken(userId, "tokenhash", expiresAt, _now);

        Assert.NotEqual(Guid.Empty, token.Id);
        Assert.True(token.IsActiveAt(_now));
        Assert.False(token.IsExpiredAt(_now));
        Assert.True(token.IsExpiredAt(_now.AddDays(8)));

        var revokeTime = _now.AddDays(1);
        token.Revoke("User logout", revokeTime);

        Assert.False(token.IsActiveAt(revokeTime));
        Assert.Equal("User logout", token.RevokedReason);
        Assert.Equal(revokeTime, token.RevokedAt);
    }

    [Fact]
    public void PasswordResetToken_MarkUsed_TransitionsCorrectly_AndValidatesState()
    {
        var userId = Guid.NewGuid();
        var expiresAt = _now.AddHours(1);
        var resetToken = new PasswordResetToken(userId, "resethash", expiresAt, _now);

        Assert.NotEqual(Guid.Empty, resetToken.Id);
        Assert.True(resetToken.IsActiveAt(_now));
        Assert.False(resetToken.IsUsed);

        // Mark used
        var usedTime = _now.AddMinutes(30);
        resetToken.MarkUsed(usedTime);
        Assert.True(resetToken.IsUsed);
        Assert.Equal(usedTime, resetToken.UsedAt);
        Assert.False(resetToken.IsActiveAt(usedTime));

        // Attempting to mark used again throws InvalidOperationException
        Assert.Throws<InvalidOperationException>(() => resetToken.MarkUsed(usedTime.AddMinutes(5)));
    }

    [Fact]
    public void PasswordResetToken_CannotMarkUsed_WhenExpired()
    {
        var userId = Guid.NewGuid();
        var expiresAt = _now.AddHours(1);
        var resetToken = new PasswordResetToken(userId, "resethash", expiresAt, _now);

        var expiredTime = _now.AddHours(2);
        Assert.True(resetToken.IsExpiredAt(expiredTime));
        Assert.Throws<InvalidOperationException>(() => resetToken.MarkUsed(expiredTime));
    }

    [Fact]
    public void ResidentVerification_Constructor_CreatesPendingEmailOtpChallenge()
    {
        var userId = Guid.NewGuid();
        var residentId = Guid.NewGuid();
        var apartmentUnitId = Guid.NewGuid();
        var expiresAt = _now.AddDays(3);
        var verification = new ResidentVerification(userId, residentId, "otp-hash", expiresAt, _now, apartmentUnitId);

        Assert.NotEqual(Guid.Empty, verification.Id);
        Assert.Equal(userId, verification.UserId);
        Assert.Equal(residentId, verification.ResidentId);
        Assert.Equal(apartmentUnitId, verification.ApartmentUnitId);
        Assert.Equal("otp-hash", verification.VerificationCodeHash);
        Assert.Equal(expiresAt, verification.ExpiresAt);
        Assert.Equal(VerificationStatus.PENDING, verification.Status);
        Assert.Equal(0, verification.AttemptCount);
        Assert.Null(verification.VerifiedAt);
        Assert.Equal(_now, verification.CreatedAt);
        Assert.Equal(_now, verification.UpdatedAt);
    }

    [Fact]
    public void ResidentVerification_Constructor_ValidatesRequiredFields()
    {
        var userId = Guid.NewGuid();
        var residentId = Guid.NewGuid();
        var expiresAt = _now.AddMinutes(5);

        Assert.Throws<ArgumentException>(() => new ResidentVerification(Guid.Empty, residentId, "hash", expiresAt, _now));
        Assert.Throws<ArgumentException>(() => new ResidentVerification(userId, Guid.Empty, "hash", expiresAt, _now));
        Assert.Throws<ArgumentException>(() => new ResidentVerification(userId, residentId, " ", expiresAt, _now));
        Assert.Throws<ArgumentException>(() => new ResidentVerification(userId, residentId, "hash", _now, _now));
        Assert.Throws<ArgumentException>(() => new ResidentVerification(userId, residentId, "hash", _now.AddTicks(-1), _now));
    }

    [Fact]
    public void ResidentVerification_RecordFailedAttempt_IncrementsOnlyWhilePendingAndNotExpired()
    {
        var verification = CreateVerification();

        verification.RecordFailedAttempt(_now.AddMinutes(1));

        Assert.Equal(1, verification.AttemptCount);

        verification.Cancel(_now.AddMinutes(2));
        Assert.Throws<InvalidOperationException>(() => verification.RecordFailedAttempt(_now.AddMinutes(3)));
    }

    [Fact]
    public void ResidentVerification_RecordFailedAttempt_RejectsExpiredPendingChallenge()
    {
        var verification = CreateVerification(expiresAt: _now.AddMinutes(5));

        Assert.Throws<InvalidOperationException>(() => verification.RecordFailedAttempt(_now.AddMinutes(5)));
    }

    [Fact]
    public void ResidentVerification_Verify_SucceedsBeforeExpiry_AndCannotVerifyAgain()
    {
        var verification = CreateVerification();
        var verifyTime = _now.AddHours(1);

        verification.Verify(verifyTime);

        Assert.Equal(VerificationStatus.VERIFIED, verification.Status);
        Assert.Equal(verifyTime, verification.VerifiedAt);

        Assert.Throws<InvalidOperationException>(() => verification.Verify(verifyTime.AddMinutes(1)));
    }

    [Fact]
    public void ResidentVerification_Verify_RejectsExpiredChallenge()
    {
        var verification = CreateVerification(expiresAt: _now.AddMinutes(5));

        Assert.Throws<InvalidOperationException>(() => verification.Verify(_now.AddMinutes(5)));
        Assert.Equal(VerificationStatus.PENDING, verification.Status);
        Assert.Null(verification.VerifiedAt);
    }

    [Fact]
    public void ResidentVerification_Expire_OnlyWhenActuallyExpired()
    {
        var verification = CreateVerification(expiresAt: _now.AddMinutes(5));

        Assert.Throws<InvalidOperationException>(() => verification.Expire(_now.AddMinutes(4)));

        var expiredAt = _now.AddMinutes(5);
        verification.Expire(expiredAt);

        Assert.Equal(VerificationStatus.EXPIRED, verification.Status);
        Assert.Equal(expiredAt, verification.UpdatedAt);
    }

    [Fact]
    public void ResidentVerification_Cancel_OnlyFromPending()
    {
        var verification = CreateVerification();
        var cancelledAt = _now.AddMinutes(1);

        verification.Cancel(cancelledAt);

        Assert.Equal(VerificationStatus.CANCELLED, verification.Status);
        Assert.Equal(cancelledAt, verification.UpdatedAt);
        Assert.Throws<InvalidOperationException>(() => verification.Cancel(cancelledAt.AddMinutes(1)));
    }

    [Fact]
    public void ResidentVerification_DoesNotExposeManualReviewer_MethodCode_OrRejectedStatus()
    {
        Assert.Null(typeof(ResidentVerification).GetProperty("ReviewedBy"));
        Assert.Null(typeof(ResidentVerification).GetProperty("Reviewer"));
        Assert.Null(typeof(ResidentVerification).GetProperty("MethodCode"));
        Assert.Null(typeof(ResidentVerification).GetMethod("Reject", BindingFlags.Instance | BindingFlags.Public));
        Assert.DoesNotContain(Enum.GetNames<VerificationStatus>(), name => name == "REJECTED");
    }

    private ResidentVerification CreateVerification(DateTimeOffset? expiresAt = null)
    {
        return new ResidentVerification(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "otp-hash",
            expiresAt ?? _now.AddHours(2),
            _now);
    }
}
