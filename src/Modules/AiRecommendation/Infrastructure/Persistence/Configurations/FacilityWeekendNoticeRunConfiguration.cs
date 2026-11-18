using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.AiRecommendation.Domain.Recommendations;

namespace PropFlow.Modules.AiRecommendation.Infrastructure.Persistence.Configurations;

public sealed class FacilityWeekendNoticeRunConfiguration : IEntityTypeConfiguration<FacilityWeekendNoticeRun>
{
    public void Configure(EntityTypeBuilder<FacilityWeekendNoticeRun> builder)
    {
        builder.ToTable("facility_weekend_notice_runs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.LocalDate).HasColumnName("local_date");
        builder.Property(x => x.Slot).HasColumnName("slot");
        builder.Property(x => x.WeekendStart).HasColumnName("weekend_start");
        builder.Property(x => x.FacilityId).HasColumnName("facility_id");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.HasIndex(x => new { x.LocalDate, x.Slot }).IsUnique();
        builder.HasIndex(x => new { x.WeekendStart, x.FacilityId }).IsUnique();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(x => x.LeaseUntil).HasColumnName("lease_until");
        builder.Property(x => x.LeaseToken).HasColumnName("lease_token").IsConcurrencyToken();
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        builder.Property(x => x.Attempts).HasColumnName("attempts");
        builder.Property(x => x.Recipients).HasColumnName("recipients");
    }
}
