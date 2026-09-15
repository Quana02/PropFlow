using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;

namespace PropFlow.Modules.Administration.Infrastructure.Persistence.Configurations;

public class UserRoleAssignmentConfiguration : IEntityTypeConfiguration<UserRoleAssignment>
{
    public void Configure(EntityTypeBuilder<UserRoleAssignment> builder)
    {
        builder.ToTable("user_role_assignments", "administration");

        builder.HasKey(ura => ura.Id);

        builder.Property(ura => ura.Id)
            .HasColumnName("id");

        builder.Property(ura => ura.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        // UNIQUE(user_id) ensures 1 primary role per user per DBML
        builder.HasIndex(ura => ura.UserId)
            .IsUnique();

        builder.Property(ura => ura.RoleId)
            .HasColumnName("role_id")
            .IsRequired();

        builder.HasIndex(ura => ura.RoleId);

        builder.Property(ura => ura.AssignedBy)
            .HasColumnName("assigned_by");

        builder.Property(ura => ura.AssignedAt)
            .HasColumnName("assigned_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(ura => ura.UpdatedBy)
            .HasColumnName("updated_by");

        builder.Property(ura => ura.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasOne(ura => ura.Role)
            .WithMany(r => r.UserRoleAssignments)
            .HasForeignKey(ura => ura.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
