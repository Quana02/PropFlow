namespace PropFlow.Modules.Administration.Domain.Roles;

public static class SystemRoleCodes
{
    public const string Resident = "RESIDENT";
    public const string Staff = "STAFF";
    public const string Accountant = "ACCOUNTANT";
    public const string Manager = "MANAGER";
    public const string Admin = "ADMIN";

    private static readonly string[] Supported =
    [
        Resident,
        Staff,
        Accountant,
        Manager,
        Admin
    ];

    public static IReadOnlyCollection<string> All => Array.AsReadOnly(Supported);

    public static bool IsSupported(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var normalizedCode = Normalize(code);
        return Supported.Contains(normalizedCode, StringComparer.Ordinal);
    }

    public static string Normalize(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Role code is required.", nameof(code));
        }

        return code.Trim().ToUpperInvariant();
    }
}
