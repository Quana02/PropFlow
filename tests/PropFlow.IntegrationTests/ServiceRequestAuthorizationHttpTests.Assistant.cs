using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed partial class ServiceRequestAuthorizationHttpTests
{
    [Fact, Trait("UseCase", "FE-05.1")]
    public async Task Repeated_assistant_submissions_create_one_request_activity_and_receipt()
    {
        const string route = "/api/v1/resident/service-requests";
        var body = new CreateResidentServiceRequestRequest(null, null, "Vòi nước trong bếp bị rò liên tục, nhờ BQL kiểm tra.", null, null, null, null, SubmissionId: Guid.NewGuid());
        var responses = await Task.WhenAll(residentA.Client.PostAsJsonAsync(route, body), residentA.Client.PostAsJsonAsync(route, body));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var one = (await responses[0].Content.ReadFromJsonAsync<ResidentServiceRequestCreatedResponse>())!;
        var two = (await responses[1].Content.ReadFromJsonAsync<ResidentServiceRequestCreatedResponse>())!;
        Assert.Equal(one.Id, two.Id);
        await using var scope = host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ServiceRequestsDbContext>();
        Assert.Equal(1, await db.ServiceRequests.CountAsync(x => x.RequestNumber == one.RequestNumber));
        Assert.Equal(1, await db.ServiceRequestActivities.CountAsync(x => x.ServiceRequestId == one.Id));
        Assert.Equal(HttpStatusCode.Conflict, (await residentA.Client.PostAsJsonAsync(route, body with { Description = "Vòi nước trong phòng tắm bị rò." })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.Client.PostAsJsonAsync(route, body)).StatusCode);
        foreach (var response in responses) response.Dispose();
    }
}
