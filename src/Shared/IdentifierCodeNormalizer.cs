using System.Globalization;
using System.Text;

namespace PropFlow.Shared;

/// <summary>Creates stable, ASCII-only identifier fragments without changing the source display name.</summary>
public static class IdentifierCodeNormalizer
{
    public static string NormalizeRequired(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{fieldName} là bắt buộc để sinh mã.", fieldName);

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD)
            .Replace('Đ', 'D').Replace('đ', 'd');
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToUpperInvariant(character));
        }

        var normalized = builder.ToString().Normalize(NormalizationForm.FormC);
        if (normalized.Length == 0)
            throw new ArgumentException($"{fieldName} không tạo được thành phần mã hợp lệ.", fieldName);
        return normalized;
    }

    public static long AdvisoryLockKey(string scope)
    {
        // FNV-1a is deterministic across processes; PostgreSQL advisory locks only need a stable 64-bit namespace key.
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            foreach (var character in scope)
            {
                hash ^= character;
                hash *= 1099511628211UL;
            }
            return (long)hash;
        }
    }
}
