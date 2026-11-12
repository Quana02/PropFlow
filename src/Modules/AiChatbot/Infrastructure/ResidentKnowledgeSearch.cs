using System.Data;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.AiChatbot.Application;
using PropFlow.Modules.AiChatbot.Contracts;
using PropFlow.Modules.AiChatbot.Domain;
using PropFlow.Modules.AiChatbot.Infrastructure.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace PropFlow.Modules.AiChatbot.Infrastructure;

public sealed class ResidentKnowledgeSearch(AiChatbotDbContext db, IPublishedKnowledgeSource sources) : IResidentKnowledgeSearch
{
    private static readonly HashSet<string> StopWords = new("toi minh muon hoi la gi nhu the nao co duoc khong cua ve cho va thi can hay xin ban khi nay do mot nhung cac o de bao nhieu".Split(' '));
    public async Task<IReadOnlyList<RetrievedPassage>> SearchAsync(string question, float[]? embedding, string embeddingKey, CancellationToken ct)
    {
        var terms = KnowledgeText.Search(question).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => !StopWords.Contains(x)).Distinct().Take(24).ToArray();
        if (terms.Length == 0 && embedding is null) return [];
        var vector = embedding ?? [];
        if (vector.Length > 0)
        {
            var norm = Math.Sqrt(vector.Sum(x => (double)x * x));
            vector = norm > 0 ? vector.Select(x => (float)(x / norm)).ToArray() : [];
        }
        // Exact cosine search over native PostgreSQL arrays keeps the local preview extension-free.
        // Publication and audience are part of the SQL query, never post-filtered client-side.
        const string sql = """
            WITH ranked AS (
                SELECT c."DocumentId", c."Revision", c."Number", r."Title", c."Content",
                  ts_rank_cd(to_tsvector('simple', c."SearchText"), to_tsquery('simple', @terms)) AS lexical,
                  CASE WHEN c."EmbeddingModel" = @model AND cardinality(c."Embedding") = cardinality(@vector)
                            AND cardinality(@vector) > 0
                    THEN (SELECT sum(v.a::double precision * v.b::double precision) FROM unnest(c."Embedding", @vector) AS v(a,b))
                    ELSE 0 END AS semantic
                FROM ai_chatbot.knowledge_chunks c
                JOIN ai_chatbot.knowledge_documents d ON d."Id" = c."DocumentId" AND d."PublishedRevision" = c."Revision"
                JOIN ai_chatbot.knowledge_revisions r ON r."DocumentId" = c."DocumentId" AND r."Number" = c."Revision"
                WHERE r."Audience" = 'RESIDENT'
            )
            SELECT "DocumentId", "Revision", "Number", "Title", "Content" FROM ranked
            WHERE lexical > 0 OR semantic >= 0.35
            ORDER BY (lexical + greatest(semantic, 0)) DESC, "DocumentId", "Number" LIMIT 6
            """;
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = new NpgsqlCommand(sql, (NpgsqlConnection)db.Database.GetDbConnection());
            command.Parameters.AddWithValue("terms", string.Join(" | ", terms));
            command.Parameters.AddWithValue("model", embeddingKey);
            command.Parameters.AddWithValue("vector", NpgsqlDbType.Array | NpgsqlDbType.Real, vector);
            var result = new List<RetrievedPassage>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) result.Add(new(reader.GetGuid(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4)));
            return result;
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
    public async Task<bool> CurrentAsync(IReadOnlyList<RetrievedPassage> passages, CancellationToken ct)
    {
        foreach (var source in passages.Select(x => (x.DocumentId, x.Revision)).Distinct())
        {
            var current = await sources.FindResidentSourceAsync(source.DocumentId, ct);
            if (current?.Revision != source.Revision) return false;
        }
        return true;
    }
    public async Task<PublishedKnowledgeSource?> SourceAsync(Guid id, int revision, CancellationToken ct)
    { var source = await sources.FindResidentSourceAsync(id, ct); return source?.Revision == revision ? source : null; }
}
