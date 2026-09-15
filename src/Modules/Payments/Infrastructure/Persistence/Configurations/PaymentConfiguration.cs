using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Payments.Domain.Payments;

namespace PropFlow.Modules.Payments.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", table =>
        {
            table.HasCheckConstraint("CK_payments_amount_positive", "amount > 0");
            table.HasCheckConstraint(
                "CK_payments_confirmed_fields",
                "(status <> 'CONFIRMED') OR (confirmed_by IS NOT NULL AND confirmed_at IS NOT NULL AND rejected_by IS NULL AND rejected_at IS NULL AND rejection_reason IS NULL)");
            table.HasCheckConstraint(
                "CK_payments_rejected_fields",
                "(status <> 'REJECTED') OR (rejected_by IS NOT NULL AND rejected_at IS NOT NULL AND rejection_reason IS NOT NULL AND confirmed_by IS NULL AND confirmed_at IS NULL)");
            table.HasCheckConstraint(
                "CK_payments_pending_fields",
                "(status <> 'PENDING') OR (confirmed_by IS NULL AND confirmed_at IS NULL AND rejected_by IS NULL AND rejected_at IS NULL AND rejection_reason IS NULL)");
        });

        builder.HasKey(payment => payment.Id);

        builder.Property(payment => payment.Id)
            .HasColumnName("id");

        builder.Property(payment => payment.PaymentNumber)
            .HasColumnName("payment_number")
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(payment => payment.InvoiceId)
            .HasColumnName("invoice_id")
            .IsRequired();

        builder.Property(payment => payment.Amount)
            .HasColumnName("amount")
            .HasColumnType("numeric(18,0)")
            .IsRequired();

        builder.Property(payment => payment.PaymentMethodCode)
            .HasColumnName("payment_method_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(payment => payment.PaymentDate)
            .HasColumnName("payment_date")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(payment => payment.ReferenceNumber)
            .HasColumnName("reference_number")
            .HasMaxLength(100);

        builder.Property(payment => payment.Note)
            .HasColumnName("note")
            .HasColumnType("text");

        builder.Property(payment => payment.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(PaymentStatus.PENDING)
            .IsRequired();

        builder.Property(payment => payment.SubmittedBy)
            .HasColumnName("submitted_by");

        builder.Property(payment => payment.ConfirmedBy)
            .HasColumnName("confirmed_by");

        builder.Property(payment => payment.ConfirmedAt)
            .HasColumnName("confirmed_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(payment => payment.RejectedBy)
            .HasColumnName("rejected_by");

        builder.Property(payment => payment.RejectedAt)
            .HasColumnName("rejected_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(payment => payment.RejectionReason)
            .HasColumnName("rejection_reason")
            .HasColumnType("text");

        builder.Property(payment => payment.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(payment => payment.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(payment => payment.PaymentNumber)
            .IsUnique();

        builder.HasIndex(payment => payment.InvoiceId);

        builder.HasIndex(payment => payment.Status);

        builder.HasIndex(payment => payment.PaymentDate);

        builder.HasIndex(payment => payment.ReferenceNumber)
            .IsUnique()
            .HasFilter("reference_number IS NOT NULL");

        builder.HasMany(payment => payment.StatusHistory)
            .WithOne(history => history.Payment)
            .HasForeignKey(history => history.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Metadata.FindNavigation(nameof(Payment.StatusHistory))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
