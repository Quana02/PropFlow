using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Billing.Domain.Invoices;

namespace PropFlow.Modules.Billing.Infrastructure.Persistence.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices", "billing", table =>
        {
            table.HasCheckConstraint("CK_invoices_billing_period", "billing_period_end >= billing_period_start");
            table.HasCheckConstraint("CK_invoices_due_date", "issue_date IS NULL OR due_date IS NULL OR due_date >= issue_date");
            table.HasCheckConstraint("CK_invoices_subtotal_non_negative", "subtotal >= 0");
            table.HasCheckConstraint("CK_invoices_total_amount_non_negative", "total_amount >= 0");
            table.HasCheckConstraint("CK_invoices_total_matches_subtotal", "total_amount = subtotal");
            table.HasCheckConstraint(
                "CK_invoices_issued_fields",
                "(status <> 'ISSUED') OR (issue_date IS NOT NULL AND issued_at IS NOT NULL AND issued_by IS NOT NULL AND cancelled_at IS NULL AND cancelled_by IS NULL)");
            table.HasCheckConstraint(
                "CK_invoices_cancelled_fields",
                "(status <> 'CANCELLED') OR (cancelled_at IS NOT NULL AND cancelled_by IS NOT NULL AND cancellation_reason IS NOT NULL)");
        });

        builder.HasKey(invoice => invoice.Id);
        builder.Property(invoice => invoice.Id).HasColumnName("id");

        builder.Property(invoice => invoice.InvoiceNumber)
            .HasColumnName("invoice_number")
            .HasMaxLength(40)
            .IsRequired();
        builder.HasIndex(invoice => invoice.InvoiceNumber).IsUnique();

        builder.Property(invoice => invoice.ApartmentUnitId).HasColumnName("apartment_unit_id").IsRequired();
        builder.HasIndex(invoice => invoice.ApartmentUnitId);

        builder.Property(invoice => invoice.BillingPeriodStart)
            .HasColumnName("billing_period_start")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(invoice => invoice.BillingPeriodEnd)
            .HasColumnName("billing_period_end")
            .HasColumnType("date")
            .IsRequired();

        builder.HasIndex(invoice => new { invoice.ApartmentUnitId, invoice.BillingPeriodStart, invoice.BillingPeriodEnd }).IsUnique();

        builder.Property(invoice => invoice.IssueDate)
            .HasColumnName("issue_date")
            .HasColumnType("date");

        builder.Property(invoice => invoice.DueDate)
            .HasColumnName("due_date")
            .HasColumnType("date");
        builder.HasIndex(invoice => invoice.DueDate);

        builder.Property(invoice => invoice.Subtotal)
            .HasColumnName("subtotal")
            .HasColumnType("numeric(18,0)")
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(invoice => invoice.TotalAmount)
            .HasColumnName("total_amount")
            .HasColumnType("numeric(18,0)")
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(invoice => invoice.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(InvoiceStatus.DRAFT)
            .IsRequired();
        builder.HasIndex(invoice => invoice.Status);

        builder.Property(invoice => invoice.Note)
            .HasColumnName("note")
            .HasColumnType("text");

        builder.Property(invoice => invoice.IssuedAt)
            .HasColumnName("issued_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(invoice => invoice.IssuedBy).HasColumnName("issued_by");

        builder.Property(invoice => invoice.CancelledAt)
            .HasColumnName("cancelled_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(invoice => invoice.CancelledBy).HasColumnName("cancelled_by");

        builder.Property(invoice => invoice.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasColumnType("text");

        builder.Property(invoice => invoice.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.Property(invoice => invoice.UpdatedBy).HasColumnName("updated_by");

        builder.Property(invoice => invoice.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(invoice => invoice.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasMany(invoice => invoice.Items)
            .WithOne(item => item.Invoice)
            .HasForeignKey(item => item.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(invoice => invoice.StatusHistory)
            .WithOne(history => history.Invoice)
            .HasForeignKey(history => history.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(invoice => invoice.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(invoice => invoice.StatusHistory).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
