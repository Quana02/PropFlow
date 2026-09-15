using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Administration.Domain.SystemConfigurations;

namespace PropFlow.Modules.Administration.Infrastructure.Persistence.Configurations;

public class SystemConfigurationConfiguration : IEntityTypeConfiguration<SystemConfiguration>
{
    public void Configure(EntityTypeBuilder<SystemConfiguration> builder)
    {
        builder.ToTable("system_configurations", "administration");

        builder.HasKey(sc => sc.Id);

        builder.Property(sc => sc.Id)
            .HasColumnName("id");

        builder.Property(sc => sc.ConfigKey)
            .HasColumnName("config_key")
            .HasMaxLength(150)
            .IsRequired();

        builder.HasIndex(sc => sc.ConfigKey)
            .IsUnique();

        builder.Property(sc => sc.ConfigValue)
            .HasColumnName("config_value")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(sc => sc.ValueType)
            .HasColumnName("value_type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(ConfigValueType.STRING)
            .IsRequired();

        builder.Property(sc => sc.Description)
            .HasColumnName("description")
            .HasColumnType("text");

        builder.Property(sc => sc.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(sc => sc.UpdatedBy)
            .HasColumnName("updated_by");

        builder.Property(sc => sc.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(sc => sc.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();
    }
}
