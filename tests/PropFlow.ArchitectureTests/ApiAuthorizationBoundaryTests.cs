using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using PropFlow.Modules.Authentication.Presentation;
using PropFlow.Modules.Payments.Presentation;

namespace PropFlow.ArchitectureTests;

public sealed class ApiAuthorizationBoundaryTests
{
    [Fact]
    public void ControllerActions_RequireAuthorizationExceptDocumentedPublicEntrypoints()
    {
        var api = typeof(Program).Assembly;
        var parts = new ApplicationPartManager();
        parts.FeatureProviders.Add(new ControllerFeatureProvider());
        parts.ApplicationParts.Add(new AssemblyPart(api));
        foreach (var part in api.GetCustomAttributes<ApplicationPartAttribute>())
            parts.ApplicationParts.Add(new AssemblyPart(Assembly.Load(part.AssemblyName)));
        var feature = new ControllerFeature();
        parts.PopulateFeature(feature);

        var expectedPublic = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(AuthController.Csrf), nameof(AuthController.Register), nameof(AuthController.Resume),
            nameof(AuthController.Activate), nameof(AuthController.Login), nameof(AuthController.Refresh),
            nameof(AuthController.Logout), nameof(AuthController.Forgot), nameof(AuthController.VerifyRecovery),
            nameof(AuthController.Reset)
        };
        var seenPublic = new HashSet<string>(StringComparer.Ordinal);
        var webhookIsPublic = false;

        foreach (var controller in feature.Controllers)
        {
            foreach (var action in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                         .Where(method => !method.IsSpecialName && !method.IsDefined(typeof(NonActionAttribute), inherit: true)))
            {
                var attributes = controller.GetCustomAttributes(inherit: true)
                    .Concat(action.GetCustomAttributes(inherit: true)).ToArray();
                var label = $"{controller.Name}.{action.Name}";
                if (attributes.OfType<IAllowAnonymous>().Any())
                {
                    if (controller.AsType() == typeof(AuthController))
                    {
                        Assert.Contains(action.Name, expectedPublic);
                        seenPublic.Add(action.Name);
                    }
                    else
                    {
                        Assert.Equal(typeof(SePayWebhookController), controller.AsType());
                        Assert.Equal(nameof(SePayWebhookController.Receive), action.Name);
                        webhookIsPublic = true;
                    }
                }
                else
                {
                    Assert.True(attributes.OfType<IAuthorizeData>().Any(), $"Action thiếu khai báo quyền: {label}");
                }
            }
        }

        Assert.True(seenPublic.SetEquals(expectedPublic));
        Assert.True(webhookIsPublic);
    }
}
