using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Infrastructure.ResidentPayments;
using QRCoder;

namespace PropFlow.Modules.Payments.Infrastructure.PaymentSettings;

public sealed class SePayBankAccounts(HttpClient http, IOptions<SePayOptions> options, IPaymentBankDirectory banks) : ISePayBankAccounts
{
    public async Task<IReadOnlyList<SePayBankAccount>> ListAsync(CancellationToken ct)
    {
        var result = new List<SePayBankAccount>();
        IReadOnlyList<BankOption>? bankList = null;
        for (var page = 1; page <= 100; page++)
        {
            using var document = await ReadAsync($"?page={page}&per_page=100", ct);
            var root = document.RootElement;
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) throw InvalidResponse();
            foreach (var account in data.EnumerateArray())
            {
                bankList ??= await banks.ListAsync(ct);
                result.Add(Parse(account, bankList));
            }
            if (!root.TryGetProperty("meta", out var meta) || !meta.TryGetProperty("pagination", out var pagination))
            {
                if (data.GetArrayLength() < 100) return result.DistinctBy(x => x.Id).ToArray();
                throw InvalidResponse();
            }
            if (pagination.TryGetProperty("has_more", out var more) && more.ValueKind == JsonValueKind.False)
                return result.DistinctBy(x => x.Id).ToArray();
            if (pagination.TryGetProperty("last_page", out var last) && last.TryGetInt32(out var lastPage) && page >= lastPage)
                return result.DistinctBy(x => x.Id).ToArray();
        }
        throw new PaymentSettingsFailure(503, "sepay_too_many_accounts", "Danh sách tài khoản quá lớn. Vui lòng liên hệ hỗ trợ.");
    }

    public async Task<SePayBankAccount> GetAsync(Guid accountId, CancellationToken ct)
    {
        if (accountId == Guid.Empty) throw new PaymentSettingsFailure(400, "sepay_account_required", "Vui lòng chọn tài khoản SePay.");
        using var document = await ReadAsync($"/{accountId:D}", ct);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) throw InvalidResponse();
        var account = Parse(data, await banks.ListAsync(ct));
        if (account.Id != accountId) throw InvalidResponse();
        return account;
    }

    private async Task<JsonDocument> ReadAsync(string suffix, CancellationToken ct)
    {
        var token = options.Value.ApiToken;
        if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsControl))
            throw new PaymentSettingsFailure(409, "sepay_token_missing", "Chưa cấu hình API Token SePay ở máy chủ. Vui lòng cấu hình trước khi đồng bộ.");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://userapi.sepay.vn/v2/bank-accounts" + suffix);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized) throw new PaymentSettingsFailure(503, "sepay_unauthorized", "API Token SePay không hợp lệ hoặc đã hết hiệu lực.");
            if (response.StatusCode == HttpStatusCode.Forbidden) throw new PaymentSettingsFailure(503, "sepay_forbidden", "API Token chưa có quyền đọc tài khoản ngân hàng.");
            if (response.StatusCode == HttpStatusCode.NotFound) throw new PaymentSettingsFailure(404, "sepay_account_not_found", "Tài khoản không tồn tại hoặc không thuộc kết nối SePay này.");
            if (!response.IsSuccessStatusCode) throw new PaymentSettingsFailure(503, "sepay_unavailable", "Chưa thể tải tài khoản SePay. Vui lòng thử lại sau.");
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String || status.GetString() != "success")
            { document.Dispose(); throw InvalidResponse(); }
            return document;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw new PaymentSettingsFailure(503, "sepay_timeout", "SePay phản hồi quá thời gian chờ."); }
        catch (HttpRequestException) { throw new PaymentSettingsFailure(503, "sepay_unavailable", "Không thể kết nối SePay."); }
        catch (JsonException) { throw InvalidResponse(); }
    }

    private static SePayBankAccount Parse(JsonElement item, IReadOnlyList<BankOption> bankList)
    {
        static string Text(JsonElement node, string name) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
        if (!Guid.TryParse(Text(item, "id"), out var id) || id == Guid.Empty) throw InvalidResponse();
        var bin = Text(item, "bank_bin");
        var code = Text(item, "bank_code");
        var shortName = Text(item, "bank_short_name");
        var bank = bankList.FirstOrDefault(x => (!string.IsNullOrEmpty(bin) && x.Bin == bin) ||
            (!string.IsNullOrEmpty(code) && x.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(shortName) && x.ShortName.Equals(shortName, StringComparison.OrdinalIgnoreCase)));
        var active = item.TryGetProperty("active", out var a) &&
            (a.ValueKind == JsonValueKind.True || a.ValueKind == JsonValueKind.Number && a.TryGetInt32(out var n) && n == 1 ||
             a.ValueKind == JsonValueKind.String && a.GetString() is "1" or "true");
        var number = Text(item, "account_number");
        var name = Text(item, "account_holder_name");
        return new(id, bank?.Bin ?? bin, bank?.Code ?? code, bank?.Name ?? Text(item, "bank_full_name"), number, name,
            active && bank is not null && number.Length is >= 6 and <= 19 && number.All(char.IsAsciiDigit) && !string.IsNullOrWhiteSpace(name));
    }

    private static PaymentSettingsFailure InvalidResponse() => new(503, "sepay_invalid_response", "Dữ liệu tài khoản từ SePay chưa hợp lệ. Vui lòng thử đồng bộ lại.");
}

public sealed class SePayAccountSelection(ISePayBankAccounts accounts, PaymentDestinationSettings settings)
{
    public async Task<IReadOnlyList<SePayBankAccountView>> ListAsync(CancellationToken ct) =>
        (await accounts.ListAsync(ct)).Select(x => new SePayBankAccountView(x.Id, x.BankCode, x.BankName,
            Mask(x.AccountNumber), x.AccountName, x.Active)).ToArray();

    private async Task<PaymentDestinationDraft> DraftAsync(Guid accountId, int version, CancellationToken ct)
    {
        if (version < 0) throw new PaymentSettingsFailure(400, "invalid_version", "Phiên bản cấu hình không hợp lệ.");
        var account = await accounts.GetAsync(accountId, ct);
        if (account.Id != accountId) throw new PaymentSettingsFailure(409, "sepay_account_changed", "Thông tin tài khoản SePay đã thay đổi. Vui lòng đồng bộ lại.");
        if (!account.Active) throw new PaymentSettingsFailure(409, "sepay_account_inactive", "Tài khoản SePay chưa hoạt động hoặc chưa đủ thông tin để nhận thanh toán.");
        return new(account.BankBin, account.BankCode, account.BankName, account.AccountNumber, account.AccountName, version, account.Id);
    }

    public async Task<PrivilegedActionChallengeResult> ChallengeAsync(Guid actor, SelectSePayAccountRequest request, CancellationToken ct) =>
        await settings.RequestChallengeAsync(actor, new("UPSERT", await DraftAsync(request.AccountId, request.ExpectedVersion, ct)), ct);

    public async Task<PaymentDestinationView> SaveAsync(Guid actor, ConfirmSePayAccountRequest request, CancellationToken ct) =>
        await settings.SaveAsync(actor, new(await DraftAsync(request.AccountId, request.ExpectedVersion, ct), request.ChallengeId, request.Code), ct);

    public async Task<PaymentDestinationPreview> PreviewAsync(SelectSePayAccountRequest request, CancellationToken ct)
    {
        var draft = await DraftAsync(request.AccountId, request.ExpectedVersion, ct);
        using var data = QRCodeGenerator.GenerateQrCode(ResidentServiceRequestPayments.VietQrPayload(draft.BankBin, draft.AccountNumber, null, ""), QRCodeGenerator.ECCLevel.M);
        var image = new PngByteQRCode(data).GetGraphic(8);
        return new("data:image/png;base64," + Convert.ToBase64String(image), draft.AccountName, Mask(draft.AccountNumber), draft.BankName);
    }

    private static string Mask(string number) => "******" + (number.Length >= 4 ? number[^4..] : "");
}
