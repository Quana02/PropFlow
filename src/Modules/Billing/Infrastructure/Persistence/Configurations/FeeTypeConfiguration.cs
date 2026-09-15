using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Billing.Domain.FeeTypes;

namespace PropFlow.Modules.Billing.Infrastructure.Persistence.Configurations;

public class FeeTypeConfiguration : IEntityTypeConfiguration<FeeType>
{
    public void Configure(EntityTypeBuilder<FeeType> builder)
    {
        builder.ToTable("fee_types", "billing");

        builder.HasKey(feeType => feeType.Id);
        builder.Property(feeType => feeType.Id).HasColumnName("id");

        builder.Property(feeType => feeType.Code)
            .HasColumnName("code")
            .HasMaxLength(50)
            .IsRequired();
        builder.HasIndex(feeType => feeType.Code).IsUnique();

        builder.Property(feeType => feeType.Name)
            .HasColumnName("name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(feeType => feeType.Description)
            .HasColumnName("description")
            .HasColumnType("text");

        builder.Property(feeType => feeType.DefaultCalculationMethodCode)
            .HasColumnName("default_calculation_method_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(feeType => feeType.DefaultBillingFrequencyCode)
            .HasColumnName("default_billing_frequency_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(feeType => feeType.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(MasterDataStatus.ACTIVE)
            .IsRequired();

        builder.Property(feeType => feeType.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.Property(feeType => feeType.UpdatedBy).HasColumnName("updated_by");

        builder.Property(feeType => feeType.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(feeType => feeType.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasMany(feeType => feeType.RateRules)
            .WithOne(rule => rule.FeeType)
            .HasForeignKey(rule => rule.FeeTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(feeType => feeType.RateRules).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
