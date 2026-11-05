using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PropFlow.Modules.AiChatbot.Application;
using PropFlow.Modules.AiChatbot.Contracts;
using PropFlow.Modules.AiChatbot.Infrastructure;
using PropFlow.Modules.AiChatbot.Presentation;

namespace PropFlow.IntegrationTests;

[Trait("Feature", "FE-12")]
public sealed class ResidentChatDatabaseTests(KnowledgeDatabaseFixture database) : IClassFixture<KnowledgeDatabaseFixture>
{
    [Fact]
    public async Task SearchOnlyReturnsCurrentPublishedResidentChunksAndRevokesSourcesImmediately()
    {
        await using var db = database.CreateDb(); var documents = new KnowledgeDocuments(db); var actor = Guid.NewGuid();
        var key = "nguon" + Guid.NewGuid().ToString("N");
        var id = await documents.SaveAsync(null, new("Nội quy", key + " Nội dung cư dân", "RESIDENT", null), actor, default);
        var search = new ResidentKnowledgeSearch(db, documents);
        Assert.Empty(await search.SearchAsync(key, null, "test", default));
        var draft = (await documents.GetAsync(id, true, default))!;
        await documents.SetPublicationAsync(id, draft.Document.Token, true, actor, default);
        var found = await search.SearchAsync(key, null, "test", default);
        Assert.Single(found); Assert.Equal(id, found[0].DocumentId);
        Assert.True(await search.CurrentAsync(found, default));
        draft = (await documents.GetAsync(id, true, default))!;
        await documents.SaveAsync(id, new("Nội bộ", key + " Bí mật mới", "INTERNAL", draft.Document.Token), actor, default);
        Assert.Equal(1, (await search.SearchAsync(key, null, "test", default)).Single().Revision);
        draft = (await documents.GetAsync(id, true, default))!;
        await documents.SetPublicationAsync(id, draft.Document.Token, true, actor, default);
        Assert.Empty(await search.SearchAsync(key, null, "test", default));
        Assert.False(await search.CurrentAsync(found, default)); Assert.Null(await search.SourceAsync(id, 1, default));
    }

    [Fact]
    public async Task NativePostgresVectorSearchFindsSemanticMatchAndRejectsWrongModel()
    {
        await using var db = database.CreateDb(); var documents = new KnowledgeDocuments(db);
        var id = await documents.SaveAsync(null, new("Vận hành", "Hướng dẫn khắc phục sự cố thiết bị.", "RESIDENT", null), Guid.NewGuid(), default);
        var draft = (await documents.GetAsync(id, true, default))!;
        await documents.SetPublicationAsync(id, draft.Document.Token, true, Guid.NewGuid(), default);
        var chunk = await db.Chunks.SingleAsync(x => x.DocumentId == id);
        chunk.SetEmbedding([1, 0, 0], "semantic-test:3"); await db.SaveChangesAsync();
        var search = new ResidentKnowledgeSearch(db, documents);
        var result = await search.SearchAsync("zzznomatchingkeyword", [1, 0, 0], "semantic-test:3", default);
        Assert.Equal(id, Assert.Single(result).DocumentId);
        Assert.Empty(await search.SearchAsync("zzznomatchingkeyword", [1, 0, 0], "different-model:3", default));
        await documents.SetPublicationAsync(id, (await documents.GetAsync(id, true, default))!.Document.Token, false, Guid.NewGuid(), default);
        Assert.Empty(await search.SearchAsync("zzznomatchingkeyword", [1, 0, 0], "semantic-test:3", default));
    }

    [Fact]
    public async Task IndexerNeverEmbedsDraftOrInternalDocuments()
    {
        await using var db = database.CreateDb(); var documents = new KnowledgeDocuments(db); var actor = Guid.NewGuid();
        var allowed = await documents.SaveAsync(null, new("Nguồn hợp lệ", "Cư dân đồng ý phí trước khi sửa chữa.", "RESIDENT", null), actor, default);
        var draft = (await documents.GetAsync(allowed, true, default))!;
        await documents.SetPublicationAsync(allowed, draft.Document.Token, true, actor, default);
        await documents.SaveAsync(null, new("Bản nháp", "DRAFT_SECRET", "RESIDENT", null), actor, default);
        var internalId = await documents.SaveAsync(null, new("Nội bộ", "INTERNAL_SECRET", "INTERNAL", null), actor, default);
        var internalDraft = (await documents.GetAsync(internalId, true, default))!;
        await documents.SetPublicationAsync(internalId, internalDraft.Document.Token, true, actor, default);
        var ai = new TestAi { Ready = true };
        await new KnowledgeIndex(db, ai).TickAsync(default);
        Assert.DoesNotContain("DRAFT_SECRET", string.Join(" ", ai.Inputs));
        Assert.DoesNotContain("INTERNAL_SECRET", string.Join(" ", ai.Inputs));
        Assert.Contains(await db.Chunks.Where(x => x.DocumentId == allowed).ToArrayAsync(), x => x.Embedding != null);
    }

    [Theory]
    [InlineData("RESIDENT", HttpStatusCode.OK)] [InlineData("MANAGER", HttpStatusCode.Forbidden)]
    [InlineData("ADMIN", HttpStatusCode.Forbidden)] [InlineData("STAFF", HttpStatusCode.Forbidden)]
    [InlineData("ACCOUNTANT", HttpStatusCode.Forbidden)] [InlineData("", HttpStatusCode.Unauthorized)]
    public async Task ChatEndpointsEnforceActorAndPermissions(string role, HttpStatusCode expected)
    {
        using var server = Server(); using var client = server.CreateClient();
        if (role.Length > 0) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        using var response = await client.PostAsJsonAsync("/api/v1/resident/chatbot/ask", new AskResidentChat("zzznomatchingkeyword"));
        Assert.Equal(expected, response.StatusCode);
        using var status = await client.GetAsync("/api/v1/resident/chatbot/status"); Assert.Equal(expected, status.StatusCode);
        using var source = await client.GetAsync($"/api/v1/resident/chatbot/sources/{Guid.NewGuid()}/1");
        Assert.Equal(expected == HttpStatusCode.OK ? HttpStatusCode.NotFound : expected, source.StatusCode);
    }

    [Fact]
    public async Task InactiveResidencyCannotAskOrReadCitedSource()
    {
        using var server = Server(); using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "RESIDENT"); client.DefaultRequestHeaders.Add("X-Test-Inactive", "1");
        using var reply = await client.PostAsJsonAsync("/api/v1/resident/chatbot/ask", new AskResidentChat("Nội quy là gì?"));
        Assert.Equal(HttpStatusCode.Forbidden, reply.StatusCode);
        using var source = await client.GetAsync($"/api/v1/resident/chatbot/sources/{Guid.NewGuid()}/1");
        Assert.Equal(HttpStatusCode.Forbidden, source.StatusCode);
    }

    private TestServer Server() => new(new WebHostBuilder().ConfigureLogging(l => l.ClearProviders()).ConfigureServices(services =>
    {
        services.AddAuthentication("ChatTest").AddScheme<AuthenticationSchemeOptions, TestAuth>("ChatTest", _ => { });
        services.AddAuthorization(options => options.AddPolicy(ResidentChatPolicy.Use, p => p.RequireAssertion(c => ResidentChatPolicy.Allowed(c.User))));
        services.AddRateLimiter(options => options.AddFixedWindowLimiter(ResidentChatPolicy.RateLimit, limiter => { limiter.PermitLimit = 20; limiter.Window = TimeSpan.FromMinutes(1); }));
        services.AddControllers().ConfigureApplicationPartManager(m => m.ApplicationParts.Clear()).AddApplicationPart(typeof(ResidentChatController).Assembly);
        services.AddScoped(_ => database.CreateDb());
        services.AddScoped<IPublishedKnowledgeSource, KnowledgeDocuments>();
        services.AddScoped<IResidentKnowledgeSearch, ResidentKnowledgeSearch>();
        services.AddSingleton<IChatAi>(new TestAi()); services.AddHttpContextAccessor();
        services.AddScoped<IChatResidentScope, TestScope>(); services.AddScoped<ResidentChat>();
    }).Configure(app => { app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter(); app.UseEndpoints(e => e.MapControllers()); }));
    private sealed class TestScope(IHttpContextAccessor accessor) : IChatResidentScope
    { public Task<bool> ActiveAsync(Guid actor, CancellationToken ct) => Task.FromResult(accessor.HttpContext!.Request.Headers["X-Test-Inactive"] != "1"); }
    private sealed class TestAi : IChatAi
    {
        public bool Ready { get; set; }
        public string EmbeddingKey => "index-test:2";
        public List<string> Inputs = [];
        public Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
        { Inputs.AddRange(texts); return Task.FromResult(texts.Select(_ => new float[] { 1, 0 }).ToArray()); }
        public Task<GroundedAnswer?> AnswerAsync(string question, IReadOnlyList<RetrievedPassage> passages, CancellationToken ct) => Task.FromResult<GroundedAnswer?>(null);
    }
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].FirstOrDefault();
            if (role is null) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new[] { new Claim("sub", Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, role), new Claim("permission", "USE_RESIDENT_SERVICES") };
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }
}
