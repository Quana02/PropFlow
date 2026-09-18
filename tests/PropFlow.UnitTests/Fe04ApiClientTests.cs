using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Web.Client.Features.Facility.Models;
using PropFlow.Web.Client.Features.Facility.Services;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.UnitTests;

public sealed class Fe04ApiClientTests
{
    private sealed class Transport(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }

    [Fact]
    public async Task Building_list_maps_forbidden_to_api_result()
    {
        var api = CreateApi(request =>
        {
            Assert.Equal("/api/v1/buildings", request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        });

        var result = await new BuildingApiClient(api).GetBuildingsAsync(new BuildingFilterModel());

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task Equipment_status_uses_typed_patch_and_propagates_validation_error()
    {
        var id = Guid.NewGuid();
        var api = CreateApi(request =>
        {
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal($"/api/v1/equipments/{id}/status", request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"title\":\"Trạng thái không hợp lệ\",\"code\":\"invalid_status\"}", Encoding.UTF8, "application/problem+json")
            };
        });

        var result = await new EquipmentApiClient(api).SetEquipmentStatusAsync(id, EquipmentStatus.ACTIVE);

        Assert.False(result.IsSuccess);
        Assert.Equal("invalid_status", result.Code);
        Assert.Equal("Trạng thái không hợp lệ", result.Message);
    }

    private static AuthenticatedApiClient CreateApi(Func<HttpRequestMessage, HttpResponseMessage> send) =>
        new(new HttpClient(new Transport(send)) { BaseAddress = new Uri("https://test.invalid/") }, NullLogger<ApiClient>.Instance);
}
