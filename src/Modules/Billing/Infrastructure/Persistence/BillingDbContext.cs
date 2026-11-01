using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Billing.Domain.FeeRateRules;
using PropFlow.Modules.Billing.Domain.FeeTypes;
using PropFlow.Modules.Billing.Domain.InvoiceItems;
using PropFlow.Modules.Billing.Domain.Invoices;
using PropFlow.Modules.Billing.Domain.InvoiceStatusHistories;

namespace PropFlow.Modules.Billing.Infrastructure.Persistence;

public class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public DbSet<PropFlow.Modules.Billing.Domain.PricingChanges.PricingChange> PricingChanges => Set<PropFlow.Modules.Billing.Domain.PricingChanges.PricingChange>();
    public DbSet<PropFlow.Modules.Billing.Domain.ElectricityUsages.ElectricityUsage> ElectricityUsages => Set<PropFlow.Modules.Billing.Domain.ElectricityUsages.ElectricityUsage>();
    public DbSet<FeeType> FeeTypes => Set<FeeType>();
    public DbSet<FeeRateRule> FeeRateRules => Set<FeeRateRule>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceRecipient> InvoiceRecipients => Set<InvoiceRecipient>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<InvoiceStatusHistory> InvoiceStatusHistory => Set<InvoiceStatusHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("billing");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BillingDbContext).Assembly);
    }
}
