using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ApiResidents = PropFlow.Modules.Residents.Presentation;
using PropFlow.Web.Client.Features.Resident.Services;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.UnitTests;

public sealed class ResidentApiContractTests
{
    [Fact]
    public async Task ListAsync_Deserializes_non_empty_resident_response()
    {
        var residentId = Guid.NewGuid();
        var apiResponse = new ApiResidents.PagedResidentsResponse(
            [new ApiResidents.ResidentListItem(residentId, "RES-000001", "Resident One", "0900000000",
                "resident@example.test", "ACTIVE", false, [])], 1, 1, 20);
        var json = JsonSerializer.Serialize(apiResponse, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var http = new HttpClient(new Transport(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        })) { BaseAddress = new Uri("https://test.invalid/") };
        var api = new ResidentApiClient(new AuthenticatedApiClient(http, NullLogger<ApiClient>.Instance));

        var result = await api.ListAsync(null, null);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("ACTIVE", Assert.Single(result.Data!.Items).Status);
    }

    private sealed class Transport(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }
}
