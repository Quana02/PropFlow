using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PropFlow.Modules.AiClassification.Application.ResidentDraftReview;

// Positive evidence only: unfamiliar items still go through the model, never a required category vocabulary.
public static class MovingDraftEvidence
{
    private static string Plain(string value) => new(value.ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    public static bool IsMoving(string text) => Regex.IsMatch(Plain(text), @"\b(?:dang ky (?:van )?chuyen|(?:van )?chuyen (?:do|nha|hang|noi that)|don nha)\b");
    public static bool HasItems(string text)
    {
        var value = Plain(text);
        if (Regex.IsMatch(value, @"\b(?:chua (?:biet|ro|xac dinh)|khong biet).*(?:do gi|mon gi|chuyen gi)\b")) return false;
        return Regex.IsMatch(value, @"\b(?:quat|pc|may tinh|sofa|giuong|tu lanh|ti vi|tivi|may giat|ban ghe|do noi that|do gia dung|do dien tu|do ca nhan|do dung ca nhan)\b|\b(?:toan bo|tat ca|het)\s+(?:do|tai san)\b") ||
            Regex.IsMatch(text.Normalize(NormalizationForm.FormC), @"\btủ\b", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(value, @"\b(?:gom|bao gom|danh sach|cac mon|nhung mon)\s*[:\-]?\s*(?!(?:gi|chua|khong|nhung gi)\b)[a-z0-9]{2,}");
    }
    public static bool HasDirection(string text)
    {
        var value = Plain(text);
        if (Regex.IsMatch(value, @"\b(?:chua|khong)\s+(?:biet|ro|xac dinh|quyet dinh).*(?:huong|chuyen|vao|ra)\b")) return false;
        return Regex.IsMatch(value, @"\b(?:ra (?:ngoai|khoi)|vao (?:can ho|nha|chung cu|toa nha)|(?:chuyen nha|don nha) (?:di|den)|(?:chuyen|van chuyen) (?:vao|ra)|(?:trong|noi bo) toa nha|giua cac can ho)\b");
    }
    public static bool IsClearlyComplete(string text) => IsMoving(text) && HasItems(text) && HasDirection(text);
}
