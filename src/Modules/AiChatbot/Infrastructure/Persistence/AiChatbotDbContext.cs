using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.AiChatbot.Domain;

namespace PropFlow.Modules.AiChatbot.Infrastructure.Persistence;

public sealed class AiChatbotDbContext(DbContextOptions<AiChatbotDbContext> options) : DbContext(options)
{
    public DbSet<KnowledgeDocument> Documents => Set<KnowledgeDocument>();
    public DbSet<KnowledgeRevision> Revisions => Set<KnowledgeRevision>();
    public DbSet<KnowledgeAudit> Activities => Set<KnowledgeAudit>();
    public DbSet<KnowledgeChunk> Chunks => Set<KnowledgeChunk>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("ai_chatbot");
        var d = model.Entity<KnowledgeDocument>();
        d.ToTable("knowledge_documents", t => t.HasCheckConstraint("ck_knowledge_revision",
            "\"LatestRevision\" > 0 AND (\"PublishedRevision\" IS NULL OR \"PublishedRevision\" BETWEEN 1 AND \"LatestRevision\")"));
        d.HasKey(x => x.Id);
        d.Property(x => x.Token).IsConcurrencyToken();
        d.HasIndex(x => x.UpdatedAt);
        var r = model.Entity<KnowledgeRevision>();
        r.ToTable("knowledge_revisions", t => {
            t.HasCheckConstraint("ck_knowledge_audience", "\"Audience\" IN ('RESIDENT','INTERNAL')");
            t.HasCheckConstraint("ck_knowledge_content", "length(btrim(\"Title\")) BETWEEN 1 AND 200 AND length(btrim(\"Content\")) BETWEEN 1 AND 60000 AND \"Number\" > 0");
        });
        r.HasKey(x => new { x.DocumentId, x.Number });
        r.Property(x => x.Title).HasMaxLength(200).IsRequired();
        r.Property(x => x.Content).IsRequired();
        r.Property(x => x.Audience).HasMaxLength(20).IsRequired();
        r.HasOne<KnowledgeDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        var a = model.Entity<KnowledgeAudit>();
        a.ToTable("knowledge_activity");
        a.HasKey(x => x.Id);
        a.Property(x => x.Action).HasMaxLength(20).IsRequired();
        a.HasIndex(x => new { x.DocumentId, x.CreatedAt });
        a.HasOne<KnowledgeRevision>().WithMany().HasForeignKey(x => new { x.DocumentId, Number = x.Revision }).OnDelete(DeleteBehavior.Restrict);
        var c = model.Entity<KnowledgeChunk>();
        c.ToTable("knowledge_chunks", t => t.HasCheckConstraint("ck_chunk_text", "length(\"Content\") BETWEEN 1 AND 1200 AND \"Number\" > 0"));
        c.HasKey(x => new { x.DocumentId, x.Revision, x.Number });
        c.Property(x => x.Content).HasMaxLength(1200).IsRequired();
        c.Property(x => x.SearchText).IsRequired();
        c.Property(x => x.Embedding).HasColumnType("real[]");
        c.Property(x => x.EmbeddingModel).HasMaxLength(150);
        c.HasOne<KnowledgeRevision>().WithMany().HasForeignKey(x => new { x.DocumentId, Number = x.Revision }).OnDelete(DeleteBehavior.Restrict);
    }
}
