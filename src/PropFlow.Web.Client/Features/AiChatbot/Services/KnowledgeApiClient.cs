using PropFlow.Modules.AiChatbot.Contracts;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.AiChatbot.Services;

public sealed class KnowledgeApiClient(AuthenticatedApiClient api)
{
    private const string Path = "api/v1/knowledge/documents";
    public Task<ApiResult<KnowledgeDocumentPage>> ListAsync(string search, int page, CancellationToken ct) =>
        api.SendAsync<KnowledgeDocumentPage>(HttpMethod.Get, $"{Path}?page={page}&search={Uri.EscapeDataString(search)}", ct: ct);
    public Task<ApiResult<KnowledgeDocumentDetail>> GetAsync(Guid id, CancellationToken ct) =>
        api.SendAsync<KnowledgeDocumentDetail>(HttpMethod.Get, $"{Path}/{id}", ct: ct);
    public Task<ApiResult<KnowledgeIndexStatus>> IndexAsync(Guid id, CancellationToken ct) =>
        api.SendAsync<KnowledgeIndexStatus>(HttpMethod.Get, $"{Path}/{id}/index", ct: ct);
    public Task<ApiResult<Guid>> SaveAsync(Guid? id, SaveKnowledgeDocument request, CancellationToken ct) =>
        api.SendAsync<Guid>(id is null ? HttpMethod.Post : HttpMethod.Put, id is null ? Path : $"{Path}/{id}", request, ct: ct);
    public Task<ApiResult<Guid>> PublicationAsync(Guid id, Guid token, bool publish, CancellationToken ct) =>
        api.SendAsync<Guid>(HttpMethod.Post, $"{Path}/{id}/{(publish ? "publication" : "unpublish")}", new ChangeKnowledgePublication(token), ct: ct);
}
