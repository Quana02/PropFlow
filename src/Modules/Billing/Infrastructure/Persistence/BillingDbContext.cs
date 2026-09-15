using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Billing.Domain.FeeRateRules;
using PropFlow.Modules.Billing.Domain.FeeTypes;
using PropFlow.Modules.Billing.Domain.InvoiceItems;
using PropFlow.Modules.Billing.Domain.Invoices;
using PropFlow.Modules.Billing.Domain.InvoiceStatusHistories;

namespace PropFlow.Modules.Billing.Infrastructure.Persistence;

public class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public DbSet<FeeType> FeeTypes => Set<FeeType>();
    public DbSet<FeeRateRule> FeeRateRules => Set<FeeRateRule>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<InvoiceStatusHistory> InvoiceStatusHistory => Set<InvoiceStatusHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("billing");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BillingDbContext).Assembly);
    }
}
