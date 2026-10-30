using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Billing.Domain.Invoices;
using PropFlow.Modules.Billing.Domain.InvoiceStatusHistories;
using PropFlow.Modules.Billing.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Contracts;

namespace PropFlow.Modules.Billing.Infrastructure.Finance;

public sealed class ServiceRequestBilling(
    BillingDbContext db,
    IBillingHouseholdDirectory households,
    IConfirmedPaymentSource payments) : IServiceRequestBilling
{
    public async Task<ServiceRequestSettlementSnapshot?> IssueAsync(ServiceRequestInvoiceCommand command, CancellationToken ct)
    {
        var total = command.BaseAmount + command.AdditionalAmount;
        if (total <= 0) return null;

        var existing = await FindAsync(command.ServiceRequestId, null, null, ct);
        if (existing is not null) return existing;

        var date = DateOnly.FromDateTime(command.ApprovedAt.UtcDateTime);
        var invoice = new Invoice(
            $"DV-{command.RequestNumber}"[..Math.Min(40, 3 + command.RequestNumber.Length)],
            command.ApartmentUnitId,
            date,
            date,
            command.ApprovedBy,
            command.ApprovedAt,
            issueDate: date,
            dueDate: date.AddDays(7),
            note: $"Chi phí xử lý yêu cầu {command.RequestNumber}",
            invoiceType: InvoiceType.SERVICE_REQUEST,
            sourceServiceRequestId: command.ServiceRequestId);

        if (command.BaseAmount > 0)
            invoice.AddItem("SERVICE_REQUEST_BASE", "Chi phí dịch vụ", "FIXED", 1, command.BaseAmount,
                command.ApprovedBy, command.ApprovedAt, description: $"Yêu cầu {command.RequestNumber}", unitName: "lần");
        if (command.AdditionalAmount > 0)
            invoice.AddItem("SERVICE_REQUEST_ADDITIONAL", "Chi phí phát sinh", "FIXED", 1, command.AdditionalAmount,
                command.ApprovedBy, command.ApprovedAt, description: command.AdditionalNote, unitName: "lần");

        invoice.Issue(command.ApprovedBy, date, command.ApprovedAt, date.AddDays(7));
        var candidates = await households.ListAsync(date, ct);
        var household = candidates.FirstOrDefault(x => x.ApartmentId == command.ApartmentUnitId && x.ResidentId == command.ResidentId)
            ?? candidates.FirstOrDefault(x => x.ApartmentId == command.ApartmentUnitId);
        db.Invoices.Add(invoice);
        db.InvoiceRecipients.Add(new InvoiceRecipient(invoice.Id, command.ResidentUserId,
            string.IsNullOrWhiteSpace(command.ResidentName) ? household?.FullName ?? "Cư dân" : command.ResidentName,
            household?.Email, household?.UnitNumber ?? "N/A", household?.AreaM2 ?? 0,
            command.ResidentId, "SERVICE_REQUESTER"));
        db.InvoiceStatusHistory.Add(new InvoiceStatusHistory(invoice.Id, InvoiceStatus.ISSUED,
            command.ApprovedBy, command.ApprovedAt, InvoiceStatus.DRAFT, "Phát hành từ yêu cầu dịch vụ"));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var raced = await FindAsync(command.ServiceRequestId, null, null, ct);
            if (raced is not null) return raced;
            throw;
        }

        return new(command.ServiceRequestId, invoice.Id, invoice.InvoiceNumber, invoice.TotalAmount, 0,
            invoice.TotalAmount, "UNPAID");
    }

    public Task<ServiceRequestSettlementSnapshot?> FindForResidentAsync(
        Guid serviceRequestId, Guid residentId, CancellationToken ct) =>
        FindAsync(serviceRequestId, residentId, null, ct);

    public Task<ServiceRequestSettlementSnapshot?> FindForUserAsync(
        Guid serviceRequestId, Guid userId, CancellationToken ct) =>
        FindAsync(serviceRequestId, null, userId, ct);

    private async Task<ServiceRequestSettlementSnapshot?> FindAsync(
        Guid serviceRequestId, Guid? residentId, Guid? userId, CancellationToken ct)
    {
        var invoice = await db.Invoices.AsNoTracking()
            .Where(x => x.SourceServiceRequestId == serviceRequestId &&
                (!residentId.HasValue || db.InvoiceRecipients.Any(r => r.InvoiceId == x.Id && r.ResidentId == residentId)) &&
                (!userId.HasValue || db.InvoiceRecipients.Any(r => r.InvoiceId == x.Id && r.UserId == userId)))
            .Select(x => new { x.Id, x.InvoiceNumber, x.TotalAmount, x.Status })
            .SingleOrDefaultAsync(ct);
        if (invoice is null) return null;
        var paid = await payments.TotalAsync(invoice.Id, ct);
        var outstanding = Math.Max(0, invoice.TotalAmount - paid);
        var status = invoice.Status == InvoiceStatus.CANCELLED ? "CANCELLED" : outstanding == 0 ? "PAID" : paid > 0 ? "PARTIAL" : "UNPAID";
        return new(serviceRequestId, invoice.Id, invoice.InvoiceNumber, invoice.TotalAmount, paid, outstanding, status);
    }
}
