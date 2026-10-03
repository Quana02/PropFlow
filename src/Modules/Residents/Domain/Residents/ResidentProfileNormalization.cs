using PropFlow.Modules.Residents.Contracts;

namespace PropFlow.Modules.Residents.Domain.Residents;

public static class ResidentProfileNormalization
{
    public static string? IdentityType(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : ResidentRegistrationNormalization.IdentityType(value);

    public static string? IdentityNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var digits = ResidentRegistrationNormalization.IdentityNumber(value);
        return digits.Length == 0 ? null : digits;
    }

    public static string? Email(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : ResidentRegistrationNormalization.Email(value);
}
