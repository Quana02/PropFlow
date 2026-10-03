using System.Globalization;

namespace PropFlow.Web.Client.Shared.Forms;

public static class PropFlowDateText
{
    public const string FormatPattern = "dd/MM/yyyy";
    public const string InvalidMessage = "Ngày không hợp lệ. Vui lòng nhập theo định dạng dd/MM/yyyy.";

    public static bool TryParse(string? text, out DateOnly value) =>
        DateOnly.TryParseExact(text?.Trim(), FormatPattern, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    public static string Format(DateOnly? value) =>
        value?.ToString(FormatPattern, CultureInfo.InvariantCulture) ?? string.Empty;

    public static string NormalizeTyping(string? input, string? previous)
    {
        var current = input ?? string.Empty;
        var prior = previous ?? string.Empty;

        // Backspace immediately after an auto-inserted separator should remove the
        // preceding digit too, otherwise the separator would be inserted forever.
        if (prior.EndsWith('/') && current == prior[..^1] && current.Length > 0)
            current = current[..^1];

        if (current.Any(character => !char.IsDigit(character) && character != '/'))
            return current;

        var digits = new string(current.Where(char.IsDigit).Take(8).ToArray());
        if (digits.Length < 2) return digits;

        var result = digits[..2] + "/";
        if (digits.Length < 4) return result + digits[2..];

        result += digits.Substring(2, 2) + "/";
        return result + digits[4..];
    }
}
