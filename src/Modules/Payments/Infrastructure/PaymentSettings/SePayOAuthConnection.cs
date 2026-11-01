using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PropFlow.Modules.Payments.Infrastructure.PaymentSettings;

// One Current Building per deployment; never scope this record by the Admin user.
public sealed class SePayOAuthConnection
{
    public int Id { get; set; } = 1;
    public Guid Generation { get; set; }
    public string? ProtectedAccessToken { get; set; }
    public string? ProtectedRefreshToken { get; set; }
    public DateTimeOffset? AccessExpiresAt { get; set; }
    public bool RequiresReconnect { get; set; }
    public Guid? ConnectedBy { get; set; }
    public DateTimeOffset? ConnectedAt { get; set; }
    public string? PendingStateHash { get; set; }
    public Guid? PendingActor { get; set; }
    public DateTimeOffset? PendingExpiresAt { get; set; }
    public string? PendingRedirectUri { get; set; }
    public Guid PendingAttempt { get; set; }
    public long? WebhookId { get; set; }
    public Guid? WebhookDestinationId { get; set; }
    public DateTimeOffset? WebhookConfiguredAt { get; set; }
}

public sealed class SePayOAuthConnectionConfiguration : IEntityTypeConfiguration<SePayOAuthConnection>
{
    public void Configure(EntityTypeBuilder<SePayOAuthConnection> b)
    {
        b.ToTable("sepay_oauth_connection", t => t.HasCheckConstraint("ck_sepay_oauth_singleton", "\"Id\" = 1"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.PendingStateHash).HasMaxLength(64);
        b.Property(x => x.PendingRedirectUri).HasMaxLength(2048);
    }
}
