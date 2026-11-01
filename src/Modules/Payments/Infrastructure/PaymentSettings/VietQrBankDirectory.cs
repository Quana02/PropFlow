using System.Net.Http.Json;
using System.Text.Json.Serialization;
using PropFlow.Modules.Payments.Application.PaymentSettings;

namespace PropFlow.Modules.Payments.Infrastructure.PaymentSettings;

public sealed class VietQrBankDirectory(HttpClient http) : IPaymentBankDirectory
{
    public async Task<IReadOnlyList<BankOption>> ListAsync(CancellationToken ct)
    {
        try
        {
            var response = await http.GetFromJsonAsync<VietQrBanksResponse>("v2/banks", ct);
            if (response?.Code != "00" || response.Data is null)
                throw Unavailable();
            return response.Data.Where(item => !string.IsNullOrWhiteSpace(item.Bin) && !string.IsNullOrWhiteSpace(item.Code))
                .Select(item => new BankOption(item.Bin!, item.Code!, item.ShortName ?? item.Code!, item.Name ?? item.ShortName ?? item.Code!, item.Logo))
                .OrderBy(item => item.ShortName)
                .ToArray();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (PaymentSettingsFailure)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException or NotSupportedException)
        {
            throw Unavailable();
        }
    }

    private static PaymentSettingsFailure Unavailable() =>
        new(503, "bank_directory_unavailable", "Chưa thể tải danh sách ngân hàng. Vui lòng thử lại sau.");

    private sealed record VietQrBanksResponse(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("data")] VietQrBank[]? Data);
    private sealed record VietQrBank(
        [property: JsonPropertyName("bin")] string? Bin,
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("shortName")] string? ShortName,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("logo")] string? Logo);
}
