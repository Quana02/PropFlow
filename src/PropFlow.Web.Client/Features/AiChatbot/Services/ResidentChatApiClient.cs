using PropFlow.Modules.AiChatbot.Contracts;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.AiChatbot.Services;

public sealed class ResidentChatApiClient(AuthenticatedApiClient api)
{
    private const string Root = "api/v1/resident/chatbot";
    public Task<ApiResult<ResidentChatStatus>> StatusAsync(CancellationToken ct) => api.SendAsync<ResidentChatStatus>(HttpMethod.Get, Root + "/status", ct: ct);
    public Task<ApiResult<ResidentChatReply>> AskAsync(string question, CancellationToken ct) =>
        api.SendAsync<ResidentChatReply>(HttpMethod.Post, Root + "/ask", new AskResidentChat(question), ct: ct);
    public Task<ApiResult<PublishedKnowledgeSource>> SourceAsync(Guid id, int revision, CancellationToken ct) =>
        api.SendAsync<PublishedKnowledgeSource>(HttpMethod.Get, $"{Root}/sources/{id}/{revision}", ct: ct);
}
