using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PropFlow.Modules.Payments.Infrastructure.PaymentSettings;

public sealed class SePaySandboxConnection
{
    public int Id { get; set; } = 1;
    public int Version { get; set; }
    public string? ProtectedApiToken { get; set; }
    public Guid UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CheckedAt { get; set; }
    public bool LastCheckSucceeded { get; set; }
    public int AccountCount { get; set; }
    public bool DemoEnabled { get; set; }
    public string? ProtectedWebhookKey { get; set; }
}

public sealed class SePaySandboxConnectionConfiguration : IEntityTypeConfiguration<SePaySandboxConnection>
{
    public void Configure(EntityTypeBuilder<SePaySandboxConnection> b)
    {
        b.ToTable("sepay_sandbox_connection", t => t.HasCheckConstraint("ck_sepay_sandbox_singleton", "\"Id\" = 1"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Version).IsConcurrencyToken();
    }
}
