using System.Globalization;

namespace PropFlow.Web.Client.Features.Apartment.Models;
public static class ApartmentAreaInput
{
    public static decimal? Parse(string? text)
    {
        var normalized = text?.Trim().Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    public static string Format(decimal? value) => value?.ToString("0.##", CultureInfo.GetCultureInfo("vi-VN")) ?? "";
}
