using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.AiChatbot.Domain;
using PropFlow.Modules.AiChatbot.Infrastructure.Persistence;

namespace PropFlow.ArchitectureTests;

public sealed class KnowledgeModuleTests
{
    [Fact]
    public void KnowledgePersistenceOwnsOnlyItsSchemaAndUsesConcurrencyAndAuditConstraints()
    {
        using var db = new AiChatbotDbContext(new DbContextOptionsBuilder<AiChatbotDbContext>()
            .UseNpgsql("Host=localhost;Database=dummy;Username=dummy;Password=dummy").Options);
        Assert.Equal("ai_chatbot", db.Model.GetDefaultSchema());
        Assert.Equal(4, db.Model.GetEntityTypes().Count());
        Assert.All(db.Model.GetEntityTypes(), entity => Assert.StartsWith("PropFlow.Modules.AiChatbot.Domain", entity.ClrType.Namespace));
        Assert.True(db.Model.FindEntityType(typeof(KnowledgeDocument))!.FindProperty(nameof(KnowledgeDocument.Token))!.IsConcurrencyToken);
        Assert.Equal(2, db.Model.FindEntityType(typeof(KnowledgeRevision))!.FindPrimaryKey()!.Properties.Count);
        Assert.Single(db.Model.FindEntityType(typeof(KnowledgeAudit))!.GetForeignKeys());
    }
}
