using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PropFlow.Modules.AiChatbot.Infrastructure.Persistence;

namespace PropFlow.Api.Composition;

public sealed class AiChatbotDesignTimeFactory : IDesignTimeDbContextFactory<AiChatbotDbContext>
{
    public AiChatbotDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder().AddUserSecrets<Program>().AddEnvironmentVariables().Build();
        var connection = configuration.GetConnectionString("PropFlowDatabase")
            ?? throw new InvalidOperationException("Cần cấu hình ConnectionStrings:PropFlowDatabase.");
        return new(new DbContextOptionsBuilder<AiChatbotDbContext>().UseNpgsql(connection,
            options => options.MigrationsHistoryTable("__EFMigrationsHistory", "ai_chatbot")).Options);
    }
}
