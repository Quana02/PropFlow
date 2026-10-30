using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Payments.Domain.PaymentDestinations;

namespace PropFlow.Modules.Payments.Infrastructure.Persistence.Configurations;

public sealed class PaymentDestinationConfiguration : IEntityTypeConfiguration<PaymentDestination>
{
    public void Configure(EntityTypeBuilder<PaymentDestination> builder)
    {
        builder.ToTable("payment_destinations", "payments");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.Version).HasColumnName("version").IsRequired();
        builder.Property(item => item.BankBin).HasColumnName("bank_bin").HasMaxLength(6).IsRequired();
        builder.Property(item => item.BankCode).HasColumnName("bank_code").HasMaxLength(20).IsRequired();
        builder.Property(item => item.BankName).HasColumnName("bank_name").HasMaxLength(150).IsRequired();
        builder.Property(item => item.ProtectedAccountNumber).HasColumnName("protected_account_number").HasMaxLength(1000).IsRequired();
        builder.Property(item => item.AccountNumberLast4).HasColumnName("account_number_last4").HasMaxLength(4).IsRequired();
        builder.Property(item => item.AccountName).HasColumnName("account_name").HasMaxLength(50).IsRequired();
        builder.Property(item => item.SePayAccountId).HasColumnName("sepay_account_id");
        builder.HasIndex(item => item.SePayAccountId);
        builder.Property(item => item.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(item => item.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.Property(item => item.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(item => item.UpdatedBy).HasColumnName("updated_by").IsRequired();
        builder.Property(item => item.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(item => item.DeactivatedAt).HasColumnName("deactivated_at");
        builder.HasIndex(item => item.Version).IsUnique();
        builder.HasIndex(item => item.IsActive).IsUnique().HasFilter("is_active = true");
    }
}
