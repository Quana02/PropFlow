using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Billing.Domain.ElectricityUsages;

namespace PropFlow.Modules.Billing.Infrastructure.Persistence.Configurations;
public sealed class ElectricityUsageConfiguration : IEntityTypeConfiguration<ElectricityUsage>
{
    public void Configure(EntityTypeBuilder<ElectricityUsage> b)
    {
        b.ToTable("electricity_usages", t => { t.HasCheckConstraint("ck_electricity_usage_kwh", "kwh >= 0 AND kwh <= 1000000"); t.HasCheckConstraint("ck_utility_usage_water", "water_m3 >= 0 AND water_m3 <= 1000000"); });
        b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.ApartmentId).HasColumnName("apartment_id");
        b.Property(x => x.UsageMonth).HasColumnName("usage_month");
        b.Property(x => x.WaterM3).HasColumnName("water_m3").HasPrecision(18, 3);
        b.Property(x => x.Kwh).HasColumnName("kwh").HasPrecision(18, 3);
        b.Property(x => x.CreatedBy).HasColumnName("created_by"); b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.UpdatedBy).HasColumnName("updated_by"); b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.HasIndex(x => new { x.ApartmentId, x.UsageMonth }).IsUnique();
    }
}
