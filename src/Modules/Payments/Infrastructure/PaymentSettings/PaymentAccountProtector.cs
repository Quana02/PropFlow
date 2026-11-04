using Microsoft.AspNetCore.DataProtection;
using PropFlow.Modules.Payments.Application.PaymentSettings;

namespace PropFlow.Modules.Payments.Infrastructure.PaymentSettings;

public sealed class PaymentAccountProtector(IDataProtectionProvider provider) : IPaymentAccountProtector
{
    private readonly IDataProtector protector = provider.CreateProtector("PropFlow.Payments.PaymentDestination.AccountNumber.v1");
    public string Protect(string accountNumber) => protector.Protect(accountNumber);
    public string Unprotect(string protectedAccountNumber) => protector.Unprotect(protectedAccountNumber);
}
