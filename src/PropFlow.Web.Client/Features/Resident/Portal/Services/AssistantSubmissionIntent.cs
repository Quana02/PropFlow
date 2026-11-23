using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PropFlow.Web.Client.Features.Resident.Portal.Services;

public static class AssistantSubmissionIntent
{
    private static string Normalize(string text) => new(text.ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    public static bool IsExplicitNewRequest(string text)
    {
        var value = Normalize(text);
        return !Regex.IsMatch(value, @"\b(?:khong|chua|dung|khoan)\b") &&
            Regex.IsMatch(value, @"\b(?:don|yeu cau)\s+(?:moi|khac)\b|\b(?:bat dau lai|tao them (?:mot )?(?:don|yeu cau))\b");
    }
    public static bool IsExplicitSend(string text)
    {
        var value = Normalize(text);
        // Confirm an action, not a question, quotation, negation, future condition or request to edit.
        if (value.IndexOfAny(['?', '"', '\'', '“', '”', '«', '»']) >= 0 || Regex.IsMatch(value,
            @"\b(khong|chua|dung|khoan|cho da|neu|gia su|vi du|nghia la|noi rang|noi la|huong dan|cach gui|lam sao|the nao|tai sao|vi sao|la gi|bao gio|khi nao|noi quy|tai lieu|chinh sach|trich dan|nhac den|duoc chua|duoc khong|sua lai|sua don|doi lai|chinh sua|bo sung|them|bao gom|gom|dinh chinh|gui sau|lat nua|ngay mai)\b")) return false;
        return Regex.IsMatch(value, @"\b(gui|nop|chuyen)\s+(?:giup\s+(?:toi|minh)\s+)?(?:cai\s+)?(?:don|yeu cau|ban nhap|phieu)(?:\s|$)|\bgui\s+(?:di|luon|ngay|cho\s+(?:bql|ban quan ly))\b");
    }
}
