using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Logging;
using PropFlow.Modules.Authentication.Presentation;

namespace PropFlow.Modules.Authentication.Infrastructure;

// Route/action metadata only: never request bodies, cookies, headers or query strings.
// The deployment log sink must enforce the approved 30-day security retention policy.
public sealed class AuthSecurityEvents(RequestDelegate next, ILogger<AuthSecurityEvents> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (action?.ControllerTypeInfo.AsType() == typeof(AuthController) && context.Request.Method != "GET")
        {
            context.Response.OnCompleted(() =>
            {
                logger.LogInformation("Authentication event {Action} returned {Status}; account {AccountId}; trace {TraceId}.",
                    action.ActionName, context.Response.StatusCode,
                    context.Items["AuthAuditUserId"] ?? context.User.FindFirst("sub")?.Value,
                    context.TraceIdentifier);
                return Task.CompletedTask;
            });
        }
        await next(context);
    }
}
