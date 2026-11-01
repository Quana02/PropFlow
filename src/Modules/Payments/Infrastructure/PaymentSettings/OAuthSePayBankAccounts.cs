using System.Text.Json;
using PropFlow.Modules.Payments.Application.PaymentSettings;

namespace PropFlow.Modules.Payments.Infrastructure.PaymentSettings;

// OAuth uses integer provider IDs and nested bank data; API v2 uses UUIDs.
// Keep an opaque, connection-bound identity at the existing payment-settings boundary.
public sealed class OAuthSePayBankAccounts(SePayOAuth oauth) : ISePayBankAccounts
{
    public async Task<IReadOnlyList<SePayBankAccount>> ListAsync(CancellationToken ct)
    {
        var generation = (await oauth.AccessAsync(ct)).Generation;
        var result = new List<SePayBankAccount>();
        try
        {
            for (var page = 1; page <= 100; page++)
            {
                using var response = await oauth.GetAsync($"bank-accounts?limit=100&page={page}", ct);
                foreach (var item in response.RootElement.GetProperty("data").EnumerateArray()) result.Add(Parse(item, generation));
                if (!SePayOAuth.HasMore(response.RootElement, page))
                {
                    if ((await oauth.AccessAsync(ct)).Generation != generation) throw Invalid();
                    return result.DistinctBy(x => x.Id).ToArray();
                }
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { throw Invalid(); }
        throw Invalid();
    }

    public async Task<SePayBankAccount> GetAsync(Guid accountId, CancellationToken ct) =>
        (await ListAsync(ct)).SingleOrDefault(x => x.Id == accountId)
        ?? throw new PaymentSettingsFailure(409, "sepay_account_changed", "Tài khoản không thuộc kết nối SePay hiện tại. Vui lòng đồng bộ và chọn lại.");

    internal static SePayBankAccount Parse(JsonElement item, Guid generation)
    {
        static string Text(JsonElement node, string key) => node.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : "";
        var id = item.GetProperty("id").GetInt64();
        if (id <= 0) throw Invalid();
        var bank = item.GetProperty("bank");
        var bin = Text(bank, "bin"); var code = Text(bank, "code"); var name = Text(bank, "full_name");
        var number = Text(item, "account_number"); var holder = Text(item, "account_holder_name");
        var active = item.TryGetProperty("active", out var a) && (a.ValueKind == JsonValueKind.True || a.ValueKind == JsonValueKind.Number && a.GetInt32() == 1);
        return new(SePayOAuth.AccountId(generation, id), bin, code, name, number, holder,
            active && bin.Length == 6 && bin.All(char.IsAsciiDigit) && code.Length > 0 && name.Length > 0 &&
            number.Length is >= 6 and <= 19 && number.All(char.IsAsciiDigit) && holder.Length > 0);
    }

    private static PaymentSettingsFailure Invalid() => new(503, "sepay_invalid_response", "Danh sách tài khoản SePay chưa hợp lệ hoặc kết nối đã thay đổi. Vui lòng đồng bộ lại.");
}
