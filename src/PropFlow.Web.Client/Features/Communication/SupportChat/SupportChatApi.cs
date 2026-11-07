using PropFlow.Modules.Communication.Contracts;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.Communication.SupportChat;

public sealed class SupportChatApi(AuthenticatedApiClient api)
{
    private const string Root = "api/v1/support-chat";
    public Task<ApiResult<SupportConversationPage>> List(int page, CancellationToken ct) => api.SendAsync<SupportConversationPage>(HttpMethod.Get, $"{Root}/conversations?page={page}", ct: ct);
    public Task<ApiResult<SupportConversationView>> Get(Guid id, CancellationToken ct) => api.SendAsync<SupportConversationView>(HttpMethod.Get, $"{Root}/conversations/{id}", ct: ct);
    public Task<ApiResult<SupportMessagePage>> Messages(Guid id, long? before, CancellationToken ct) => api.SendAsync<SupportMessagePage>(HttpMethod.Get, $"{Root}/conversations/{id}/messages" + (before is null ? "" : $"?before={before}"), ct: ct);
    public Task<ApiResult<SupportConversationView>> Start(ChatSendRequest request, CancellationToken ct) => api.SendAsync<SupportConversationView>(HttpMethod.Post, $"{Root}/conversations", request, ct: ct);
    public Task<ApiResult<SupportMessageView>> Send(Guid id, ChatSendRequest request, CancellationToken ct) => api.SendAsync<SupportMessageView>(HttpMethod.Post, $"{Root}/conversations/{id}/messages", request, ct: ct);
    public Task<ApiResult<SupportConversationView>> Close(Guid id, CancellationToken ct) => api.SendAsync<SupportConversationView>(HttpMethod.Post, $"{Root}/conversations/{id}/close", ct: ct);
    public Task<ApiResult<SupportAvailabilityView>> Availability(CancellationToken ct) => api.SendAsync<SupportAvailabilityView>(HttpMethod.Get, $"{Root}/availability", ct: ct);
    public Task<ApiResult<SupportAvailabilityView>> SetAvailability(bool available, CancellationToken ct) => api.SendAsync<SupportAvailabilityView>(HttpMethod.Put, $"{Root}/availability", new ChatAvailabilityRequest(available), ct: ct);
}
