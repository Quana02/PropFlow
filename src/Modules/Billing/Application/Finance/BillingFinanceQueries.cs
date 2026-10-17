using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Payments.Contracts;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.Modules.Billing.Application.Finance;

public sealed record PagedResult<T>(int Total, int Page, int PageSize, IReadOnlyList<T> Items, IReadOnlyDictionary<string, int>? Counts = null);
public sealed record InvoiceBalance(Guid Id, string InvoiceNumber, Guid ApartmentUnitId, DateOnly BillingPeriodStart,
    DateOnly BillingPeriodEnd, DateOnly? IssueDate, DateOnly? DueDate, decimal TotalAmount,
    decimal ConfirmedPaidAmount, decimal OutstandingAmount, string Status, bool IsOverdue, int DaysOverdue, string? InvoiceType = null, string? ApartmentNumber = null, string? ResidentName = null);

public sealed class BillingFinanceQueries(IInvoiceFinancialSource invoices, IConfirmedPaymentSource payments,
    ICurrentBuildingTimeZone buildingTimeZone, TimeProvider? clock = null, IBillingHouseholdDirectory? directory = null)
{
    public async Task<PagedResult<InvoiceBalance>> UnpaidAsync(string? search, int page, int pageSize, string sortDirection, CancellationToken ct, string? filter = null)
    {
        var rows = await BalancesAsync(ct);
        var query = rows.Where(x => x.OutstandingAmount > 0 && !x.IsOverdue);
        query = await SearchAsync(query, search, ct);
        var today = await TodayAsync(ct);
        var counts = new Dictionary<string,int> { ["ALL"] = query.Count(), ["UNPAID"] = query.Count(x => x.ConfirmedPaidAmount == 0), ["PARTIAL"] = query.Count(x => x.ConfirmedPaidAmount > 0), ["DUE_SOON"] = query.Count(x => x.DueDate >= today && x.DueDate <= today.AddDays(3)) };
        query = filter switch { "UNPAID" => query.Where(x => x.ConfirmedPaidAmount == 0), "PARTIAL" => query.Where(x => x.ConfirmedPaidAmount > 0), "DUE_SOON" => query.Where(x => x.DueDate >= today && x.DueDate <= today.AddDays(3)), _ => query };
        query = sortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase)
            ? query.OrderBy(x => x.DueDate).ThenBy(x => x.InvoiceNumber)
            : query.OrderByDescending(x => x.DueDate).ThenByDescending(x => x.InvoiceNumber);
        return Page(query, page, pageSize, counts);
    }

    public async Task<PagedResult<InvoiceBalance>> OverdueAsync(string? search, int page, int pageSize, string sortDirection, CancellationToken ct, string? filter = null)
    {
        var query = (await BalancesAsync(ct)).Where(x => x.IsOverdue && x.OutstandingAmount > 0);
        query = await SearchAsync(query, search, ct);
        var counts = new Dictionary<string,int> { ["ALL"] = query.Count(), ["30_60"] = query.Count(x => x.DaysOverdue >= 30 && x.DaysOverdue <= 60), ["60_90"] = query.Count(x => x.DaysOverdue >= 61 && x.DaysOverdue <= 90), ["OVER_90"] = query.Count(x => x.DaysOverdue > 90) };
        query = filter switch { "30_60" => query.Where(x => x.DaysOverdue >= 30 && x.DaysOverdue <= 60), "60_90" => query.Where(x => x.DaysOverdue >= 61 && x.DaysOverdue <= 90), "OVER_90" => query.Where(x => x.DaysOverdue > 90), _ => query };
        query = sortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase)
            ? query.OrderBy(x => x.DaysOverdue).ThenBy(x => x.InvoiceNumber)
            : query.OrderByDescending(x => x.DaysOverdue).ThenByDescending(x => x.InvoiceNumber);
        return Page(query, page, pageSize, counts);
    }

    public async Task<InvoiceBalance?> DetailAsync(Guid id, CancellationToken ct)
    {
        var invoice = await invoices.FindAsync(id, ct);
        if (invoice is null) return null;
        return Balance(invoice, await payments.TotalAsync(id, ct), await TodayAsync(ct));
    }

    private async Task<IReadOnlyList<InvoiceBalance>> BalancesAsync(CancellationToken ct)
    {
        var rows = await invoices.ListIssuedAsync(ct);
        var totals = await payments.TotalsAsync(rows.Select(x => x.Id).ToArray(), ct);
        var today = await TodayAsync(ct);
        return rows.Select(x => Balance(x, totals.GetValueOrDefault(x.Id), today)).ToArray();
    }

    private async Task<IEnumerable<InvoiceBalance>> SearchAsync(IEnumerable<InvoiceBalance> rows, string? search, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(search)) return rows;
        var term = search.Trim();
        var households = directory is null ? [] : await directory.ListAsync(await TodayAsync(ct), ct);
        return rows.Where(x => x.InvoiceNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
            || x.ApartmentNumber?.Contains(term, StringComparison.OrdinalIgnoreCase) == true || x.ResidentName?.Contains(term, StringComparison.OrdinalIgnoreCase) == true
            || x.ResidentName is null && households.Any(h => h.ApartmentId == x.ApartmentUnitId && (h.UnitNumber.Contains(term, StringComparison.OrdinalIgnoreCase) || h.FullName.Contains(term, StringComparison.OrdinalIgnoreCase))));
    }
    private async Task<DateOnly> TodayAsync(CancellationToken ct)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(await buildingTimeZone.GetAsync(ct));
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime((clock ?? TimeProvider.System).GetUtcNow(), zone).DateTime);
    }

    private static InvoiceBalance Balance(InvoiceFinancialSnapshot x, decimal paid, DateOnly today)
    {
        var outstanding = Math.Max(0, x.TotalAmount - paid);
        var overdue = x.DueDate is { } due && due < today && outstanding > 0;
        return new(x.Id, x.InvoiceNumber, x.ApartmentUnitId, x.BillingPeriodStart, x.BillingPeriodEnd, x.IssueDate, x.DueDate,
            x.TotalAmount, paid, outstanding, x.Status, overdue, overdue ? today.DayNumber - x.DueDate!.Value.DayNumber : 0, x.InvoiceType, x.ApartmentNumber, x.ResidentName);
    }

    private static PagedResult<InvoiceBalance> Page(IEnumerable<InvoiceBalance> query, int page, int size, IReadOnlyDictionary<string,int>? counts = null)
    {
        page = Math.Max(1, page); size = Math.Clamp(size, 1, 100); var rows = query.ToArray();
        page = Math.Min(page, Math.Max(1, (int)Math.Ceiling(rows.Length / (double)size)));
        return new(rows.Length, page, size, rows.Skip((page - 1) * size).Take(size).ToArray(), counts);
    }
}
