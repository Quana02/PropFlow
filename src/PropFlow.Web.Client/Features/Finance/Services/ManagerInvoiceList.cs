using System.Globalization;
using System.Text;

namespace PropFlow.Web.Client.Features.Finance.Services;
public sealed record ManagerInvoiceFilter(string Search = "", string Period = "", string Type = "", string Settlement = "",
    IReadOnlyList<string>? Periods=null,IReadOnlyList<string>? Types=null,IReadOnlyList<string>? Settlements=null);
public static class ManagerInvoiceList
{
    public static HouseholdInvoice[] Filter(IEnumerable<HouseholdInvoice> invoices, ManagerInvoiceFilter filter, DateOnly today)
    {
        var search = Normalize(filter.Search.Trim());
        var periods=Selections(filter.Period,filter.Periods);var types=Selections(filter.Type,filter.Types);
        var settlements=Selections(filter.Settlement,filter.Settlements);
        return invoices.Where(i => (search.Length == 0 || Normalize($"{i.Number} {i.Apartment} {i.ResidentName}").Contains(search, StringComparison.Ordinal))
            && (periods.Length == 0 || periods.Contains(i.Period.ToString("yyyy-MM", CultureInfo.InvariantCulture)))
            && (types.Length == 0 || (i.InvoiceType is not null&&types.Contains(i.InvoiceType)))
            && (settlements.Length==0 || settlements.Any(value=>value switch {
                "PAID" => i.Outstanding == 0,
                "UNPAID" => i.Outstanding > 0 && i.Paid == 0,
                "PARTIAL" => i.Outstanding > 0 && i.Paid > 0,
                "OVERDUE" => i.Outstanding > 0 && i.DueDate is { } due && due < today,
                _ => value==filter.Settlement })))
            .OrderByDescending(i => i.Period).ThenBy(i => i.Number, StringComparer.Ordinal).ThenBy(i => i.Id).ToArray();
    }
    private static string[] Selections(string legacy,IReadOnlyList<string>? values)=>(values??[]).Concat(legacy.Length>0?[legacy]:[]).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
    private static string Normalize(string value) => new string(value.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Replace('đ', 'd').Replace('Đ', 'D').ToUpperInvariant();
}
