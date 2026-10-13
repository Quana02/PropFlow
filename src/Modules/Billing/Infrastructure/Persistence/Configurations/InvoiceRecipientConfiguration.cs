using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Billing.Domain.Invoices;
namespace PropFlow.Modules.Billing.Infrastructure.Persistence.Configurations;
public sealed class InvoiceRecipientConfiguration : IEntityTypeConfiguration<InvoiceRecipient>
{
    public void Configure(EntityTypeBuilder<InvoiceRecipient> b)
    {
        b.ToTable("invoice_recipients"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.ResidentId).HasColumnName("resident_id");
        b.Property(x => x.Capacity).HasColumnName("capacity").HasMaxLength(30).HasDefaultValue("HOUSEHOLD_HEAD");
        b.HasIndex(x => new { x.InvoiceId, x.UserId }).IsUnique().HasFilter("user_id IS NOT NULL");
        b.HasIndex(x => new { x.InvoiceId, x.ResidentId }).IsUnique().HasFilter("resident_id IS NOT NULL");
        b.Property(x => x.InvoiceId).HasColumnName("invoice_id");
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.FullName).HasColumnName("full_name").HasMaxLength(200);
        b.Property(x => x.Email).HasColumnName("email").HasMaxLength(320);
        b.Property(x => x.UnitNumber).HasColumnName("unit_number").HasMaxLength(50);
        b.Property(x => x.AreaM2).HasColumnName("area_m2").HasPrecision(18, 2);
        b.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.UserId);
    }
}
