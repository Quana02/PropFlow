using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.Configurations;

public sealed class FacilityGuideConfiguration : IEntityTypeConfiguration<FacilityGuide>
{
    public void Configure(EntityTypeBuilder<FacilityGuide> b)
    {
        b.ToTable("facility_guides", t =>
        {
            t.HasCheckConstraint("ck_facility_guide_age", "(minimum_age IS NULL OR minimum_age BETWEEN 0 AND 120) AND (maximum_age IS NULL OR maximum_age BETWEEN 0 AND 120) AND (minimum_age IS NULL OR maximum_age IS NULL OR minimum_age <= maximum_age)");
            t.HasCheckConstraint("ck_facility_guide_audience", "audience IN ('GENERAL','CHILDREN','FAMILY','ADULTS')");
            t.HasCheckConstraint("ck_facility_guide_child_policy", "child_policy IN ('GUARDIAN_REQUIRED','SUPERVISED_CONTACT_REQUIRED','NOT_FOR_CHILDREN')");
        });
        b.HasKey(x => x.FacilityId); b.HasOne<Facility>().WithOne().HasForeignKey<FacilityGuide>(x => x.FacilityId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Description).HasMaxLength(2000); b.Property(x => x.Audience).HasMaxLength(30);
        b.Property(x => x.ChildPolicy).HasMaxLength(40); b.Property(x => x.SupervisionNote).HasMaxLength(1000); b.Property(x => x.Rules).HasMaxLength(4000);
        b.Property(x => x.HoursJson).HasColumnType("jsonb"); b.Property(x => x.ContactDetails).HasMaxLength(300); b.Property(x => x.Token).IsConcurrencyToken();
        foreach (var property in b.Metadata.GetProperties()) property.SetColumnName(System.Text.RegularExpressions.Regex.Replace(property.Name,"([a-z0-9])([A-Z])","$1_$2").ToLowerInvariant());
    }
}
