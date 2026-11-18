using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.Extensions.DependencyInjection;

namespace PropFlow.IntegrationTests;

public sealed class HttpInventoryTests(PropFlowApiFactory factory) : IClassFixture<PropFlowApiFactory>
{
    [Fact]
    public void Static_inventory_matches_all_actual_Mvc_attribute_route_method_keys()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PropFlow.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var actions = factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>().ToArray();
        var actual = actions.SelectMany(action => action.ActionConstraints!.OfType<HttpMethodActionConstraint>()
                .SelectMany(constraint => constraint.HttpMethods.Select(method => method + " /" + action.AttributeRouteInfo!.Template!.TrimStart('/'))))
            .Distinct().Order(StringComparer.Ordinal).ToArray();
        var inventoryPath = Path.Combine(root.FullName, "artifacts/authz/endpoints.json");
        using var json = JsonDocument.Parse(File.ReadAllText(inventoryPath));
        var expected = json.RootElement.EnumerateArray().Select(row => row.GetProperty("method").GetString() + " " + row.GetProperty("path").GetString())
            .Order(StringComparer.Ordinal).ToArray();
        File.WriteAllText(Path.Combine(root.FullName, "artifacts/authz/stabilize-runtime-http-keys.json"), JsonSerializer.Serialize(actual));
        Assert.Equal(actual, expected);
    }
}
