using PropFlow.Web.Components;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Components.Endpoints;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsEnvironment("Testing"))
{
    // The development host loads referenced WASM static-asset manifests automatically.
    // E2E uses Testing, so opt in only there; published/production asset behavior stays unchanged.
    builder.WebHost.UseStaticWebAssets();
    // Browser E2E hosts run in a restricted process profile.  Keep their ephemeral
    // key ring out of the user profile without changing any production key policy.
    var testKeyRing = builder.Configuration["E2E:DataProtectionKeyRing"];
    if (!string.IsNullOrWhiteSpace(testKeyRing))
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(testKeyRing))
            .SetApplicationName("PropFlow.E2E");
}
var knownProxyAddresses = builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
        Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    foreach (var address in knownProxyAddresses) options.KnownProxies.Add(System.Net.IPAddress.Parse(address));
});

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

var app = builder.Build();
if (knownProxyAddresses.Length > 0) app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

if (app.Environment.IsEnvironment("Testing"))
    app.MapGet("/__e2e/ready", () => Results.Text("ready", "text/plain"));

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(PropFlow.Web.Client._Imports).Assembly)
    .Add(endpoint =>
    {
        // These endpoints serve only the non-prerendered WASM shell. Their component
        // attributes remain intact for AuthorizeRouteView after client session restore.
        // The Web host has no bearer token and must not authenticate via the refresh cookie.
        var component = endpoint.Metadata.OfType<ComponentTypeMetadata>().LastOrDefault();
        if (component?.Type.Assembly == typeof(PropFlow.Web.Client._Imports).Assembly)
            for (var i = endpoint.Metadata.Count - 1; i >= 0; i--)
                if (endpoint.Metadata[i] is IAuthorizeData) endpoint.Metadata.RemoveAt(i);
    });

app.Run();
