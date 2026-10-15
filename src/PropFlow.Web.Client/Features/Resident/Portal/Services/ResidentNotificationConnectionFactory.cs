using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using PropFlow.Web.Client.Services.Authentication;

namespace PropFlow.Web.Client.Features.Resident.Portal.Services;

public sealed class ResidentNotificationConnectionFactory(IConfiguration configuration, NavigationManager navigation, AuthSession session)
{
    public HubConnection Create()
    {
        var apiBase = new Uri(new Uri(navigation.BaseUri), configuration["Api:BaseUrl"]
            ?? throw new InvalidOperationException("Thiếu cấu hình Api:BaseUrl."));
        return new HubConnectionBuilder()
            .WithUrl(new Uri(apiBase, "hubs/resident-notifications"), options =>
                options.AccessTokenProvider = async () => {
                    await session.InitializeAsync();
                    if (session.AccessToken is { } token) await session.RefreshAsync(token);
                    return session.AccessToken;
                })
            .WithAutomaticReconnect(new ReconnectPolicy())
            .Build();
    }

    private sealed class ReconnectPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
            TimeSpan.FromSeconds(Math.Min(30, 2 + retryContext.PreviousRetryCount * 2));
    }
}
