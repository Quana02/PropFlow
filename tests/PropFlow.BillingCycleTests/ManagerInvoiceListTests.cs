using PropFlow.Web.Client.Features.Finance.Services;

namespace PropFlow.BillingCycleTests;
public sealed class ManagerInvoiceListTests
{
    private static readonly DateOnly Today = new(2026, 10, 14);
    private static HouseholdInvoice Invoice(string number, string apartment, string resident, string type, decimal paid, DateOnly? due, DateOnly? period = null) =>
        new(Guid.NewGuid(), number, apartment, resident, period ?? new DateOnly(2026, 10, 1), due, 1000000, paid, 1000000 - paid, InvoiceType: type);
    private static HouseholdInvoice[] Rows() => [
        Invoice("HD-202610-A-0404", "A-0404", "Đặng Thị Bình", "SERVICE_FEE", 0, Today.AddDays(1)),
        Invoice("HT-202610-A-0503", "A-0503", "Nguyễn Văn An", "MONTHLY_RENTAL", 400000, Today.AddDays(-1)),
        Invoice("HD-202609-A-0404", "A-0404", "Đặng Thị Bình", "SERVICE_FEE", 1000000, Today.AddDays(-10), new(2026, 9, 1))];
    [Theory]
    [InlineData("dang thi binh", 2)]
    [InlineData("ĐẶNG THỊ BÌNH", 2)]
    [InlineData("  a-0404  ", 2)]
    [InlineData("ht-202610", 1)]
    [InlineData("khong-co", 0)]
    public void Search_matches_invoice_apartment_and_resident_with_or_without_accents(string search, int count) =>
        Assert.Equal(count, ManagerInvoiceList.Filter(Rows(), new(Search: search), Today).Length);
    [Fact]
    public void Period_type_and_payment_filters_intersect()
    {
        var result = ManagerInvoiceList.Filter(Rows(), new(Period: "2026-10", Type: "SERVICE_FEE", Settlement: "UNPAID"), Today);
        Assert.Equal("HD-202610-A-0404", Assert.Single(result).Number);
        Assert.Empty(ManagerInvoiceList.Filter(Rows(), new(Period: "2026-10", Type: "SERVICE_FEE", Settlement: "PAID"), Today));
    }
    [Fact]
    public void Overdue_filter_excludes_paid_and_due_today_but_includes_partial_debt()
    {
        var rows = Rows().Append(Invoice("HD-DUE-TODAY", "A-1", "Chủ hộ", "SERVICE_FEE", 0, Today)).ToArray();
        Assert.Equal("HT-202610-A-0503", Assert.Single(ManagerInvoiceList.Filter(rows, new(Settlement: "OVERDUE"), Today)).Number);
        Assert.Equal("HT-202610-A-0503", Assert.Single(ManagerInvoiceList.Filter(rows, new(Settlement: "PARTIAL"), Today)).Number);
    }
    [Fact]
    public void Results_keep_stable_period_and_invoice_order_for_page_navigation()
    {
        var rows = Rows();
        var first = ManagerInvoiceList.Filter(rows, new(), Today);
        var second = ManagerInvoiceList.Filter(rows.Reverse(), new(), Today);
        Assert.Equal(first.Select(x => x.Id), second.Select(x => x.Id));
        Assert.Equal("HD-202609-A-0404", first[^1].Number);
        Assert.Equal(3000000, first.Sum(x => x.Total));
        Assert.Equal(1600000, first.Sum(x => x.Outstanding));
    }
}
