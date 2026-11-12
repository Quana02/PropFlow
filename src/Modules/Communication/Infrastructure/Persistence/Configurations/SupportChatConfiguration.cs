using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Communication.Domain.SupportChat;

namespace PropFlow.Modules.Communication.Infrastructure.Persistence.Configurations;

public sealed class SupportChatConfiguration : IEntityTypeConfiguration<SupportConversation>, IEntityTypeConfiguration<SupportMessage>,
    IEntityTypeConfiguration<SupportManagerPresence>, IEntityTypeConfiguration<SupportManagerConnection>
{
    public void Configure(EntityTypeBuilder<SupportConversation> b)
    {
        b.ToTable("support_conversations", t => {
            t.HasCheckConstraint("ck_support_state", "\"Status\" IN ('WAITING','ACTIVE','CLOSED') AND ((\"Status\" = 'CLOSED') = (\"ClosedAt\" IS NOT NULL)) AND (\"Status\" <> 'ACTIVE' OR \"ManagerUserId\" IS NOT NULL)");
        });
        b.HasKey(x => x.Id); b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.ResidentName).HasMaxLength(200).IsRequired(); b.Property(x => x.ManagerName).HasMaxLength(200);
        b.HasIndex(x => x.ResidentUserId).IsUnique().HasFilter("\"Status\" IN ('WAITING','ACTIVE')");
        b.HasIndex(x => x.ManagerUserId).IsUnique().HasFilter("\"Status\" = 'ACTIVE'");
        b.HasIndex(x => new { x.Status, x.CreatedAt }); b.HasIndex(x => new { x.ManagerUserId, x.UpdatedAt });
    }
    public void Configure(EntityTypeBuilder<SupportMessage> b)
    {
        b.ToTable("support_messages", t => t.HasCheckConstraint("ck_support_message", "length(btrim(\"Text\")) BETWEEN 1 AND 4000 AND \"Sequence\" > 0 AND \"Kind\" IN ('SYSTEM','RESIDENT','MANAGER') AND ((\"Kind\" = 'SYSTEM') = (\"SenderUserId\" IS NULL))"));
        b.HasKey(x => x.Id); b.Property(x => x.SenderName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Kind).HasMaxLength(20).IsRequired(); b.Property(x => x.Text).HasMaxLength(4000).IsRequired();
        b.HasIndex(x => new { x.ConversationId, x.Sequence }).IsUnique();
        b.HasIndex(x => new { x.SenderUserId, x.ClientMessageId }).IsUnique().HasFilter("\"SenderUserId\" IS NOT NULL");
        b.HasOne<SupportConversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<SupportManagerPresence> b) { b.ToTable("support_manager_presence"); b.HasKey(x => x.UserId); }
    public void Configure(EntityTypeBuilder<SupportManagerConnection> b)
    { b.ToTable("support_manager_connections"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasMaxLength(128); b.HasIndex(x => new { x.ManagerUserId, x.ExpiresAt }); }
}
