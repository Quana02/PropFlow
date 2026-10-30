using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;

namespace PropFlow.UnitTests;

public sealed class ServiceRequestPaymentTests
{
    [Fact]
    public void ServiceRequest_TracksBaseProposedAndApprovedCosts()
    {
        var now = DateTimeOffset.UtcNow;
        var request = Create(now);
        request.MarkUnderReview(now);
        request.SetBaseServiceCost(120_000, now);
        request.MarkAssigned(now);
        request.MarkInProgress(now);
        request.ProposeAdditionalCost(80_000, "Thay van khóa nước", now);
        request.ApproveAdditionalCost(60_000, now);
        request.Resolve(now);

        Assert.Equal(120_000, request.BaseServiceCost);
        Assert.Equal(80_000, request.ProposedAdditionalCost);
        Assert.Equal(60_000, request.ApprovedAdditionalCost);
        Assert.Equal("Thay van khóa nước", request.AdditionalCostNote);
    }

    [Fact]
    public void ServiceRequest_RejectsApprovalAboveStaffProposal()
    {
        var now = DateTimeOffset.UtcNow;
        var request = Create(now);
        request.MarkUnderReview(now);
        request.MarkAssigned(now);
        request.MarkInProgress(now);
        request.ProposeAdditionalCost(50_000, "Vật tư phát sinh", now);

        Assert.Throws<InvalidOperationException>(() => request.ApproveAdditionalCost(50_001, now));
    }

    [Fact]
    public void ServiceRequest_LinksOnlyOneInvoice()
    {
        var request = Create(DateTimeOffset.UtcNow);
        var invoiceId = Guid.NewGuid();
        request.LinkServiceInvoice(invoiceId, DateTimeOffset.UtcNow);
        request.LinkServiceInvoice(invoiceId, DateTimeOffset.UtcNow);

        Assert.Equal(invoiceId, request.ServiceInvoiceId);
        Assert.Throws<InvalidOperationException>(() => request.LinkServiceInvoice(Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    private static ServiceRequest Create(DateTimeOffset now) => new(
        "YC-TEST-001", Guid.NewGuid(), Guid.NewGuid(), "Sửa vòi nước",
        "Vòi nước bị rò rỉ cần kiểm tra", now, apartmentUnitId: Guid.NewGuid(), submittedByUserId: Guid.NewGuid());
}
