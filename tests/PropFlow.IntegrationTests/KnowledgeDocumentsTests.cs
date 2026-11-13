using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using PropFlow.Modules.AiChatbot.Application;
using PropFlow.Modules.AiChatbot.Contracts;
using PropFlow.Modules.AiChatbot.Infrastructure;
using PropFlow.Modules.AiChatbot.Infrastructure.Persistence;
using PropFlow.Modules.AiChatbot.Presentation;

namespace PropFlow.IntegrationTests;

public sealed class KnowledgeDatabaseFixture : IAsyncLifetime
{
    private readonly string name = "propflow_fe12_docs_" + Guid.NewGuid().ToString("N");
    private string connection = "", adminConnection = "";
    public async Task InitializeAsync()
    {
        var source = new NpgsqlConnectionStringBuilder(TestDatabaseConfiguration.GetConnectionString());
        if (source.Database != "propflow_test") throw new InvalidOperationException("Unexpected test database source.");
        source.Database = "postgres"; source.Pooling = false; adminConnection = source.ConnectionString;
        await using var admin = new NpgsqlConnection(adminConnection); await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin); await create.ExecuteNonQueryAsync();
        source.Database = name; connection = source.ConnectionString;
        await using var db = CreateDb(); await db.Database.MigrateAsync();
    }
    public AiChatbotDbContext CreateDb() => new(new DbContextOptionsBuilder<AiChatbotDbContext>()
        .UseNpgsql(connection, o => o.MigrationsHistoryTable("__EFMigrationsHistory", "ai_chatbot")).Options);
    public async Task DisposeAsync()
    {
        if (!name.StartsWith("propflow_fe12_docs_", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(adminConnection)) return;
        await using var admin = new NpgsqlConnection(adminConnection); await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync();
    }
}

[Trait("Feature", "FE-12-ScopeExtension-Knowledge")]
public sealed class KnowledgeDocumentsTests(KnowledgeDatabaseFixture database) : IClassFixture<KnowledgeDatabaseFixture>
{
    private const string Path = "/api/v1/knowledge/documents";
    [Fact]
    public async Task PublishedSqlSourcesExcludeDraftAndInternalAndRevokedVersions()
    {
        await using var db = database.CreateDb(); var service = new KnowledgeDocuments(db); var actor = Guid.NewGuid();
        var id = await service.SaveAsync(null, new("Nội quy", "Công khai", "RESIDENT", null), actor, default);
        Assert.Null(await service.GetAsync(id, false, default));
        var draft = (await service.GetAsync(id, true, default))!;
        await service.SetPublicationAsync(id, draft.Document.Token, true, actor, default);
        draft = (await service.GetAsync(id, true, default))!;
        await service.SaveAsync(id, new("Nội bộ", "Chỉ cho nhân sự", "INTERNAL", draft.Document.Token), actor, default);
        Assert.Equal("Công khai", (await service.GetAsync(id, false, default))!.Revision.Content);
        Assert.Equal("Công khai", (await service.FindResidentSourceAsync(id, default))!.Content);
        draft = (await service.GetAsync(id, true, default))!;
        await service.SetPublicationAsync(id, draft.Document.Token, true, actor, default);
        Assert.Null(await service.FindResidentSourceAsync(id, default));
        Assert.Equal("Chỉ cho nhân sự", (await service.GetAsync(id, false, default))!.Revision.Content);
        draft = (await service.GetAsync(id, true, default))!;
        await service.SetPublicationAsync(id, draft.Document.Token, false, actor, default);
        Assert.Null(await service.GetAsync(id, false, default));
        Assert.Equal(2, await db.Revisions.CountAsync(x => x.DocumentId == id));
        Assert.Equal(5, await db.Activities.CountAsync(x => x.DocumentId == id));
    }

    [Fact]
    public async Task SimultaneousEditorsCommitExactlyOneRevisionAndAudit()
    {
        Guid id, token;
        await using (var setup = database.CreateDb())
        {
            var service = new KnowledgeDocuments(setup);
            id = await service.SaveAsync(null, new("Nội quy", "Gốc", "RESIDENT", null), Guid.NewGuid(), default);
            token = (await service.GetAsync(id, true, default))!.Document.Token;
        }
        await using var first = database.CreateDb(); await using var second = database.CreateDb();
        // Both contexts hold the same original token before either write starts.
        await first.Documents.SingleAsync(x => x.Id == id); await second.Documents.SingleAsync(x => x.Id == id);
        async Task<bool> Save(AiChatbotDbContext db, string content)
        {
            try { await new KnowledgeDocuments(db).SaveAsync(id, new("Nội quy", content, "RESIDENT", token), Guid.NewGuid(), default); return true; }
            catch (KnowledgeConflictException) { return false; }
        }
        var results = await Task.WhenAll(Save(first, "Thay đổi A"), Save(second, "Thay đổi B"));
        Assert.Single(results.Where(x => x));
        await using var check = database.CreateDb();
        Assert.Equal(2, await check.Revisions.CountAsync(x => x.DocumentId == id));
        Assert.Equal(2, await check.Activities.CountAsync(x => x.DocumentId == id));
    }

    [Theory]
    [InlineData("ADMIN", true, true)] [InlineData("MANAGER", true, true)]
    [InlineData("STAFF", true, false)] [InlineData("RESIDENT", false, false)]
    [InlineData("ACCOUNTANT", false, false)] [InlineData("", false, false)]
    public async Task HttpApiEnforcesReadAndWritePolicies(string role, bool read, bool write)
    {
        using var server = Server(); using var client = server.CreateClient();
        if (role.Length > 0) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        using var list = await client.GetAsync(Path);
        Assert.Equal(read ? HttpStatusCode.OK : role.Length == 0 ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, list.StatusCode);
        using var create = await client.PostAsJsonAsync(Path, new SaveKnowledgeDocument("Kiểm tra HTTP", "Nội dung", "RESIDENT", null));
        Assert.Equal(write ? HttpStatusCode.OK : role.Length == 0 ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, create.StatusCode);
        foreach (var operation in new[] { "publication", "unpublish" })
        {
            using var response = await client.PostAsJsonAsync($"{Path}/{Guid.NewGuid()}/{operation}", new ChangeKnowledgePublication(Guid.NewGuid()));
            Assert.Equal(write ? HttpStatusCode.NotFound : role.Length == 0 ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    private TestServer Server() => new(new WebHostBuilder().ConfigureLogging(l => l.ClearProviders()).ConfigureServices(services =>
    {
        services.AddAuthentication("KnowledgeTest").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("KnowledgeTest", _ => { });
        services.AddAuthorization(options =>
        {
            options.AddPolicy(KnowledgePolicies.Read, p => p.RequireAssertion(c => KnowledgePolicies.CanRead(c.User)));
            options.AddPolicy(KnowledgePolicies.Manage, p => p.RequireAssertion(c => KnowledgePolicies.CanManage(c.User)));
        });
        services.AddControllers().ConfigureApplicationPartManager(m => m.ApplicationParts.Clear())
            .AddApplicationPart(typeof(KnowledgeDocumentsController).Assembly);
        services.AddScoped(_ => database.CreateDb()); services.AddScoped<IKnowledgeDocuments, KnowledgeDocuments>();
    }).Configure(app =>
    {
        app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.UseEndpoints(e => e.MapControllers());
    }));

    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].FirstOrDefault();
            if (role is null) return Task.FromResult(AuthenticateResult.NoResult());
            var permission = role switch { "ADMIN" => "MANAGE_INTERNAL_ACCOUNTS", "MANAGER" => "MANAGE_OPERATIONS", "STAFF" => "PERFORM_ASSIGNED_OPERATIONS", _ => "USE_RESIDENT_SERVICES" };
            var user = new ClaimsPrincipal(new ClaimsIdentity([new("sub", Guid.NewGuid().ToString()), new(ClaimTypes.Role, role), new("permission", permission)], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new(user, Scheme.Name)));
        }
    }
}
