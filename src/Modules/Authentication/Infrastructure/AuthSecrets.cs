using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Users;

namespace PropFlow.Modules.Authentication.Infrastructure;

public sealed class AuthSecrets : IAuthSecrets, IDisposable
{
    private readonly PasswordHasher<UserAccount> hasher = new();
    private readonly UserAccount dummy;
    private readonly AuthPolicy policy;
    private readonly RSA rsa;
    private readonly byte[] otpKey;
    public RsaSecurityKey SigningKey { get; }
    public AuthSecrets(AuthPolicy policy)
    {
        this.policy = policy;
        rsa = RSA.Create();
        rsa.ImportFromPem(policy.PrivateKeyPem);
        if (rsa.KeySize < 2048) throw new InvalidOperationException("Auth RSA key must be at least 2048 bits.");
        SigningKey = new RsaSecurityKey(rsa);
        otpKey = Convert.FromBase64String(policy.OtpHashKey);
        if (otpKey.Length < 32) throw new InvalidOperationException("Auth OTP hash key must contain at least 32 random bytes.");
        dummy = new UserAccount("timing-only", "pending", "Timing verification", DateTimeOffset.UnixEpoch);
        dummy.ChangePasswordHash(hasher.HashPassword(dummy, NewToken()), null, DateTimeOffset.UnixEpoch);
    }
    public string HashPassword(UserAccount user, string password) => hasher.HashPassword(user, password);
    public bool VerifyPassword(UserAccount? user, string password)
    {
        var target = user ?? dummy;
        var result = hasher.VerifyHashedPassword(target, target.PasswordHash, password);
        if (user != null && result == PasswordVerificationResult.SuccessRehashNeeded)
            user.ChangePasswordHash(hasher.HashPassword(user, password), user.Id, DateTimeOffset.UtcNow);
        return user != null && result != PasswordVerificationResult.Failed;
    }
    public string NewToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
    public string NewOtp() => RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    public string HashToken(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public string HashOtp(string code)
    {
        var salt = NewToken();
        return salt + "." + Convert.ToHexString(HMACSHA256.HashData(otpKey, Encoding.UTF8.GetBytes(salt + ":" + code)));
    }
    public bool MatchesOtp(string code, string hash)
    {
        var parts = hash.Split('.');
        if (parts.Length != 2 || parts[1].Length != 64) return false;
        var actual = HMACSHA256.HashData(otpKey, Encoding.UTF8.GetBytes(parts[0] + ":" + code));
        try { return CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(parts[1])); }
        catch (FormatException) { return false; }
    }
    public SessionResponse Issue(AccountResponse user, DateTimeOffset now)
    {
        var expires = now.AddMinutes(policy.AccessMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()), new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("name", user.DisplayName), new("role", user.Role)
        };
        claims.AddRange(user.Permissions.Select(x => new Claim("permission", x)));
        var jwt = new JwtSecurityToken(policy.Issuer, policy.Audience, claims, now.UtcDateTime, expires.UtcDateTime,
            new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256));
        return new(new JwtSecurityTokenHandler().WriteToken(jwt), expires, user);
    }
    public void Dispose() { rsa.Dispose(); CryptographicOperations.ZeroMemory(otpKey); }
}
