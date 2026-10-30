using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Payments.Domain.PaymentInstructions;

namespace PropFlow.Modules.Payments.Infrastructure.Persistence.Configurations;

public sealed class PaymentInstructionConfiguration : IEntityTypeConfiguration<PaymentInstruction>
{
    public void Configure(EntityTypeBuilder<PaymentInstruction> builder)
    {
        builder.ToTable("payment_instructions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TransferCode).HasColumnName("transfer_code").HasMaxLength(24).IsRequired();
        builder.HasIndex(x => x.TransferCode).IsUnique();
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.InvoiceId).HasColumnName("invoice_id").IsRequired();
        builder.Property(x => x.PaymentDestinationId).HasColumnName("payment_destination_id").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(x => x.InvoiceId).IsUnique();
        builder.HasIndex(x => x.PaymentDestinationId);
        builder.HasOne<PropFlow.Modules.Payments.Domain.PaymentDestinations.PaymentDestination>()
            .WithMany().HasForeignKey(x => x.PaymentDestinationId).OnDelete(DeleteBehavior.Restrict);
    }
}
