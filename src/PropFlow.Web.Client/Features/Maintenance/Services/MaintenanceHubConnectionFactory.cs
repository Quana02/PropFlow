using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using PropFlow.Web.Client.Services.Authentication;

namespace PropFlow.Web.Client.Features.Maintenance.Services;

public sealed class MaintenanceHubConnectionFactory(IConfiguration configuration, NavigationManager navigation, AuthSession session)
{
    public HubConnection Create()
    {
        var configuredApiBase = configuration["Api:BaseUrl"]
            ?? throw new InvalidOperationException("Thiếu cấu hình Api:BaseUrl.");
        var apiBase = new Uri(new Uri(navigation.BaseUri), configuredApiBase);

        return session.ObserveTransport(new HubConnectionBuilder()
            .WithUrl(new Uri(apiBase, "hubs/maintenance"), options =>
                options.AccessTokenProvider = session.GetValidAccessTokenAsync)
            .WithAutomaticReconnect()
            .Build());
    }
}
