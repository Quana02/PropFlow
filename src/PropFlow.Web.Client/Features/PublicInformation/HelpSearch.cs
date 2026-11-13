using System.Globalization;
using System.Text;

namespace PropFlow.Web.Client.Features.PublicInformation;

public sealed record FaqEntry(string Question, string Answer, string Keywords = "", string? Link = null, string? LinkLabel = null);
public sealed record FaqCategory(string Id, string Title, IReadOnlyList<FaqEntry> Entries);

public static class HelpSearch
{
    public static bool Matches(FaqEntry entry, string category, string query)
    {
        var searchable = Normalize($"{entry.Question} {category} {entry.Keywords}");
        return Normalize(query).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .All(term => searchable.Contains(term, StringComparison.Ordinal));
    }

    private static string Normalize(string value)
    {
        var normalized = new StringBuilder();
        foreach (var letter in value.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (CharUnicodeInfo.GetUnicodeCategory(letter) != UnicodeCategory.NonSpacingMark)
                normalized.Append(letter == 'đ' ? 'd' : letter);
        }
        return normalized.ToString();
    }
}
