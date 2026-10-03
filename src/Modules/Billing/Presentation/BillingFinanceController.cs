using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.Billing.Application.Finance;

namespace PropFlow.Modules.Billing.Presentation;

[ApiController, Route("api/v1/billing"), Authorize(Roles = "ACCOUNTANT", Policy = "finance.manage")]
[Produces("application/json")]
public sealed class BillingFinanceController(BillingFinanceQueries queries) : ControllerBase
{
    [HttpGet("invoices/unpaid")]
    public Task<PagedResult<InvoiceBalance>> Unpaid(string? search, int page = 1, int pageSize = 20,
        string sortDirection = "desc", CancellationToken ct = default) => queries.UnpaidAsync(search, page, pageSize, sortDirection, ct);

    [HttpGet("debts/overdue")]
    public Task<PagedResult<InvoiceBalance>> Overdue(string? search, int page = 1, int pageSize = 20,
        string sortDirection = "desc", CancellationToken ct = default) => queries.OverdueAsync(search, page, pageSize, sortDirection, ct);

    [HttpGet("invoices/{id:guid}")]
    public async Task<ActionResult<InvoiceBalance>> Detail(Guid id, CancellationToken ct) =>
        await queries.DetailAsync(id, ct) is { } result ? Ok(result) : NotFound(new ProblemDetails
        { Status = 404, Title = "Không tìm thấy hóa đơn.", Extensions = { ["code"] = "invoice_not_found" } });
}
