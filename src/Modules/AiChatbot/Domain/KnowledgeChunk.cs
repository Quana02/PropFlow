using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PropFlow.Modules.AiChatbot.Domain;

public sealed class KnowledgeChunk
{
    private KnowledgeChunk() { }
    public KnowledgeChunk(Guid documentId, int revision, int number, string title, string text)
    { DocumentId = documentId; Revision = revision; Number = number; Content = text; SearchText = KnowledgeText.Search(title + " " + text); }
    public Guid DocumentId { get; private set; }
    public int Revision { get; private set; }
    public int Number { get; private set; }
    public string Content { get; private set; } = "";
    public string SearchText { get; private set; } = "";
    public float[]? Embedding { get; private set; }
    public string? EmbeddingModel { get; private set; }
    public DateTimeOffset? AttemptedAt { get; private set; }
    public void Attempt(DateTimeOffset now) => AttemptedAt = now;
    public void SetEmbedding(float[] vector, string model)
    {
        if (vector.Length == 0 || vector.Any(x => !float.IsFinite(x)) || vector.All(x => x == 0))
            throw new ArgumentException("Invalid embedding.");
        var norm = Math.Sqrt(vector.Sum(x => (double)x * x));
        Embedding = vector.Select(x => (float)(x / norm)).ToArray(); EmbeddingModel = model;
    }
}

public static partial class KnowledgeText
{
    public static IReadOnlyList<KnowledgeChunk> Split(Guid documentId, int revision, string title, string content)
    {
        var chunks = new List<KnowledgeChunk>();
        for (var start = 0; start < content.Length;)
        {
            var end = Math.Min(start + 1200, content.Length);
            if (end < content.Length)
            {
                if (char.IsLowSurrogate(content[end])) end--;
                var boundary = content.LastIndexOfAny(['\n', ' ', '\t'], end - 1, end - start);
                if (boundary > start + 700) end = boundary;
            }
            var text = content[start..end].Trim();
            if (text.Length > 0) chunks.Add(new(documentId, revision, chunks.Count + 1, title, text));
            if (end == content.Length) break;
            start = Math.Max(start + 1, end - 140);
            if (char.IsLowSurrogate(content[start])) start--;
        }
        return chunks;
    }
    public static string Search(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD).ToLowerInvariant())
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c == 'đ' ? 'd' : char.IsLetterOrDigit(c) ? c : ' ');
        return Spaces().Replace(builder.ToString(), " ").Trim();
    }
    public static string Redact(string text) => Sensitive().Replace(text, "[thông tin đã ẩn]");
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex(@"[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}|(?<!\w)(?:\+?\d[\d .()-]{7,}\d)(?!\w)")] private static partial Regex Sensitive();
}
