using PropFlow.Modules.Authentication.Domain.Users;

namespace PropFlow.Modules.Authentication.Domain.Tokens;

public class PasswordResetToken
{
    private PasswordResetToken()
    {
        // Parameterless constructor for EF Core
    }

    public PasswordResetToken(
        Guid userId,
        string tokenHash,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        string? requestedIp = null)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        Id = Guid.NewGuid();
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        UsedAt = null;
        RequestedIp = requestedIp?.Trim();
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }
    public string? RequestedIp { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // Within-module navigation
    public UserAccount? User { get; private set; }

    public bool IsUsed => UsedAt != null;

    public bool IsExpiredAt(DateTimeOffset now) => now >= ExpiresAt;
    public bool IsActiveAt(DateTimeOffset now) => !IsUsed && !IsExpiredAt(now);

    public void MarkUsed(DateTimeOffset now)
    {
        if (IsUsed)
        {
            throw new InvalidOperationException("Password reset token has already been used.");
        }

        if (IsExpiredAt(now))
        {
            throw new InvalidOperationException("Cannot use an expired password reset token.");
        }

        UsedAt = now;
    }
}
