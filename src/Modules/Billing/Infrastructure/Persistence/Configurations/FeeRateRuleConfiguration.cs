using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Billing.Domain.FeeRateRules;

namespace PropFlow.Modules.Billing.Infrastructure.Persistence.Configurations;

public class FeeRateRuleConfiguration : IEntityTypeConfiguration<FeeRateRule>
{
    public void Configure(EntityTypeBuilder<FeeRateRule> builder)
    {
        builder.ToTable("fee_rate_rules", "billing", table =>
        {
            table.HasCheckConstraint(
                "CK_fee_rate_rules_effective_dates",
                "effective_to IS NULL OR effective_to >= effective_from");
            table.HasCheckConstraint(
                "CK_fee_rate_rules_amount_range",
                "minimum_amount IS NULL OR maximum_amount IS NULL OR minimum_amount <= maximum_amount");
            table.HasCheckConstraint("CK_fee_rate_rules_unit_rate_non_negative", "unit_rate >= 0");
            table.HasCheckConstraint("CK_fee_rate_rules_minimum_amount_non_negative", "minimum_amount IS NULL OR minimum_amount >= 0");
            table.HasCheckConstraint("CK_fee_rate_rules_maximum_amount_non_negative", "maximum_amount IS NULL OR maximum_amount >= 0");
        });

        builder.HasKey(rule => rule.Id);
        builder.Property(rule => rule.Id).HasColumnName("id");

        builder.Property(rule => rule.FeeTypeId).HasColumnName("fee_type_id").IsRequired();
        builder.HasIndex(rule => rule.FeeTypeId);

        builder.Property(rule => rule.BuildingId).HasColumnName("building_id");
        builder.HasIndex(rule => rule.BuildingId);

        builder.Property(rule => rule.RuleName)
            .HasColumnName("rule_name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(rule => rule.CalculationMethodCode)
            .HasColumnName("calculation_method_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(rule => rule.BillingFrequencyCode)
            .HasColumnName("billing_frequency_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(rule => rule.UnitRate)
            .HasColumnName("unit_rate")
            .HasColumnType("numeric(18,0)")
            .IsRequired();

        builder.Property(rule => rule.UnitName)
            .HasColumnName("unit_name")
            .HasMaxLength(50);

        builder.Property(rule => rule.MinimumAmount)
            .HasColumnName("minimum_amount")
            .HasColumnType("numeric(18,0)");

        builder.Property(rule => rule.MaximumAmount)
            .HasColumnName("maximum_amount")
            .HasColumnType("numeric(18,0)");

        builder.Property(rule => rule.RuleConfig)
            .HasColumnName("rule_config")
            .HasColumnType("jsonb");

        builder.Property(rule => rule.EffectiveFrom)
            .HasColumnName("effective_from")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(rule => rule.EffectiveTo)
            .HasColumnName("effective_to")
            .HasColumnType("date");

        builder.Property(rule => rule.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();
        builder.HasIndex(rule => rule.IsActive);

        builder.HasIndex(rule => new { rule.FeeTypeId, rule.BuildingId, rule.EffectiveFrom });

        builder.Property(rule => rule.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.Property(rule => rule.UpdatedBy).HasColumnName("updated_by");

        builder.Property(rule => rule.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(rule => rule.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();
    }
}
