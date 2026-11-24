using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PropFlow.Modules.AiChatbot.Application;
using PropFlow.Modules.AiChatbot.Contracts;
using PropFlow.Modules.AiChatbot.Presentation;

namespace PropFlow.IntegrationTests;

[Trait("Feature", "FE-12"), Trait("UseCase", "FE-12-LiveDocumentLookup")]
public sealed class ResidentAssistantHttpTests
{
    [Fact]
    public async Task Live_document_lookup_preserves_role_scope_publication_and_never_calls_an_answer_model()
    {
        var fakes = new Knowledge(); var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(ResidentChatController).Assembly);
        builder.Services.AddAuthentication("AssistantTest").AddScheme<AuthenticationSchemeOptions, Authentication>("AssistantTest", _ => { });
        builder.Services.AddAuthorization(o => o.AddPolicy(ResidentChatPolicy.Use, p => p.RequireAssertion(c => ResidentChatPolicy.Allowed(c.User))));
        builder.Services.AddSingleton<IChatResidentScope>(fakes); builder.Services.AddSingleton<IResidentKnowledgeSearch>(fakes);
        builder.Services.AddSingleton<IChatAi>(fakes); builder.Services.AddScoped<ResidentChat>();
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync(); using var client = app.GetTestClient();
        const string route = "/api/v1/resident/chatbot/lookup";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(route, new { question = "Nội quy" })).StatusCode);
        client.DefaultRequestHeaders.Add("X-Actor", fakes.Resident.ToString());
        foreach (var role in new[] { "MANAGER", "STAFF", "ADMIN", "ACCOUNTANT" })
        {
            client.DefaultRequestHeaders.Remove("X-Role"); client.DefaultRequestHeaders.Add("X-Role", role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(route, new { question = "Nội quy" })).StatusCode);
        }
        client.DefaultRequestHeaders.Remove("X-Role"); client.DefaultRequestHeaders.Add("X-Role", "RESIDENT");
        var response = await client.PostAsJsonAsync(route, new { question = "Nội quy sử dụng tiện ích" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Single((await response.Content.ReadFromJsonAsync<ResidentChatReply>())!.Sources);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(route, new { question = "" })).StatusCode);
        fakes.Published = false;
        Assert.Empty((await (await client.PostAsJsonAsync(route, new { question = "Nội quy" })).Content.ReadFromJsonAsync<ResidentChatReply>())!.Sources);
        client.DefaultRequestHeaders.Remove("X-Actor"); client.DefaultRequestHeaders.Add("X-Actor", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(route, new { question = "Nội quy" })).StatusCode);
        await app.StopAsync();
    }
    private sealed class Knowledge : IChatResidentScope, IResidentKnowledgeSearch, IChatAi
    {
        public Guid Resident = Guid.NewGuid(); public bool Published = true;
        public bool Ready => false; public string EmbeddingKey => "not-used";
        public Task<bool> ActiveAsync(Guid actor, CancellationToken ct) => Task.FromResult(actor == Resident);
        public Task<IReadOnlyList<RetrievedPassage>> SearchAsync(string question, float[]? embedding, string key, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RetrievedPassage>>([new(Guid.NewGuid(), 1, 0, "Nội quy", "Không gây tiếng ồn sau 22 giờ.")]);
        public Task<bool> CurrentAsync(IReadOnlyList<RetrievedPassage> passages, CancellationToken ct) => Task.FromResult(Published);
        public Task<PublishedKnowledgeSource?> SourceAsync(Guid id, int revision, CancellationToken ct) => Task.FromResult<PublishedKnowledgeSource?>(null);
        public Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct, ChatEmbeddingPurpose purpose = ChatEmbeddingPurpose.Document) => throw new InvalidOperationException("Lookup must work without embedding.");
        public Task<GroundedAnswer?> AnswerAsync(string question, IReadOnlyList<RetrievedPassage> passages, CancellationToken ct) => throw new InvalidOperationException("Live lookup must not call another answer model.");
    }
    private sealed class Authentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Guid.TryParse(Request.Headers["X-Actor"], out var actor)) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new("sub", actor.ToString()), new(ClaimTypes.Role, Request.Headers["X-Role"].ToString()), new("permission", "USE_RESIDENT_SERVICES")], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
