using PropFlow.Modules.Authentication.Domain.Users;

namespace PropFlow.Modules.Authentication.Domain.Tokens;

public class RefreshToken
{
    private RefreshToken()
    {
        // Parameterless constructor for EF Core
    }

    public RefreshToken(
        Guid userId,
        string tokenHash,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        string? deviceName = null,
        string? ipAddress = null,
        string? userAgent = null)
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
        DeviceName = deviceName?.Trim();
        IpAddress = ipAddress?.Trim();
        UserAgent = userAgent?.Trim();
        RevokedAt = null;
        RevokedReason = null;
        ReplacedByTokenId = null;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public string? DeviceName { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokedReason { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // Within-module navigation
    public UserAccount? User { get; private set; }
    public RefreshToken? ReplacedByToken { get; private set; }

    public bool IsRevoked => RevokedAt != null;

    public bool IsExpiredAt(DateTimeOffset now) => now >= ExpiresAt;
    public bool IsActiveAt(DateTimeOffset now) => !IsRevoked && !IsExpiredAt(now);

    public void Revoke(string reason, DateTimeOffset now, Guid? replacedByTokenId = null)
    {
        if (IsRevoked)
        {
            throw new InvalidOperationException("Refresh token has already been revoked.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        RevokedAt = now;
        RevokedReason = reason.Trim();
        ReplacedByTokenId = replacedByTokenId;
    }
}
