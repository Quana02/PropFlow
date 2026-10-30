using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Payments.Domain.Payments;
using PropFlow.Modules.Payments.Domain.PaymentDestinations;
using PropFlow.Modules.Payments.Domain.PaymentInstructions;

namespace PropFlow.Modules.Payments.Infrastructure.Persistence;

public class PaymentsDbContext : DbContext
{
    public PaymentsDbContext(DbContextOptions<PaymentsDbContext> options)
        : base(options)
    {
    }

    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PropFlow.Modules.Payments.Infrastructure.ResidentPayments.SandboxSePayReceipt> SandboxSePayReceipts => Set<PropFlow.Modules.Payments.Infrastructure.ResidentPayments.SandboxSePayReceipt>();
    public DbSet<PropFlow.Modules.Payments.Infrastructure.ResidentPayments.SandboxPaymentSimulation> SandboxPaymentSimulations => Set<PropFlow.Modules.Payments.Infrastructure.ResidentPayments.SandboxPaymentSimulation>();
    public DbSet<PropFlow.Modules.Payments.Infrastructure.PaymentSettings.SePayOAuthConnection> SePayConnections => Set<PropFlow.Modules.Payments.Infrastructure.PaymentSettings.SePayOAuthConnection>();
    public DbSet<PropFlow.Modules.Payments.Infrastructure.PaymentSettings.SePaySandboxConnection> SePaySandboxConnections => Set<PropFlow.Modules.Payments.Infrastructure.PaymentSettings.SePaySandboxConnection>();
    public DbSet<PaymentStatusHistory> PaymentStatusHistory => Set<PaymentStatusHistory>();
    public DbSet<PaymentOperation> PaymentOperations => Set<PaymentOperation>();
    public DbSet<PaymentDestination> PaymentDestinations => Set<PaymentDestination>();
    public DbSet<PaymentInstruction> PaymentInstructions => Set<PaymentInstruction>();
    public DbSet<PropFlow.Modules.Payments.Domain.BankTransfers.BankTransfer> BankTransfers => Set<PropFlow.Modules.Payments.Domain.BankTransfers.BankTransfer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("payments");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentsDbContext).Assembly);
    }
}
