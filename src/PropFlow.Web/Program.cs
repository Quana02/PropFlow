using PropFlow.Web.Components;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Endpoints;

var builder = WebApplication.CreateBuilder(args);
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
