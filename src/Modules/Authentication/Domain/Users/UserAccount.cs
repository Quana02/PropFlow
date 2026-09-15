using PropFlow.Modules.Authentication.Domain.Tokens;

namespace PropFlow.Modules.Authentication.Domain.Users;

public class UserAccount
{
    private readonly List<RefreshToken> _refreshTokens = [];
    private readonly List<PasswordResetToken> _passwordResetTokens = [];

    private UserAccount()
    {
        // Parameterless constructor for EF Core
    }

    public UserAccount(
        string username,
        string passwordHash,
        string displayName,
        DateTimeOffset now,
        string? email = null,
        string? phoneNumber = null,
        AccountStatus status = AccountStatus.PENDING,
        bool emailVerified = false,
        Guid? createdBy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        Id = Guid.NewGuid();
        Username = username.Trim();
        PasswordHash = passwordHash;
        DisplayName = displayName.Trim();
        Email = NormalizeOptionalEmail(email);
        PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        Status = status;
        EmailVerified = emailVerified;
        FailedLoginCount = 0;
        LockoutUntil = null;
        LastLoginAt = null;
        LastLoginIp = null;
        PasswordChangedAt = null;
        CreatedBy = createdBy;
        UpdatedBy = createdBy;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public string Username { get; private set; } = null!;
    public string? Email { get; private set; }
    public string? PhoneNumber { get; private set; }
    public string PasswordHash { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public AccountStatus Status { get; private set; } = AccountStatus.PENDING;
    public bool EmailVerified { get; private set; }
    public int FailedLoginCount { get; private set; }
    public DateTimeOffset? LockoutUntil { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public string? LastLoginIp { get; private set; }
    public DateTimeOffset? PasswordChangedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Within-module relationships
    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();
    public IReadOnlyCollection<PasswordResetToken> PasswordResetTokens => _passwordResetTokens.AsReadOnly();

    public bool IsLockedOutAt(DateTimeOffset now)
    {
        return LockoutUntil.HasValue && LockoutUntil.Value > now;
    }

    public void RecordLoginSuccess(string? ip, DateTimeOffset now)
    {
        FailedLoginCount = 0;
        LockoutUntil = null;
        LastLoginAt = now;
        LastLoginIp = ip?.Trim();
        UpdatedAt = now;
    }

    public void RecordLoginFailure(int maxAllowedFailures, TimeSpan lockoutDuration, DateTimeOffset now)
    {
        FailedLoginCount++;
        if (FailedLoginCount >= maxAllowedFailures)
        {
            LockoutUntil = now.Add(lockoutDuration);
        }
        UpdatedAt = now;
    }

    public void ChangePasswordHash(string newPasswordHash, Guid? updatedBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newPasswordHash);
        PasswordHash = newPasswordHash;
        PasswordChangedAt = now;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void UpdateProfile(string displayName, string? phoneNumber, Guid? updatedBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        DisplayName = displayName.Trim();
        PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void ChangeEmail(string newEmail, Guid? updatedBy, DateTimeOffset now)
    {
        var normalizedEmail = NormalizeRequiredEmail(newEmail);

        if (!string.Equals(Email, normalizedEmail, StringComparison.Ordinal))
        {
            Email = normalizedEmail;
            EmailVerified = false;
        }

        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void Activate(Guid? updatedBy, DateTimeOffset now)
    {
        ApplyAccountStatus(AccountStatus.ACTIVE, updatedBy, now);
    }

    public void Lock(Guid? updatedBy, DateTimeOffset now)
    {
        ApplyAccountStatus(AccountStatus.LOCKED, updatedBy, now);
    }

    public void Suspend(Guid? updatedBy, DateTimeOffset now)
    {
        ApplyAccountStatus(AccountStatus.SUSPENDED, updatedBy, now);
    }

    public void Disable(Guid? updatedBy, DateTimeOffset now)
    {
        ApplyAccountStatus(AccountStatus.DISABLED, updatedBy, now);
    }

    public void MarkEmailVerified(DateTimeOffset now)
    {
        EmailVerified = true;
        UpdatedAt = now;
    }

    private static string? NormalizeOptionalEmail(string? email)
    {
        return string.IsNullOrWhiteSpace(email) ? null : NormalizeRequiredEmail(email);
    }

    private static string NormalizeRequiredEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return email.Trim().ToUpperInvariant();
    }

    private void ApplyAccountStatus(AccountStatus status, Guid? updatedBy, DateTimeOffset now)
    {
        Status = status;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }
}
