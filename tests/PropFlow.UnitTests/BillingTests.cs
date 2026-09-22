using System.Reflection;
using PropFlow.Modules.Billing.Domain.FeeRateRules;
using PropFlow.Modules.Billing.Domain.FeeTypes;
using PropFlow.Modules.Billing.Domain.InvoiceItems;
using PropFlow.Modules.Billing.Domain.Invoices;
using PropFlow.Modules.Billing.Domain.InvoiceStatusHistories;

namespace PropFlow.UnitTests;

public class BillingTests
{
    private readonly DateTimeOffset _now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
    private readonly DateOnly _periodStart = new(2026, 9, 1);
    private readonly DateOnly _periodEnd = new(2026, 9, 30);

    [Fact]
    public void FeeType_Constructor_CreatesActiveFeeTypeAndNormalizesCodes()
    {
        var feeType = NewFeeType();

        Assert.NotEqual(Guid.Empty, feeType.Id);
        Assert.Equal("MANAGEMENT", feeType.Code);
        Assert.Equal("AREA", feeType.DefaultCalculationMethodCode);
        Assert.Equal("MONTHLY", feeType.DefaultBillingFrequencyCode);
        Assert.Equal(MasterDataStatus.ACTIVE, feeType.Status);
        Assert.Equal(_now, feeType.CreatedAt);
        Assert.Throws<ArgumentException>(() => new FeeType("", "Name", "AREA", "MONTHLY", Guid.NewGuid(), _now));
        Assert.Throws<ArgumentException>(() => new FeeType("CODE", "Name", "AREA", "MONTHLY", Guid.Empty, _now));
    }

    [Fact]
    public void FeeType_CanBeUpdatedAndDeactivatedWithoutHardDeleteWorkflow()
    {
        var feeType = NewFeeType();
        var updatedBy = Guid.NewGuid();
        var updatedAt = _now.AddHours(1);

        feeType.Update("Management Fee", "fixed", "monthly", updatedBy, updatedAt, "Monthly fee");
        feeType.Deactivate(updatedBy, updatedAt.AddMinutes(1));

        Assert.Equal("Management Fee", feeType.Name);
        Assert.Equal("FIXED", feeType.DefaultCalculationMethodCode);
        Assert.Equal(MasterDataStatus.INACTIVE, feeType.Status);
        Assert.Equal(updatedBy, feeType.UpdatedBy);
    }

    [Fact]
    public void FeeRateRule_Constructor_ValidatesFinancialAndDateInvariants()
    {
        var rule = NewFeeRateRule();

        Assert.NotEqual(Guid.Empty, rule.Id);
        Assert.True(rule.IsActive);
        Assert.Equal(15000m, rule.UnitRate);
        Assert.Equal(new DateOnly(2026, 9, 1), rule.EffectiveFrom);
        Assert.Throws<ArgumentOutOfRangeException>(() => new FeeRateRule(Guid.NewGuid(), "Rule", "AREA", "MONTHLY", -1m, _periodStart, Guid.NewGuid(), _now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FeeRateRule(Guid.NewGuid(), "Rule", "AREA", "MONTHLY", 1m, _periodStart, Guid.NewGuid(), _now, minimumAmount: -1m));
        Assert.Throws<ArgumentException>(() => new FeeRateRule(Guid.NewGuid(), "Rule", "AREA", "MONTHLY", 1m, _periodStart, Guid.NewGuid(), _now, minimumAmount: 100m, maximumAmount: 50m));
        Assert.Throws<ArgumentException>(() => new FeeRateRule(Guid.NewGuid(), "Rule", "AREA", "MONTHLY", 1m, _periodEnd, Guid.NewGuid(), _now, effectiveTo: _periodStart));
    }

    [Fact]
    public void Invoice_Constructor_CreatesDraftInvoiceAndValidatesDates()
    {
        var invoice = NewInvoice();

        Assert.NotEqual(Guid.Empty, invoice.Id);
        Assert.Equal(InvoiceStatus.DRAFT, invoice.Status);
        Assert.Equal(0m, invoice.Subtotal);
        Assert.Equal(0m, invoice.TotalAmount);
        Assert.Equal(_now, invoice.CreatedAt);
        Assert.Throws<ArgumentException>(() => new Invoice("", Guid.NewGuid(), _periodStart, _periodEnd, Guid.NewGuid(), _now));
        Assert.Throws<ArgumentException>(() => new Invoice("INV-2", Guid.Empty, _periodStart, _periodEnd, Guid.NewGuid(), _now));
        Assert.Throws<ArgumentException>(() => new Invoice("INV-3", Guid.NewGuid(), _periodEnd, _periodStart, Guid.NewGuid(), _now));
        Assert.Throws<ArgumentException>(() => new Invoice("INV-4", Guid.NewGuid(), _periodStart, _periodEnd, Guid.NewGuid(), _now, issueDate: _periodEnd, dueDate: _periodStart));
    }

    [Fact]
    public void Invoice_AddItem_RoundsLineAmountAndRecalculatesTotals()
    {
        var invoice = NewInvoice();
        var updatedBy = Guid.NewGuid();

        var first = invoice.AddItem("management", "Management fee", "area", 12.3456m, 1000m, updatedBy, _now.AddMinutes(1));
        var second = invoice.AddItem("parking", "Parking fee", "fixed", 1m, 250000m, updatedBy, _now.AddMinutes(2));

        Assert.Equal(12346m, first.LineAmount);
        Assert.Equal(250000m, second.LineAmount);
        Assert.Equal(262346m, invoice.Subtotal);
        Assert.Equal(invoice.Subtotal, invoice.TotalAmount);
        Assert.Equal(updatedBy, invoice.UpdatedBy);
    }

    [Fact]
    public void Invoice_Issue_MakesFinancialSnapshotImmutable()
    {
        var invoice = NewInvoice();
        var accountant = Guid.NewGuid();
        invoice.AddItem("management", "Management fee", "area", 10m, 1000m, accountant, _now.AddMinutes(1));
        var issuedAt = _now.AddHours(1);
        var issueDate = new DateOnly(2026, 10, 1);
        var dueDate = new DateOnly(2026, 10, 10);

        invoice.Issue(accountant, issueDate, issuedAt, dueDate);

        Assert.Equal(InvoiceStatus.ISSUED, invoice.Status);
        Assert.Equal(issueDate, invoice.IssueDate);
        Assert.Equal(dueDate, invoice.DueDate);
        Assert.Equal(issuedAt, invoice.IssuedAt);
        Assert.Equal(accountant, invoice.IssuedBy);
        Assert.Throws<InvalidOperationException>(() => invoice.AddItem("water", "Water", "fixed", 1m, 100m, accountant, issuedAt.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => invoice.UpdateDraft(_periodStart, _periodEnd, accountant, issuedAt.AddMinutes(2)));
    }

    [Fact]
    public void Invoice_Issue_RequiresAtLeastOneLineAndDueDateNotBeforeIssueDate()
    {
        var invoice = NewInvoice();
        var accountant = Guid.NewGuid();

        Assert.Throws<InvalidOperationException>(() => invoice.Issue(accountant, new DateOnly(2026, 10, 1), _now));

        invoice.AddItem("management", "Management fee", "fixed", 1m, 1000m, accountant, _now);
        Assert.Throws<ArgumentException>(() => invoice.Issue(accountant, new DateOnly(2026, 10, 10), _now, new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public void Invoice_Cancel_IsTerminalAndRequiresReason()
    {
        var invoice = NewInvoice();
        var accountant = Guid.NewGuid();
        var cancelledAt = _now.AddHours(1);

        invoice.Cancel(accountant, "Wrong billing period", cancelledAt);

        Assert.Equal(InvoiceStatus.CANCELLED, invoice.Status);
        Assert.Equal(accountant, invoice.CancelledBy);
        Assert.Equal(cancelledAt, invoice.CancelledAt);
        Assert.Equal("Wrong billing period", invoice.CancellationReason);
        Assert.Throws<InvalidOperationException>(() => invoice.Cancel(accountant, "Again", cancelledAt.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => invoice.AddItem("water", "Water", "fixed", 1m, 100m, accountant, cancelledAt.AddMinutes(2)));
    }

    [Fact]
    public void InvoiceItem_Constructor_ValidatesSnapshotValues()
    {
        var invoiceId = Guid.NewGuid();
        var item = new InvoiceItem(invoiceId, " fee ", "Fee", "fixed", 1.5m, 1001m, _now);

        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.Equal(invoiceId, item.InvoiceId);
        Assert.Equal("FEE", item.FeeCodeSnapshot);
        Assert.Equal("FIXED", item.CalculationMethodCodeSnapshot);
        Assert.Equal(1502m, item.LineAmount);
        Assert.Throws<ArgumentOutOfRangeException>(() => new InvoiceItem(invoiceId, "FEE", "Fee", "FIXED", 0m, 1m, _now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InvoiceItem(invoiceId, "FEE", "Fee", "FIXED", 1m, -1m, _now));
    }

    [Fact]
    public void InvoiceStatusHistory_IsAppendOnlySnapshot()
    {
        var history = new InvoiceStatusHistory(
            Guid.NewGuid(),
            InvoiceStatus.ISSUED,
            Guid.NewGuid(),
            _now,
            InvoiceStatus.DRAFT,
            "Issued");

        Assert.NotEqual(Guid.Empty, history.Id);
        Assert.Equal(InvoiceStatus.DRAFT, history.FromStatus);
        Assert.Equal(InvoiceStatus.ISSUED, history.ToStatus);

        var publicMethods = typeof(InvoiceStatusHistory)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .ToList();
        Assert.Empty(publicMethods);
    }

    private FeeType NewFeeType()
    {
        return new FeeType(" management ", "Management", "area", "monthly", Guid.NewGuid(), _now, "Building management fee");
    }

    private FeeRateRule NewFeeRateRule()
    {
        return new FeeRateRule(
            Guid.NewGuid(),
            "Management rate",
            "area",
            "monthly",
            15000m,
            _periodStart,
            Guid.NewGuid(),
            _now,
            unitName: "m2",
            minimumAmount: 0m,
            maximumAmount: 1000000m,
            ruleConfig: """{"basis":"area"}""",
            effectiveTo: _periodEnd);
    }

    private Invoice NewInvoice()
    {
        return new Invoice(
            "INV-2026-0001",
            Guid.NewGuid(),
            _periodStart,
            _periodEnd,
            Guid.NewGuid(),
            _now,
            note: "September invoice");
    }
}
