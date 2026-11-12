using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Billing.Domain.ElectricityUsages;

namespace PropFlow.Modules.Billing.Infrastructure.Persistence.Configurations;
public sealed class ElectricityUsageConfiguration : IEntityTypeConfiguration<ElectricityUsage>
{
    public void Configure(EntityTypeBuilder<ElectricityUsage> b)
    {
        b.ToTable("electricity_usages", t =>
        {
            t.HasCheckConstraint("ck_electricity_usage_kwh", "kwh >= 0 AND kwh <= 1000000");
            t.HasCheckConstraint("ck_utility_usage_water", "water_m3 >= 0 AND water_m3 <= 1000000");
            t.HasCheckConstraint("ck_utility_electricity_readings",
                "(previous_electricity_reading IS NULL AND current_electricity_reading IS NULL) OR " +
                "(previous_electricity_reading IS NOT NULL AND current_electricity_reading IS NOT NULL AND kwh IS NOT NULL AND " +
                "previous_electricity_reading >= 0 AND current_electricity_reading <= 1000000 AND kwh = current_electricity_reading - previous_electricity_reading)");
            t.HasCheckConstraint("ck_utility_water_readings",
                "(previous_water_reading IS NULL AND current_water_reading IS NULL) OR " +
                "(previous_water_reading IS NOT NULL AND current_water_reading IS NOT NULL AND water_m3 IS NOT NULL AND " +
                "previous_water_reading >= 0 AND current_water_reading <= 1000000 AND water_m3 = current_water_reading - previous_water_reading)");
        });
        b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.ApartmentId).HasColumnName("apartment_id");
        b.Property(x => x.UsageMonth).HasColumnName("usage_month");
        b.Property(x => x.WaterM3).HasColumnName("water_m3").HasPrecision(18, 3);
        b.Property(x => x.Kwh).HasColumnName("kwh").HasPrecision(18, 3);
        b.Property(x => x.PreviousElectricityReading).HasColumnName("previous_electricity_reading").HasPrecision(18, 3);
        b.Property(x => x.CurrentElectricityReading).HasColumnName("current_electricity_reading").HasPrecision(18, 3);
        b.Property(x => x.PreviousWaterReading).HasColumnName("previous_water_reading").HasPrecision(18, 3);
        b.Property(x => x.CurrentWaterReading).HasColumnName("current_water_reading").HasPrecision(18, 3);
        b.Property(x => x.CreatedBy).HasColumnName("created_by"); b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.UpdatedBy).HasColumnName("updated_by"); b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.HasIndex(x => new { x.ApartmentId, x.UsageMonth }).IsUnique();
    }
}
