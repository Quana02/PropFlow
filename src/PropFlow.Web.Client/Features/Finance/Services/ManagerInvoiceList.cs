using System.Globalization;
using System.Text;

namespace PropFlow.Web.Client.Features.Finance.Services;
public sealed record ManagerInvoiceFilter(string Search = "", string Period = "", string Type = "", string Settlement = "");
public static class ManagerInvoiceList
{
    public static HouseholdInvoice[] Filter(IEnumerable<HouseholdInvoice> invoices, ManagerInvoiceFilter filter, DateOnly today)
    {
        var search = Normalize(filter.Search.Trim());
        return invoices.Where(i => (search.Length == 0 || Normalize($"{i.Number} {i.Apartment} {i.ResidentName}").Contains(search, StringComparison.Ordinal))
            && (filter.Period.Length == 0 || i.Period.ToString("yyyy-MM", CultureInfo.InvariantCulture) == filter.Period)
            && (filter.Type.Length == 0 || i.InvoiceType == filter.Type)
            && (filter.Settlement switch {
                "PAID" => i.Outstanding == 0,
                "UNPAID" => i.Outstanding > 0 && i.Paid == 0,
                "PARTIAL" => i.Outstanding > 0 && i.Paid > 0,
                "OVERDUE" => i.Outstanding > 0 && i.DueDate is { } due && due < today,
                _ => true }))
            .OrderByDescending(i => i.Period).ThenBy(i => i.Number, StringComparer.Ordinal).ThenBy(i => i.Id).ToArray();
    }
    private static string Normalize(string value) => new string(value.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Replace('đ', 'd').Replace('Đ', 'D').ToUpperInvariant();
}
