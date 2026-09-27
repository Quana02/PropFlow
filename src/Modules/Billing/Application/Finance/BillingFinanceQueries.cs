using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Payments.Contracts;

namespace PropFlow.Modules.Billing.Application.Finance;

public sealed record PagedResult<T>(int Total, int Page, int PageSize, IReadOnlyList<T> Items);
public sealed record InvoiceBalance(Guid Id, string InvoiceNumber, Guid ApartmentUnitId, DateOnly BillingPeriodStart,
    DateOnly BillingPeriodEnd, DateOnly? IssueDate, DateOnly? DueDate, decimal TotalAmount,
    decimal ConfirmedPaidAmount, decimal OutstandingAmount, string Status, bool IsOverdue, int DaysOverdue);

public sealed class BillingFinanceQueries(IInvoiceFinancialSource invoices, IConfirmedPaymentSource payments)
{
    public async Task<PagedResult<InvoiceBalance>> UnpaidAsync(string? search, int page, int pageSize, string sortDirection, CancellationToken ct)
    {
        var rows = await BalancesAsync(ct);
        var query = rows.Where(x => x.OutstandingAmount > 0);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.InvoiceNumber.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));
        query = sortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase)
            ? query.OrderBy(x => x.DueDate).ThenBy(x => x.InvoiceNumber)
            : query.OrderByDescending(x => x.DueDate).ThenByDescending(x => x.InvoiceNumber);
        return Page(query, page, pageSize);
    }

    public async Task<PagedResult<InvoiceBalance>> OverdueAsync(string? search, int page, int pageSize, string sortDirection, CancellationToken ct)
    {
        var query = (await BalancesAsync(ct)).Where(x => x.IsOverdue && x.OutstandingAmount > 0);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.InvoiceNumber.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));
        query = sortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase)
            ? query.OrderBy(x => x.DaysOverdue).ThenBy(x => x.InvoiceNumber)
            : query.OrderByDescending(x => x.DaysOverdue).ThenByDescending(x => x.InvoiceNumber);
        return Page(query, page, pageSize);
    }

    public async Task<InvoiceBalance?> DetailAsync(Guid id, CancellationToken ct)
    {
        var invoice = await invoices.FindAsync(id, ct);
        if (invoice is null) return null;
        return Balance(invoice, await payments.TotalAsync(id, ct), DateOnly.FromDateTime(DateTime.UtcNow));
    }

    private async Task<IReadOnlyList<InvoiceBalance>> BalancesAsync(CancellationToken ct)
    {
        var rows = await invoices.ListIssuedAsync(ct);
        var totals = await payments.TotalsAsync(rows.Select(x => x.Id).ToArray(), ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return rows.Select(x => Balance(x, totals.GetValueOrDefault(x.Id), today)).ToArray();
    }

    private static InvoiceBalance Balance(InvoiceFinancialSnapshot x, decimal paid, DateOnly today)
    {
        var outstanding = Math.Max(0, x.TotalAmount - paid);
        var overdue = x.DueDate is { } due && due < today && outstanding > 0;
        return new(x.Id, x.InvoiceNumber, x.ApartmentUnitId, x.BillingPeriodStart, x.BillingPeriodEnd, x.IssueDate, x.DueDate,
            x.TotalAmount, paid, outstanding, x.Status, overdue, overdue ? today.DayNumber - x.DueDate!.Value.DayNumber : 0);
    }

    private static PagedResult<InvoiceBalance> Page(IEnumerable<InvoiceBalance> query, int page, int size)
    {
        page = Math.Max(1, page); size = Math.Clamp(size, 1, 100); var rows = query.ToArray();
        return new(rows.Length, page, size, rows.Skip((page - 1) * size).Take(size).ToArray());
    }
}
