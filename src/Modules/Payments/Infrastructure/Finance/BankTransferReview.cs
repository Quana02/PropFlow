using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Contracts;
using PropFlow.Modules.Payments.Domain.BankTransfers;
using PropFlow.Modules.Payments.Domain.Payments;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Infrastructure.ResidentPayments;

namespace PropFlow.Modules.Payments.Infrastructure.Finance;

public sealed class BankTransferReview(PaymentsDbContext db, IInvoiceFinancialSource invoices,
    IDataProtectionProvider protection, TimeProvider clock)
{
    public async Task<PagedBankTransfers> ListAsync(string? status, int page, int pageSize, CancellationToken ct)
    {
        var query = db.BankTransfers.AsNoTracking();
        if (status is not (null or "" or "ALL")) query = query.Where(x => x.Status == status);
        pageSize = Math.Clamp(pageSize, 1, 50); page = Math.Max(1, page);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.ReceivedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        var identities = (await invoices.FindManyAsync(rows.Where(x => x.InvoiceId.HasValue).Select(x => x.InvoiceId!.Value).Distinct().ToArray(), ct)).ToDictionary(x => x.Id);
        return new(total, page, pageSize, rows.Select(x => View(x, x.InvoiceId.HasValue ? identities.GetValueOrDefault(x.InvoiceId.Value)?.InvoiceNumber : null)).ToArray());
    }

    public async Task<BankTransferRow> DetailAsync(Guid id, CancellationToken ct)
    {
        var row = await FindAsync(id, ct);
        var invoice = row.InvoiceId is { } invoiceId ? await invoices.FindAsync(invoiceId, ct) : null;
        var payload = JsonSerializer.Deserialize<SePayTransfer>(protection.CreateProtector("PropFlow.Payments.SePay.Inbox.v1").Unprotect(row.ProtectedPayload));
        return View(row, invoice?.InvoiceNumber) with { Content = payload?.Content };
    }

    public async Task<IReadOnlyList<BankTransferInvoiceOption>> CandidatesAsync(string? search, CancellationToken ct)
    {
        var issued = await invoices.ListIssuedAsync(ct);
        var term = search?.Trim();
        var ids = issued.Where(x => string.IsNullOrWhiteSpace(term) || x.InvoiceNumber.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            x.ApartmentNumber?.Contains(term, StringComparison.OrdinalIgnoreCase) == true || x.ResidentName?.Contains(term, StringComparison.OrdinalIgnoreCase) == true).Select(x => x.Id).ToArray();
        var pinned = await db.PaymentInstructions.AsNoTracking().Where(x => ids.Contains(x.InvoiceId)).Select(x => x.InvoiceId).ToArrayAsync(ct);
        var paid = await db.Payments.AsNoTracking().Where(x => pinned.Contains(x.InvoiceId) && x.Status == PaymentStatus.CONFIRMED)
            .GroupBy(x => x.InvoiceId).Select(x => new { Id = x.Key, Total = x.Sum(p => p.Amount) }).ToDictionaryAsync(x => x.Id, x => x.Total, ct);
        return issued.Where(x => pinned.Contains(x.Id) && x.TotalAmount > paid.GetValueOrDefault(x.Id))
            .OrderBy(x => x.InvoiceNumber).Take(50).Select(x => new BankTransferInvoiceOption(x.Id, x.InvoiceNumber,
                x.ApartmentNumber, x.ResidentName, x.TotalAmount - paid.GetValueOrDefault(x.Id))).ToArray();
    }

    public async Task AssignAsync(Guid id, AssignBankTransferRequest request, Guid actor, CancellationToken ct)
    {
        var row = await FindAsync(id, ct);
        if (row.Revision != request.ExpectedRevision) throw Conflict();
        var invoice = await invoices.FindAsync(request.InvoiceId, ct);
        if (invoice is null || invoice.Status != "ISSUED" || !await db.PaymentInstructions.AnyAsync(x => x.InvoiceId == request.InvoiceId, ct))
            throw new PaymentSettingsFailure(409, "bank_invoice_unavailable", "Hóa đơn chưa phát hành hoặc chưa có thông tin nhận tiền.");
        var paid = await db.Payments.Where(x => x.InvoiceId == request.InvoiceId && x.Status == PaymentStatus.CONFIRMED).SumAsync(x => x.Amount, ct);
        if (request.Amount > invoice.TotalAmount - paid) throw new PaymentSettingsFailure(409, "bank_allocation_exceeds_balance", "Số tiền phân bổ vượt quá số còn nợ của hóa đơn.");
        try { row.Assign(request.InvoiceId, request.Amount, actor, request.Note, clock.GetUtcNow()); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { throw new PaymentSettingsFailure(409, "bank_assignment_invalid", ex.Message); }
        await SaveAsync(ct);
    }

    public async Task DismissAsync(Guid id, DismissBankTransferRequest request, Guid actor, CancellationToken ct)
    {
        var row = await FindAsync(id, ct);
        if (row.Revision != request.ExpectedRevision) throw Conflict();
        try { row.Dismiss(actor, request.Note, clock.GetUtcNow()); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { throw new PaymentSettingsFailure(409, "bank_resolution_invalid", ex.Message); }
        await SaveAsync(ct);
    }

    private async Task<BankTransfer> FindAsync(Guid id, CancellationToken ct) =>
        await db.BankTransfers.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new PaymentSettingsFailure(404, "bank_transfer_missing", "Không tìm thấy giao dịch ngân hàng.");
    private async Task SaveAsync(CancellationToken ct)
    { try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { throw Conflict(); } }
    private static PaymentSettingsFailure Conflict() => new(409, "bank_transfer_changed", "Giao dịch vừa thay đổi. Vui lòng tải lại danh sách.");
    private static BankTransferRow View(BankTransfer row, string? number) => new(row.Id, row.TransactionId, row.Amount, row.Gateway,
        "******" + row.AccountLast4, row.TransferCode, row.BankReference, row.Status, row.Outcome, row.InvoiceId, number,
        row.PaymentId, row.AllocatedAmount, row.ReviewNote, row.ReviewedBy, row.ReceivedAt, row.UpdatedAt, row.Revision);
}
