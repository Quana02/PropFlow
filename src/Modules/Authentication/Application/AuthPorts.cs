using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Domain.Tokens;
using PropFlow.Modules.Authentication.Domain.ResidentVerifications;

namespace PropFlow.Modules.Authentication.Application;

public interface IAuthStore
{
    Task<T> SerializedAsync<T>(string key, Func<Task<T>> action, CancellationToken ct);
    Task<UserAccount?> UserAsync(Guid id, CancellationToken ct);
    Task<UserAccount?> UsernameAsync(string username, CancellationToken ct);
    Task<UserAccount?> EmailAsync(string email, CancellationToken ct);
    Task<bool> PhoneInUseAsync(string phone, Guid? except, CancellationToken ct);
    Task<ResidentVerification?> VerificationAsync(Guid id, CancellationToken ct);
    Task<List<ResidentVerification>> VerificationsAsync(Guid userId, CancellationToken ct);
    Task<PasswordResetToken?> RecoveryAsync(Guid id, CancellationToken ct);
    Task<List<PasswordResetToken>> RecoveriesAsync(Guid userId, CancellationToken ct);
    Task<RefreshToken?> RefreshAsync(string hash, CancellationToken ct);
    Task<RefreshToken?> RefreshByIdAsync(Guid id, CancellationToken ct);
    Task<List<RefreshToken>> SessionsAsync(Guid userId, CancellationToken ct);
    void Add(UserAccount value);
    void Add(ResidentVerification value);
    void Add(PasswordResetToken value);
    void Add(RefreshToken value);
    Task SaveAsync(CancellationToken ct);
}

public interface IAuthSecrets
{
    string HashPassword(UserAccount user, string password);
    bool VerifyPassword(UserAccount? user, string password);
    string NewToken();
    string NewOtp();
    string HashToken(string value);
    string HashOtp(string code);
    bool MatchesOtp(string code, string hash);
    SessionResponse Issue(AccountResponse user, DateTimeOffset now);
}
public interface IAuthEmail { Task SendCodeAsync(string email, string code, bool recovery, int expiryMinutes, CancellationToken ct); }
public sealed class AuthFailure(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
public sealed record SessionIssue(SessionResponse Response, string RefreshToken, DateTimeOffset RefreshExpiresAt);
