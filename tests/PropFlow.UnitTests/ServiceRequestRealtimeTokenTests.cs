using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Web.Client.Features.ServiceRequests.Services;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Services.Authentication;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-05")]
public sealed class ServiceRequestRealtimeTokenTests
{
    [Fact]
    public async Task HubHandshake_UsesCurrentAccessToken_WithoutRotatingTheSession()
    {
        var refreshCalls = 0;
        var session = new AuthSession(new ApiClient(
            new HttpClient(new Transport(request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/csrf", StringComparison.Ordinal))
                    return new(HttpStatusCode.OK) { Content = JsonContent.Create(new CsrfResponse("csrf")) };

                refreshCalls++;
                return new(HttpStatusCode.Unauthorized);
            })) { BaseAddress = new Uri("https://api.test/") },
            NullLogger<ApiClient>.Instance));
        session.Accept(Session("current-token"));

        await using var realtime = new ServiceRequestRealtimeClient(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Api:BaseUrl"] = "https://api.test/"
            }).Build(),
            new Navigation(),
            session,
            NullLogger<ServiceRequestRealtimeClient>.Instance);

        var method = typeof(ServiceRequestRealtimeClient).GetMethod(
            "GetAccessTokenAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var token = await (Task<string?>)method.Invoke(realtime, null)!;

        Assert.Equal("current-token", token);
        Assert.Equal(0, refreshCalls);
        Assert.Equal("current-token", session.AccessToken);
    }

    private static SessionResponse Session(string token) => new(
        token,
        DateTimeOffset.UtcNow.AddMinutes(15),
        new AccountResponse(
            Guid.NewGuid(), "manager", "Quản lý", "manager@example.test", null,
            "ACTIVE", true, "MANAGER", ["operations.manage"]));

    private sealed class Transport(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(send(request));
    }

    private sealed class Navigation : NavigationManager
    {
        public Navigation() => Initialize("https://web.test/", "https://web.test/manager/service-requests");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
}
