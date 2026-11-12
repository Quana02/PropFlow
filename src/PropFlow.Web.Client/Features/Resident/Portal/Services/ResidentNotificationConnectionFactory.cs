using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using PropFlow.Web.Client.Services.Authentication;

namespace PropFlow.Web.Client.Features.Resident.Portal.Services;

public sealed class ResidentNotificationConnectionFactory(IConfiguration configuration, NavigationManager navigation, AuthSession session,
    ILogger<ResidentNotificationConnectionFactory>? logger = null)
{
    public HubConnection Create()
    {
        var apiBase = new Uri(new Uri(navigation.BaseUri), configuration["Api:BaseUrl"]
            ?? throw new InvalidOperationException("Thiếu cấu hình Api:BaseUrl."));
        return new HubConnectionBuilder()
            .WithUrl(new Uri(apiBase, "hubs/notifications"), options =>
                options.AccessTokenProvider = session.GetValidAccessTokenAsync)
            .WithAutomaticReconnect(new ReconnectPolicy())
            .Build();
    }

    public async Task StartAsync(HubConnection connection, CancellationToken ct)
    {
        var attempt = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await connection.StartAsync(ct);
                return;
            }
            catch (Exception exception) when (!ct.IsCancellationRequested)
            {
                if (attempt == 0)
                    logger?.LogWarning("Notification realtime connection failed ({ErrorType}); retrying.", exception.GetType().Name);
            }

            await Task.Delay(RetryDelay(attempt++), ct);
        }
    }

    private static TimeSpan RetryDelay(long attempt) => TimeSpan.FromSeconds(2 + Math.Min(14, attempt) * 2);

    private sealed class ReconnectPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext) => RetryDelay(retryContext.PreviousRetryCount);
    }
}
