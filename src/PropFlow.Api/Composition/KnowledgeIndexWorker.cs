using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Modules.AiChatbot.Infrastructure;
using PropFlow.Modules.AiChatbot.Infrastructure.Persistence;

namespace PropFlow.Api.Composition;

public sealed class KnowledgeIndexWorker(IServiceScopeFactory scopes, IHostEnvironment environment, ILogger<KnowledgeIndexWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (environment.IsEnvironment("Testing")) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AiChatbotDbContext>();
                await db.Database.OpenConnectionAsync(stoppingToken);
                var connection = (NpgsqlConnection)db.Database.GetDbConnection();
                await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(71241012)", connection);
                if (await command.ExecuteScalarAsync(stoppingToken) is true)
                    try { await scope.ServiceProvider.GetRequiredService<KnowledgeIndex>().TickAsync(stoppingToken); }
                    finally
                    {
                        await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(71241012)", connection);
                        await release.ExecuteNonQueryAsync(CancellationToken.None);
                    }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) when (e is NpgsqlException or DbUpdateException or InvalidOperationException)
            { logger.LogWarning("Knowledge indexing deferred ({Kind}).", e.GetType().Name); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
