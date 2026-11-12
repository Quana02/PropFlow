using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Web.Client.Features.Resident.Portal.Services;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-05")]
[Trait("UseCase", "FE-05.4")]
public sealed class ResidentServiceRequestApiClientTests
{
    [Fact]
    public async Task ListAsync_SendsEncodedSearchStatusAndDateFilters()
    {
        var handler = new CapturingHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://propflow.test/") };
        var client = new ResidentServiceRequestApiClient(
            new AuthenticatedApiClient(http, NullLogger<ApiClient>.Instance));

        var result = await client.ListAsync(new ResidentServiceRequestListQuery(
            "rò nước",
            "IN_PROGRESS",
            new DateOnly(2026, 10, 8),
            new DateOnly(2026, 10, 9)));

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "/api/v1/resident/service-requests?search=r%C3%B2%20n%C6%B0%E1%BB%9Bc&status=IN_PROGRESS&fromDate=2026-10-08&toDate=2026-10-09",
            handler.RequestUri?.PathAndQuery);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
        }
    }
}
