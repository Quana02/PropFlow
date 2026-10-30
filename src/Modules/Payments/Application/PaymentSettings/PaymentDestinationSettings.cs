using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Payments.Domain.PaymentDestinations;

namespace PropFlow.Modules.Payments.Application.PaymentSettings;

public sealed record PaymentDestinationView(
    Guid Id,
    int Version,
    string BankBin,
    string BankCode,
    string BankName,
    string MaskedAccountNumber,
    string AccountName,
    bool IsActive,
    Guid UpdatedBy,
    DateTimeOffset UpdatedAt,
    Guid? SePayAccountId = null,
    Guid? CreatedBy = null,
    DateTimeOffset? CreatedAt = null);

public sealed record PaymentDestinationDraft(
    string BankBin,
    string BankCode,
    string BankName,
    string AccountNumber,
    string AccountName,
    int ExpectedVersion = 0,
    Guid? SePayAccountId = null);

public sealed record PaymentDestinationChallengeRequest(string Action, PaymentDestinationDraft? Destination);
public sealed record ConfirmPaymentDestinationRequest(
    PaymentDestinationDraft Destination,
    Guid ChallengeId,
    string Code);
public sealed record DeactivatePaymentDestinationRequest(int ExpectedVersion, Guid ChallengeId, string Code);
public sealed record BankOption(string Bin, string Code, string ShortName, string Name, string? Logo);
public sealed record PaymentDestinationPreview(string QrImageUrl, string AccountName, string AccountNumber, string BankName);
public sealed record PaymentDestinationState(PaymentDestinationView? Destination);

public interface IPaymentDestinationStore
{
    Task<PaymentDestination?> ActiveAsync(CancellationToken ct);
    Task<IReadOnlyList<PaymentDestination>> HistoryAsync(CancellationToken ct);
    Task<PaymentDestination> AddReplacementAsync(
        PaymentDestinationWrite write,
        int expectedVersion,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken ct);
    Task DeactivateAsync(int expectedVersion, Guid actorUserId, DateTimeOffset now, CancellationToken ct);
}

public sealed record PaymentDestinationWrite(
    string BankBin,
    string BankCode,
    string BankName,
    string ProtectedAccountNumber,
    string AccountNumberLast4,
    string AccountName,
    Guid? SePayAccountId = null);

public interface IPaymentAccountProtector
{
    string Protect(string accountNumber);
    string Unprotect(string protectedAccountNumber) => throw new NotSupportedException("Account number decryption is not available.");
}

public interface IPaymentBankDirectory
{
    Task<IReadOnlyList<BankOption>> ListAsync(CancellationToken ct);
}

public sealed class PaymentSettingsFailure(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public sealed class PaymentDestinationSettings(
    IPaymentDestinationStore store,
    IPaymentAccountProtector protector,
    IPaymentBankDirectory banks,
    IPrivilegedActionOtp otp,
    TimeProvider clock)
{
    public const string OtpPurpose = "PAYMENT_DESTINATION_CHANGE";

    public async Task<PaymentDestinationState> CurrentAsync(CancellationToken ct) =>
        new((await store.ActiveAsync(ct)) is { } destination ? View(destination) : null);

    public async Task<IReadOnlyList<PaymentDestinationView>> HistoryAsync(CancellationToken ct) =>
        (await store.HistoryAsync(ct)).Select(View).ToArray();

    public Task<IReadOnlyList<BankOption>> BanksAsync(CancellationToken ct) => banks.ListAsync(ct);

    public async Task<PaymentDestinationPreview> PreviewAsync(PaymentDestinationDraft request, CancellationToken ct)
    {
        var draft = await NormalizeAndValidateAsync(request, ct);
        var url = $"https://img.vietqr.io/image/{Uri.EscapeDataString(draft.BankBin)}-{Uri.EscapeDataString(draft.AccountNumber)}-compact2.png?accountName={Uri.EscapeDataString(draft.AccountName)}";
        return new(url, draft.AccountName, draft.AccountNumber, draft.BankName);
    }

    public async Task<PrivilegedActionChallengeResult> RequestChallengeAsync(
        Guid actorUserId,
        PaymentDestinationChallengeRequest request,
        CancellationToken ct)
    {
        var action = NormalizeAction(request.Action);
        if (action == "UPSERT") await EnsureExpectedVersionAsync(request.Destination?.ExpectedVersion ?? -1, ct);
        var payloadHash = action == "UPSERT"
            ? Hash(action, await NormalizeAndValidateAsync(request.Destination ?? throw Invalid(), ct))
            : await DeactivateHashAsync(action, request.Destination?.ExpectedVersion ?? 0, ct);
        return await otp.RequestAsync(actorUserId, OtpPurpose, payloadHash, ct);
    }

    public async Task<PaymentDestinationView> SaveAsync(Guid actorUserId, ConfirmPaymentDestinationRequest request, CancellationToken ct)
    {
        var draft = await NormalizeAndValidateAsync(request.Destination, ct);
        await EnsureExpectedVersionAsync(draft.ExpectedVersion, ct);
        var payloadHash = Hash("UPSERT", draft);
        await otp.VerifyAsync(actorUserId, request.ChallengeId, OtpPurpose, payloadHash, request.Code, ct);
        var now = clock.GetUtcNow();
        var destination = await store.AddReplacementAsync(
            new PaymentDestinationWrite(
                draft.BankBin,
                draft.BankCode,
                draft.BankName,
                protector.Protect(draft.AccountNumber),
                draft.AccountNumber[^4..],
                draft.AccountName,
                draft.SePayAccountId),
            draft.ExpectedVersion,
            actorUserId,
            now,
            ct);
        return View(destination);
    }

    public async Task DeactivateAsync(Guid actorUserId, DeactivatePaymentDestinationRequest request, CancellationToken ct)
    {
        var payloadHash = await DeactivateHashAsync("DEACTIVATE", request.ExpectedVersion, ct);
        await otp.VerifyAsync(actorUserId, request.ChallengeId, OtpPurpose, payloadHash, request.Code, ct);
        await store.DeactivateAsync(request.ExpectedVersion, actorUserId, clock.GetUtcNow(), ct);
    }

    private async Task<PaymentDestinationDraft> NormalizeAndValidateAsync(PaymentDestinationDraft request, CancellationToken ct)
    {
        if (request.ExpectedVersion < 0 || request.SePayAccountId == Guid.Empty) throw Invalid();
        var bankBin = Digits(request.BankBin);
        var accountNumber = Digits(request.AccountNumber);
        var accountName = RemoveDiacritics(request.AccountName).ToUpperInvariant();
        accountName = Regex.Replace(accountName, @"\s+", " ").Trim();
        if (bankBin.Length != 6 || accountNumber.Length is < 6 or > 19 ||
            accountName.Length is < 5 or > 50 || !Regex.IsMatch(accountName, "^[A-Z0-9 ]+$"))
            throw Invalid();

        var bank = (await banks.ListAsync(ct)).SingleOrDefault(item => item.Bin == bankBin);
        if (bank is null) throw new PaymentSettingsFailure(400, "invalid_bank", "Ngân hàng được chọn không hợp lệ hoặc chưa được VietQR hỗ trợ.");
        return new(bank.Bin, bank.Code, bank.Name, accountNumber, accountName, request.ExpectedVersion, request.SePayAccountId);
    }

    private async Task<string> DeactivateHashAsync(string action, int expectedVersion, CancellationToken ct)
    {
        var active = await store.ActiveAsync(ct) ?? throw new PaymentSettingsFailure(409, "payment_destination_missing", "Chưa có cấu hình nhận tiền đang hoạt động.");
        if (expectedVersion != active.Version) throw Conflict();
        return Sha256($"{action}|{active.Id:N}|{active.Version}");
    }

    private async Task EnsureExpectedVersionAsync(int expectedVersion, CancellationToken ct)
    {
        var active = await store.ActiveAsync(ct);
        if (expectedVersion < 0 || (active?.Version ?? 0) != expectedVersion) throw Conflict();
    }

    private static string Hash(string action, PaymentDestinationDraft draft) =>
        Sha256(System.Text.Json.JsonSerializer.Serialize(new { Action = action, Destination = draft }));
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
    private static string RemoveDiacritics(string? value)
    {
        var normalized = (value ?? string.Empty).Normalize(NormalizationForm.FormD);
        return new string(normalized.Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .Select(character => character is 'Đ' ? 'D' : character is 'đ' ? 'd' : character).ToArray()).Normalize(NormalizationForm.FormC);
    }
    private static string NormalizeAction(string action) => action.Trim().ToUpperInvariant() switch
    {
        "UPSERT" => "UPSERT",
        "DEACTIVATE" => "DEACTIVATE",
        _ => throw Invalid()
    };
    private static PaymentSettingsFailure Invalid() => new(400, "invalid_payment_destination", "Vui lòng kiểm tra ngân hàng, số tài khoản và tên người nhận.");
    private static PaymentSettingsFailure Conflict() => new(409, "payment_destination_changed", "Cấu hình đã được thay đổi bởi phiên làm việc khác. Vui lòng tải lại.");
    private static PaymentDestinationView View(PaymentDestination item) => new(item.Id, item.Version, item.BankBin,
        item.BankCode, item.BankName, "******" + item.AccountNumberLast4, item.AccountName, item.IsActive,
        item.UpdatedBy, item.UpdatedAt, item.SePayAccountId, item.CreatedBy, item.CreatedAt);
}
