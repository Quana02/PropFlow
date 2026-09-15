using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Billing.Domain.InvoiceItems;

namespace PropFlow.Modules.Billing.Infrastructure.Persistence.Configurations;

public class InvoiceItemConfiguration : IEntityTypeConfiguration<InvoiceItem>
{
    public void Configure(EntityTypeBuilder<InvoiceItem> builder)
    {
        builder.ToTable("invoice_items", "billing", table =>
        {
            table.HasCheckConstraint("CK_invoice_items_quantity_positive", "quantity > 0");
            table.HasCheckConstraint("CK_invoice_items_unit_rate_non_negative", "unit_rate >= 0");
            table.HasCheckConstraint("CK_invoice_items_line_amount_non_negative", "line_amount >= 0");
        });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");

        builder.Property(item => item.InvoiceId).HasColumnName("invoice_id").IsRequired();
        builder.HasIndex(item => item.InvoiceId);

        builder.Property(item => item.FeeTypeId).HasColumnName("fee_type_id");
        builder.HasIndex(item => item.FeeTypeId);

        builder.Property(item => item.FeeRateRuleId).HasColumnName("fee_rate_rule_id");
        builder.HasIndex(item => item.FeeRateRuleId);

        builder.Property(item => item.FeeCodeSnapshot)
            .HasColumnName("fee_code_snapshot")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(item => item.FeeNameSnapshot)
            .HasColumnName("fee_name_snapshot")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(item => item.CalculationMethodCodeSnapshot)
            .HasColumnName("calculation_method_code_snapshot")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(item => item.Description)
            .HasColumnName("description")
            .HasColumnType("text");

        builder.Property(item => item.Quantity)
            .HasColumnName("quantity")
            .HasColumnType("numeric(18,4)")
            .HasDefaultValue(1m)
            .IsRequired();

        builder.Property(item => item.UnitName)
            .HasColumnName("unit_name")
            .HasMaxLength(50);

        builder.Property(item => item.UnitRate)
            .HasColumnName("unit_rate")
            .HasColumnType("numeric(18,0)")
            .IsRequired();

        builder.Property(item => item.LineAmount)
            .HasColumnName("line_amount")
            .HasColumnType("numeric(18,0)")
            .IsRequired();

        builder.Property(item => item.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasOne(item => item.FeeType)
            .WithMany()
            .HasForeignKey(item => item.FeeTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(item => item.FeeRateRule)
            .WithMany()
            .HasForeignKey(item => item.FeeRateRuleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
