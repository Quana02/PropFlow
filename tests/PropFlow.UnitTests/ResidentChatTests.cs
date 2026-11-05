using System.Security.Claims;
using PropFlow.Modules.AiChatbot.Application;
using PropFlow.Modules.AiChatbot.Contracts;
using PropFlow.Modules.AiChatbot.Domain;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-12")]
public sealed class ResidentChatTests
{
    [Theory]
    [InlineData("RESIDENT", "USE_RESIDENT_SERVICES", true)]
    [InlineData("RESIDENT", "", false)]
    [InlineData("MANAGER", "MANAGE_OPERATIONS", false)]
    [InlineData("ADMIN", "MANAGE_INTERNAL_ACCOUNTS", false)]
    [InlineData("STAFF", "PERFORM_ASSIGNED_OPERATIONS", false)]
    [InlineData("ACCOUNTANT", "MANAGE_FINANCE", false)]
    public void ChatIsResidentFacingAndRequiresPermission(string role, string permission, bool allowed)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Role, role), new("permission", permission)], "test"));
        Assert.Equal(allowed, ResidentChatPolicy.Allowed(principal));
    }

    [Fact]
    public async Task MissingResidenceIsRejectedBeforeRetrievalOrProviderCalls()
    {
        var fixture = new Fixture { Active = false };
        await Assert.ThrowsAsync<ChatAccessException>(() => fixture.Chat.AskAsync(Guid.NewGuid(), "Nội quy là gì?", default));
        Assert.Equal(0, fixture.Searches); Assert.Equal(0, fixture.Answers); Assert.Equal(0, fixture.Embeddings);
    }

    [Fact]
    public async Task UnconfiguredAiReturnsRealExcerptsWithoutAnInventedAnswer()
    {
        var fixture = new Fixture { Ready = false };
        var result = await fixture.Chat.AskAsync(Guid.NewGuid(), "Phí sửa chữa phát sinh", default);
        Assert.Equal("DOCUMENTS_ONLY", result.Mode); Assert.Single(result.Sources);
        Assert.Equal(fixture.Passages[0].Content, result.Sources[0].Excerpt);
        Assert.Equal(0, fixture.Answers); Assert.Equal(0, fixture.Embeddings);
    }

    [Fact]
    public async Task NoRelevantSourcesNeverCallsTheAnswerModel()
    {
        var fixture = new Fixture { Passages = [] };
        var result = await fixture.Chat.AskAsync(Guid.NewGuid(), "Câu hỏi ngoài phạm vi", default);
        Assert.Equal("NO_SOURCES", result.Mode); Assert.Empty(result.Sources); Assert.Equal(0, fixture.Answers);
    }

    [Fact]
    public async Task AiUsesServerOwnedCitationIdentifiersOnly()
    {
        var fixture = new Fixture { Answer = new(true, "Cư dân cần đồng ý trước khi làm [1].", [1]) };
        var result = await fixture.Chat.AskAsync(Guid.NewGuid(), "Chi phí phát sinh xử lý thế nào?", default);
        Assert.Equal("AI", result.Mode); Assert.Equal(fixture.Passages[0].DocumentId, result.Sources[0].DocumentId);
        Assert.Equal(1, fixture.Answers);
    }

    [Theory]
    [InlineData(true, 99)] [InlineData(false, 1)] [InlineData(true, 0)]
    public async Task UnsupportedOrForgedCitationsFallBackToDocuments(bool supported, int citation)
    {
        var fixture = new Fixture { Answer = new(supported, "Thông tin không có căn cứ", [citation]) };
        var result = await fixture.Chat.AskAsync(Guid.NewGuid(), "Quy trình sửa chữa", default);
        Assert.Equal("DOCUMENTS_ONLY", result.Mode); Assert.DoesNotContain("Thông tin không có căn cứ", result.Answer);
        Assert.DoesNotContain("Dịch vụ AI hiện không khả dụng", result.Answer);
    }

    [Fact]
    public async Task RevocationDuringGenerationDiscardsBothAnswerAndExcerpts()
    {
        var fixture = new Fixture { Current = false, Answer = new(true, "Nội dung cũ [1]", [1]) };
        var result = await fixture.Chat.AskAsync(Guid.NewGuid(), "Hướng dẫn xử lý phát sinh", default);
        Assert.Equal("SOURCE_CHANGED", result.Mode); Assert.Empty(result.Sources); Assert.DoesNotContain("Nội dung cũ", result.Answer);
    }

    [Fact]
    public async Task ProviderFailureDoesNotBlockReadingDocuments()
    {
        var fixture = new Fixture { Throw = true };
        var result = await fixture.Chat.AskAsync(Guid.NewGuid(), "Quy định sửa chữa", default);
        Assert.Equal("DOCUMENTS_ONLY", result.Mode); Assert.Single(result.Sources);
        Assert.Contains("Dịch vụ AI hiện không khả dụng", result.Answer);
        Assert.DoesNotContain("chưa tạo được câu trả lời có đủ căn cứ", result.Answer);
    }

    [Fact]
    public async Task QuestionsDoNotSendRawEmailOrPhoneToAi()
    {
        var fixture = new Fixture();
        await fixture.Chat.AskAsync(Guid.NewGuid(), "Hỏi nội quy. Email test@example.com, điện thoại 0901234567", default);
        Assert.DoesNotContain("test@example.com", fixture.ProviderQuestion);
        Assert.DoesNotContain("0901234567", fixture.ProviderQuestion);
    }

    [Fact]
    public async Task PersonalFinanceIsGuidanceOnly()
    {
        var fixture = new Fixture();
        var result = await fixture.Chat.AskAsync(Guid.NewGuid(), "Hóa đơn của tôi đã thanh toán chưa?", default);
        Assert.Equal("GUIDANCE", result.Mode); Assert.Empty(result.Sources);
        Assert.Equal(0, fixture.Embeddings); Assert.Equal(0, fixture.Answers); Assert.Equal(0, fixture.Searches);
    }

    [Theory]
    [InlineData("")] [InlineData("a")] [InlineData("abc\0def")]
    public async Task RejectsInvalidQuestions(string question) =>
        await Assert.ThrowsAsync<KnowledgeValidationException>(() => new Fixture().Chat.AskAsync(Guid.NewGuid(), question, default));

    [Fact]
    public void ChunkingPreservesUnicodeAndRespectsSizeBounds()
    {
        var content = string.Concat(Enumerable.Repeat("Nội quy chung cư: cư dân đồng ý chi phí phát sinh. 🏡\n", 500));
        var chunks = KnowledgeText.Split(Guid.NewGuid(), 1, "Nội quy", content);
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => {
            Assert.InRange(chunk.Content.Length, 1, 1200);
            Assert.False(char.IsLowSurrogate(chunk.Content[0])); Assert.False(char.IsHighSurrogate(chunk.Content[^1]));
            Assert.Contains("noi quy", chunk.SearchText);
        });
        Assert.Contains("đồng ý", chunks[0].Content); Assert.Equal("dien nuoc", KnowledgeText.Search("Điện nước"));
    }

    private sealed class Fixture : IChatAi, IResidentKnowledgeSearch, IChatResidentScope
    {
        public bool Active = true, Current = true, Throw;
        public bool Ready { get; set; } = true;
        public string EmbeddingKey => "test:2";
        public int Searches, Answers, Embeddings;
        public string ProviderQuestion = "";
        public GroundedAnswer? Answer = new(true, "Đồng ý trước khi làm [1].", [1]);
        public IReadOnlyList<RetrievedPassage> Passages = [new(Guid.NewGuid(), 1, 1, "Nội quy", "Cư dân cần đồng ý chi phí phát sinh trước khi thực hiện.")];
        public ResidentChat Chat => new(this, this, this);
        public Task<bool> ActiveAsync(Guid actor, CancellationToken ct) => Task.FromResult(Active);
        public Task<bool> CurrentAsync(IReadOnlyList<RetrievedPassage> passages, CancellationToken ct) => Task.FromResult(Current);
        public Task<PublishedKnowledgeSource?> SourceAsync(Guid id, int revision, CancellationToken ct) => Task.FromResult<PublishedKnowledgeSource?>(null);
        public Task<IReadOnlyList<RetrievedPassage>> SearchAsync(string question, float[]? embedding, string embeddingKey, CancellationToken ct)
        { Searches++; return Task.FromResult(Passages); }
        public Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
        { Embeddings++; ProviderQuestion = texts[0]; if (Throw) throw new ChatAiUnavailableException(); return Task.FromResult(new float[][] { [1, 0] }); }
        public Task<GroundedAnswer?> AnswerAsync(string question, IReadOnlyList<RetrievedPassage> passages, CancellationToken ct)
        { Answers++; ProviderQuestion = question; if (Throw) throw new ChatAiUnavailableException(); return Task.FromResult(Answer); }
    }
}
