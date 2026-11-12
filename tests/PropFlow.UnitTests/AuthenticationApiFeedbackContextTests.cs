using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropFlow.Web.Client.Features.Authentication.Components;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Shared.Feedback;
namespace PropFlow.UnitTests;
public sealed class AuthenticationApiFeedbackContextTests
{
    [Theory]
    [InlineData("current")] [InlineData("navigation")] [InlineData("dispose")]
    public async Task Lifecycle_feedback_uses_the_context_of_its_api_request_even_if_rendering_is_late(string state)
    {
        await using var services = new ServiceCollection().AddLogging().AddSingleton<ToastService>().BuildServiceProvider();
        var toasts = services.GetRequiredService<ToastService>(); using var owner = new ToastLifetime();
        var operation = toasts.BeginOperation(owner);
        if (state == "navigation") toasts.NavigationChanged("new");
        if (state == "dispose") owner.Dispose();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<ApiFeedback>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(ApiFeedback.Result)] = new ApiResult(false, StatusCode: 500, Message: "Phản hồi API"),
            [nameof(ApiFeedback.Operation)] = operation
        })));
        if (state == "current") Assert.Equal("Phản hồi API", Assert.Single(toasts.Messages).Message);
        else Assert.Empty(toasts.Messages);
    }
}
