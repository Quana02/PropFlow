using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.AiChatbot.Application;
using PropFlow.Modules.AiChatbot.Contracts;
using PropFlow.Modules.AiChatbot.Domain;
using PropFlow.Modules.AiChatbot.Infrastructure;
using PropFlow.Modules.AiChatbot.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

[Trait("Feature", "FE-12")]
public sealed class GeminiReindexTests(KnowledgeDatabaseFixture database) : IClassFixture<KnowledgeDatabaseFixture>
{
    private const string GeminiKey = "gemini:gemini-embedding-001:3:retrieval-v1";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderChangeReindexesRecentAttemptOnceAndNeverMixesOldVectors(bool hadOldVector)
    {
        await using var db = database.CreateDb();
        var id = await Published(db, "RESIDENT", "Cư dân báo hỏng vòi nước.");
        var chunk = await db.Chunks.SingleAsync(x => x.DocumentId == id);
        const string oldKey = "text-embedding-3-small:512";
        if (hadOldVector) chunk.SetEmbedding([1, 0, 0], oldKey);
        chunk.Attempt(DateTimeOffset.UtcNow, oldKey);
        // The previous adapter left the model null when its first attempt failed.
        if (!hadOldVector) db.Entry(chunk).Property(x => x.EmbeddingModel).CurrentValue = null;
        await db.SaveChangesAsync();
        var ai = new TestAi();

        await new KnowledgeIndex(db, ai).TickAsync(default);

        await db.Entry(chunk).ReloadAsync();
        Assert.Equal(GeminiKey, chunk.EmbeddingModel);
        Assert.Equal(new float[] { 0, 1, 0 }, chunk.Embedding);
        Assert.Contains(ai.Inputs, x => x.Contains("Cư dân báo hỏng vòi nước.", StringComparison.Ordinal));
        var calls = ai.Calls;
        await new KnowledgeIndex(db, ai).TickAsync(default);
        Assert.Equal(calls, ai.Calls);

        var search = new ResidentKnowledgeSearch(db, new KnowledgeDocuments(db));
        Assert.DoesNotContain(await search.SearchAsync("zzznomatchingkeyword", [1, 0, 0], oldKey, default), x => x.DocumentId == id);
        Assert.Contains(await search.SearchAsync("zzznomatchingkeyword", [0, 1, 0], GeminiKey, default), x => x.DocumentId == id);
    }

    [Fact]
    public async Task FailedGeminiReplacementClearsOldVectorAndKeepsCooldownAcrossWorkerScopes()
    {
        Guid id;
        await using (var setup = database.CreateDb())
        {
            id = await Published(setup, "RESIDENT", "Hướng dẫn tiếp nhận hỗ trợ kỹ thuật.");
            var chunk = await setup.Chunks.SingleAsync(x => x.DocumentId == id);
            chunk.SetEmbedding([1, 0, 0], "text-embedding-3-small:512");
            chunk.Attempt(DateTimeOffset.UtcNow, "text-embedding-3-small:512");
            await setup.SaveChangesAsync();
        }
        var ai = new TestAi { Fail = true };
        await using (var firstWorker = database.CreateDb()) await new KnowledgeIndex(firstWorker, ai).TickAsync(default);
        var calls = ai.Calls;
        Assert.True(calls > 0);
        await using var secondWorker = database.CreateDb();
        await new KnowledgeIndex(secondWorker, ai).TickAsync(default);
        Assert.Equal(calls, ai.Calls);
        var attempted = await secondWorker.Chunks.SingleAsync(x => x.DocumentId == id);
        Assert.Equal(GeminiKey, attempted.EmbeddingModel);
        Assert.Null(attempted.Embedding);
        Assert.NotNull(attempted.AttemptedAt);
        var status = await new KnowledgeDocuments(secondWorker, ai).IndexStatusAsync(id, default);
        Assert.NotNull(status);
        Assert.Equal(0, status.EmbeddedChunks);
    }

    [Fact]
    public async Task ProviderChangeDoesNotSendInternalOrDraftContent()
    {
        await using var db = database.CreateDb();
        var marker = Guid.NewGuid().ToString("N");
        await Published(db, "INTERNAL", "INTERNAL_" + marker);
        await new KnowledgeDocuments(db).SaveAsync(null, new("Bản nháp", "DRAFT_" + marker, "RESIDENT", null), Guid.NewGuid(), default);
        var id = await Published(db, "RESIDENT", "PUBLIC_" + marker);
        var ai = new TestAi();

        await new KnowledgeIndex(db, ai).TickAsync(default);

        Assert.DoesNotContain(ai.Inputs, x => x.Contains("INTERNAL_" + marker, StringComparison.Ordinal));
        Assert.DoesNotContain(ai.Inputs, x => x.Contains("DRAFT_" + marker, StringComparison.Ordinal));
        Assert.Contains(ai.Inputs, x => x.Contains("PUBLIC_" + marker, StringComparison.Ordinal));
        Assert.NotNull((await db.Chunks.SingleAsync(x => x.DocumentId == id)).Embedding);
    }

    private static async Task<Guid> Published(AiChatbotDbContext db, string audience, string content)
    {
        var documents = new KnowledgeDocuments(db); var actor = Guid.NewGuid();
        var id = await documents.SaveAsync(null, new("Tài liệu kiểm thử", content, audience, null), actor, default);
        var detail = (await documents.GetAsync(id, true, default))!;
        await documents.SetPublicationAsync(id, detail.Document.Token, true, actor, default);
        return id;
    }

    private sealed class TestAi : IChatAi
    {
        public bool Ready => true;
        public string EmbeddingKey => GeminiKey;
        public int Calls;
        public bool Fail;
        public List<string> Inputs = [];
        public Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct, ChatEmbeddingPurpose purpose = ChatEmbeddingPurpose.Document)
        {
            Calls++; Inputs.AddRange(texts);
            if (Fail) throw new ChatAiUnavailableException();
            return Task.FromResult(texts.Select(_ => new float[] { 0, 1, 0 }).ToArray());
        }
        public Task<GroundedAnswer?> AnswerAsync(string question, IReadOnlyList<RetrievedPassage> passages, CancellationToken ct) => Task.FromResult<GroundedAnswer?>(null);
    }
}
