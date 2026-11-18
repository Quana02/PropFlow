using System.Security.Claims;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Api.Composition;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Tokens;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

internal static class AuthenticationTestSessions
{
    internal static HubConnection ConnectWebSocket(PropFlowApiFactory factory, string hub, string accessToken,
        bool negotiate = false) =>
        new HubConnectionBuilder().WithUrl($"https://localhost/hubs/{hub}", options =>
        {
            options.SkipNegotiation = !negotiate;
            options.Transports = HttpTransportType.WebSockets;
            options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
            options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            options.WebSocketFactory = async (context, ct) =>
            {
                var client = factory.Server.CreateWebSocketClient();
                client.ConfigureRequest = request => request.Headers.Authorization = $"Bearer {accessToken}";
                return await client.ConnectAsync(context.Uri, ct);
            };
        }).Build();

    internal static Task WhenClosed(HubConnection hub)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.Closed += _ => { closed.TrySetResult(); return Task.CompletedTask; };
        return closed.Task;
    }

    internal static async Task AwaitServerReadyAsync(HubConnection hub)
    {
        // StartAsync completes the client handshake before server lifetime registration is guaranteed.
        // A completion for a deliberately absent method is a setup barrier, with no business write.
        var completion = await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(() =>
            hub.InvokeAsync("PF04_UnregisteredMethod"));
        Assert.Contains("PF04_UnregisteredMethod", completion.Message);
    }

    internal static async Task<SessionResponse> IssueAsync(IServiceProvider services, AccountResponse account, DateTimeOffset now)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var secrets = scope.ServiceProvider.GetRequiredService<IAuthSecrets>();
        var sessionNow = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        var token = new RefreshToken(account.Id, secrets.HashToken(secrets.NewToken()), sessionNow.AddDays(1), sessionNow,
            accessFingerprint: SessionAccess.Fingerprint(account.Role, account.Permissions));
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();
        return secrets.Issue(account, now, token.SessionId);
    }
}

internal sealed class TestRealtimeSessionValidation(string authenticationType) : IRealtimeSessionValidation
{
    public Task<HashSet<ClaimsPrincipal>> CurrentAsync(IReadOnlyCollection<ClaimsPrincipal> principals, CancellationToken ct) =>
        Task.FromResult(principals.Where(principal => principal.Identity?.AuthenticationType == authenticationType).ToHashSet());
}
