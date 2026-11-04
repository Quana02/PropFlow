namespace PropFlow.Modules.Payments.Application.PaymentSettings;

public sealed record SePayBankAccount(Guid Id, string BankBin, string BankCode, string BankName,
    string AccountNumber, string AccountName, bool Active);
public sealed record SePayBankAccountView(Guid Id, string BankCode, string BankName, string MaskedAccountNumber,
    string AccountName, bool Active);
public sealed record SelectSePayAccountRequest(Guid AccountId, int ExpectedVersion = 0);
public sealed record ConfirmSePayAccountRequest(Guid AccountId, int ExpectedVersion, Guid ChallengeId, string Code);

public interface ISePayBankAccounts
{
    Task<IReadOnlyList<SePayBankAccount>> ListAsync(CancellationToken ct);
    Task<SePayBankAccount> GetAsync(Guid accountId, CancellationToken ct);
}
