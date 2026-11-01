using System.Globalization;
using System.Text.RegularExpressions;

namespace PropFlow.Web.Client.Shared.Forms;

public static class PropFlowMoneyText
{
    private static readonly NumberFormatInfo Vietnamese = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };

    public static string Format(decimal value) => value.ToString("N0", Vietnamese);

    public static string Normalize(string text)
    {
        text = text.Trim();
        return Regex.IsMatch(text, @"^-?[0-9]{1,3}(?:\.[0-9]{3})+$") ? text.Replace(".", "") : text;
    }

    public static string FormatInput(string text) =>
        decimal.TryParse(Normalize(text), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? Format(value) : text;

    public static string NormalizeEdit(string previousDisplay, string text)
    {
        // A digit edit can temporarily disturb the grouping we inserted, e.g.
        // "4.000" + "0" becomes "4.0000" before it is regrouped as "40.000".
        // Only remove inherited separators for digit/deletion edits of valid text.
        // Pasted decimals, letters and malformed grouping still use strict parsing.
        if (previousDisplay.Length > 0 && !Regex.IsMatch(previousDisplay, @"^-?(?:[0-9]+|[0-9]{1,3}(?:\.[0-9]{3})+)$"))
            return Normalize(text);
        var prefix = 0;
        while (prefix < previousDisplay.Length && prefix < text.Length && previousDisplay[prefix] == text[prefix]) prefix++;
        var suffix = 0;
        while (suffix < previousDisplay.Length - prefix && suffix < text.Length - prefix
            && previousDisplay[^(suffix + 1)] == text[^(suffix + 1)]) suffix++;
        var inserted = text.AsSpan(prefix, text.Length - prefix - suffix);
        foreach (var character in inserted)
            if (!char.IsAsciiDigit(character)) return Normalize(text);
        return text.Replace(".", "");
    }
}
