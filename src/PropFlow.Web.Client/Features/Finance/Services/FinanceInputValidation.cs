using System.Globalization;
using System.Text.RegularExpressions;
using PropFlow.Web.Client.Shared.Forms;

namespace PropFlow.Web.Client.Features.Finance.Services;
public static class FinanceInputValidation
{
    public static string? Number(string? text, string label, decimal min, decimal max, int places, out decimal? value)
    {
        value = null;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return $"Không được để trống ô {label}.";
        if (!Regex.IsMatch(text, @"^-?[0-9]+(?:[.,][0-9]+)?$") ||
            !decimal.TryParse(text.Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
            return "Vui lòng nhập số hợp lệ, không dùng chữ hoặc ký tự đặc biệt.";
        var separator = text.IndexOfAny(['.', ',']);
        if (separator >= 0 && text.Length - separator - 1 > places)
            return places == 0 ? "Số tiền VND phải là số nguyên." : $"Chỉ nhập tối đa {places} chữ số sau dấu thập phân.";
        if (parsed < min) return min == 0 ? "Giá trị không được âm." : $"Giá trị phải lớn hơn hoặc bằng {min:0.###}.";
        if (parsed > max) return $"Giá trị không được vượt quá {(places == 0 ? PropFlowMoneyText.Format(max) : max.ToString("0", CultureInfo.InvariantCulture))}.";
        value = parsed;
        return null;
    }
}
