using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Web.Client.Features.ServiceRequests.Services;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-05")]
public sealed class ManagerServiceRequestCacheTests
{
    [Fact]
    public async Task RepeatedManagerListGet_BypassesAStaleBrowserCache()
    {
        var handler = new BrowserLikeCachingHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://propflow.test/") };
        var client = new ManagerServiceRequestApiClient(
            new AuthenticatedApiClient(http, NullLogger<ApiClient>.Instance));
        var query = new ManagerServiceRequestListQuery(Page: 1, PageSize: 20);

        var first = await client.ListAsync(query);
        handler.ServerTotal = 6;
        var refreshed = await client.ListAsync(query);

        Assert.Equal(5, first.Data?.TotalCount);
        Assert.Equal(6, refreshed.Data?.TotalCount);
    }

    private sealed class BrowserLikeCachingHandler : HttpMessageHandler
    {
        private string? cachedBody;
        public int ServerTotal { get; set; } = 5;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var bypass = request.Headers.CacheControl?.NoCache == true ||
                         request.Headers.CacheControl?.NoStore == true;
            if (bypass || cachedBody is null)
                cachedBody = Body(ServerTotal);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(cachedBody, Encoding.UTF8, "application/json")
            });
        }

        private static string Body(int total) => JsonSerializer.Serialize(new
        {
            items = Array.Empty<object>(),
            totalCount = total,
            page = 1,
            pageSize = 20,
            summary = new { total, submitted = Math.Max(0, total - 5), inProgress = 2, completed = 3 }
        });
    }
}
