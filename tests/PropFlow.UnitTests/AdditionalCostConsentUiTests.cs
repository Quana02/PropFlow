using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Presentation;
using PropFlow.Web.Client.Features.Resident.Portal.Components;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-05")]
public sealed class AdditionalCostConsentUiTests
{
    [Theory]
    [InlineData("PENDING", "Đồng ý chi phí và tiếp tục", true)]
    [InlineData("ACCEPTED", "Bạn đã đồng ý chi phí phát sinh", false)]
    [InlineData("REJECTED", "Yêu cầu dịch vụ đã được tự động từ chối", false)]
    public async Task ResidentSeesExactProposalAndOnlyPendingProposalHasDecisionButtons(string status, string expected, bool buttons)
    {
        using var services = new ServiceCollection().AddLogging().AddSingleton<AuthenticatedApiClient>(sp =>
            new(new HttpClient(), sp.GetRequiredService<ILogger<ApiClient>>())).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<ResidentAdditionalCostConsent>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["RequestId"] = Guid.NewGuid(), ["BaseCost"] = 0m,
                ["Consent"] = new AdditionalCostConsentView(Guid.NewGuid(), 50000, "Thay van nước", status, DateTimeOffset.UtcNow, null)
            }));
            return WebUtility.HtmlDecode(component.ToHtmlString());
        });
        Assert.Contains(expected, html); Assert.Contains("50.000 đ", html); Assert.Contains("Thay van nước", html);
        Assert.Equal(buttons, html.Contains("Từ chối dịch vụ"));
    }

    [Theory]
    [InlineData("Propose")]
    [InlineData("Respond")]
    public void EveryDecisionEndpointHasItsOwnActorPolicy(string action)
    {
        var policy = typeof(AdditionalCostConsentController).GetMethod(action)!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Policy;
        // Compare against the published permission policy names, not a generic authenticated-user requirement.
        Assert.False(string.IsNullOrWhiteSpace(policy));
        Assert.Equal(action == "Propose" ? ServiceRequestsAuthorizationPolicies.WorkAssigned : ServiceRequestsAuthorizationPolicies.SubmitAsResident, policy);
    }
}
