using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Communication.Domain.Notifications;
namespace PropFlow.Modules.Communication.Infrastructure.Persistence.Configurations;
public sealed class FinanceEmailConfiguration : IEntityTypeConfiguration<FinanceEmail>
{
    public void Configure(EntityTypeBuilder<FinanceEmail> b)
    {
        b.ToTable("finance_emails"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.EventId).HasColumnName("event_id");
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.Address).HasColumnName("address").HasMaxLength(320);
        b.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(200);
        b.Property(x => x.Html).HasColumnName("html");
        b.Property(x => x.AttachmentFileName).HasColumnName("attachment_file_name").HasMaxLength(100);
        b.Property(x => x.Attempts).HasColumnName("attempts");
        b.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
        b.Property(x => x.SentAt).HasColumnName("sent_at");
        b.HasIndex(x => new { x.EventId, x.UserId }).IsUnique();
        b.HasIndex(x => new { x.EventId, x.Address }).IsUnique().HasFilter("user_id IS NULL");
    }
}
