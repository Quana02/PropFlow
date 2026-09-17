namespace PropFlow.Modules.Authentication.Application;

public sealed class AuthPolicy
{
    public int AccessMinutes { get; set; } = 15;
    public int RefreshDays { get; set; } = 7;
    public int OtpMinutes { get; set; } = 5;
    public int OtpAttempts { get; set; } = 5;
    public int ResendSeconds { get; set; } = 60;
    public int SendsPerHour { get; set; } = 5;
    public int LockoutFailures { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string PrivateKeyPem { get; set; } = "";
    public string OtpHashKey { get; set; } = "";
    public bool CrossSiteCookie { get; set; }
}
