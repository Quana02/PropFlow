using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.ServiceRequests.Application.WorkAssignedServiceRequests;
using PropFlow.Modules.ServiceRequests.Application.ReviewManagerServiceRequests;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-05")]
public sealed class AdditionalCostConsentTests
{
    [Fact]
    public async Task PendingProposalBlocksWorkCompletionProgressAndManagerAcceptance()
    {
        await using var f = new Fixture(); await f.Seed();
        var proposal = await f.Propose();
        Assert.Equal("AWAITING_MANAGER", proposal.Status);
        Assert.Single(f.Db.ResidentEvents);
        Assert.Equal("ServiceRequestAdditionalCostReviewRequested", f.Db.ResidentEvents.Single().Name);
        Assert.Equal(409, (await Assert.ThrowsAsync<ServiceRequestCostFailure>(() => f.Consent.RespondAsync(f.OwnerId, f.Request.Id, new(proposal.ProposalId, true), default))).Status);
        Assert.Equal(StaffServiceRequestOutcome.Conflict, await f.Staff.AddProgressAsync(f.StaffId, f.Request.Id, "Không được làm trước", f.Now, default));
        Assert.Equal(StaffServiceRequestOutcome.Conflict, await f.Staff.CompleteWorkAsync(f.StaffId, f.Request.Id, "Hoàn thành công việc phát sinh", 50000, "Thay van", f.Now, default));
        Assert.Equal(ManagerServiceRequestOutcome.Conflict, await f.Manager.ApproveResolutionAsync(f.Request.Id, f.ManagerId, 50000, null, f.Now, default));
        Assert.Throws<InvalidOperationException>(() => f.Request.ApproveAdditionalCost(50000, f.Now));
        Assert.Throws<InvalidOperationException>(() => f.Request.Resolve(f.Now));
        Assert.DoesNotContain(f.Db.ServiceRequestActivities, x => x.ActivityType == ServiceActivityType.WORK_RESULT_ADDED);
    }

    [Fact]
    public async Task ManagerReviewIsIdempotentAndWorkStillWaitsForResident()
    {
        await using var f = new Fixture(); await f.Seed(); var proposal = await f.Propose();
        var reviewed = await f.Approve(proposal.ProposalId);
        await f.Approve(proposal.ProposalId);
        Assert.Equal("PENDING", reviewed.Status);
        Assert.Equal(2, await f.Db.ResidentEvents.CountAsync());
        var activity = Assert.Single(f.Db.ServiceRequestActivities.Where(x => x.ActivityType == ServiceActivityType.ADDITIONAL_COST_MANAGER_APPROVED));
        Assert.Equal(f.ManagerId, activity.PerformedBy);
        Assert.Equal(StaffServiceRequestOutcome.Conflict, await f.Staff.AddProgressAsync(f.StaffId, f.Request.Id, "Chưa được làm", f.Now, default));
        Assert.Throws<InvalidOperationException>(() => f.Request.Resolve(f.Now));
        var changed = await f.Consent.ProposeAsync(f.StaffId, f.Request.Id, new(60000, "Thay vật tư khác", proposal.ProposalId), default);
        Assert.Equal("AWAITING_MANAGER", changed.Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<ServiceRequestCostFailure>(() => f.Consent.RespondAsync(f.OwnerId, f.Request.Id, new(changed.ProposalId, true), default))).Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(120000)]
    public async Task AssignmentCannotSetAChargeAndDoesNotResetHistoricalCharges(decimal legacyCharge)
    {
        await using var f = new Fixture(); await f.Seed(legacyCharge);
        Assert.Equal(ManagerServiceRequestOutcome.InvalidInput, await f.Manager.AssignAsync(f.Request.Id, f.ManagerId, Guid.NewGuid(), 50000, null, f.Now, default));
        Assert.Equal(legacyCharge, f.Request.BaseServiceCost);
        Assert.Single(f.Request.Assignments);
        Assert.Equal(ManagerServiceRequestOutcome.Success, await f.Manager.AssignAsync(f.Request.Id, f.ManagerId, Guid.NewGuid(), 0, null, f.Now, default));
        Assert.Equal(legacyCharge, f.Request.BaseServiceCost);
    }

    [Fact]
    public async Task LinkingTheSameInvoiceTwiceEnqueuesOnlyOneResidentNotice()
    {
        await using var f = new Fixture(); await f.Seed();
        var invoice = Guid.NewGuid();
        Assert.Equal(ManagerServiceRequestOutcome.Success, await f.Manager.LinkInvoiceAsync(f.Request.Id, invoice, f.Now, default));
        Assert.Equal(ManagerServiceRequestOutcome.Success, await f.Manager.LinkInvoiceAsync(f.Request.Id, invoice, f.Now, default));
        Assert.Equal("ServiceRequestInvoiceReady", Assert.Single(f.Db.ResidentEvents).Name);
        Assert.Equal(ManagerServiceRequestOutcome.Conflict, await f.Manager.LinkInvoiceAsync(f.Request.Id, Guid.NewGuid(), f.Now, default));
        Assert.Single(f.Db.ResidentEvents);
    }

    [Fact]
    public async Task AcceptanceAllowsWorkAndFinalManagerChargeNeverExceedsConsentedAmount()
    {
        await using var f = new Fixture(); await f.Seed(); var proposal = await f.Propose();
        await f.Approve(proposal.ProposalId);
        var answer = await f.Consent.RespondAsync(f.OwnerId, f.Request.Id, new(proposal.ProposalId, true), default);
        Assert.Equal("ACCEPTED", answer.Status);
        var detail = await f.Staff.GetByIdAsync(f.StaffId, f.Request.Id, default);
        Assert.Equal("ACCEPTED", detail!.AdditionalCostConsent!.Status);
        Assert.Equal(StaffServiceRequestOutcome.Success, await f.Staff.CompleteWorkAsync(f.StaffId, f.Request.Id,
            "Đã hoàn tất thay van và kiểm tra kín nước.", 50000, "Thay van", f.Now.AddMinutes(1), default));
        Assert.Throws<InvalidOperationException>(() => f.Request.ApproveAdditionalCost(50001, f.Now));
        var activityId = await f.Staff.PendingWorkResultActivityIdAsync(f.StaffId, f.Request.Id, default);
        var evidence = new StaffEvidenceMetadata(Guid.NewGuid(), f.Request.Id, activityId!.Value, "result.jpg", "image/jpeg", 20, "key", f.StaffId, f.Now);
        Assert.Equal(StaffServiceRequestOutcome.Success, await f.Staff.AddEvidenceAsync(f.StaffId, f.Request.Id, activityId.Value, [evidence], f.Now.AddMinutes(2), default));
        Assert.Equal(ManagerServiceRequestOutcome.Success, await f.Manager.ApproveResolutionAsync(f.Request.Id, f.ManagerId, 40000, null, f.Now.AddMinutes(3), default));
        Assert.Equal(ServiceRequestStatus.RESOLVED, f.Request.Status);
        Assert.Equal(40000, f.Request.ApprovedAdditionalCost);
    }

    [Fact]
    public async Task RejectionAutomaticallyStopsRequestAndEveryActiveAssignmentWithoutInvoice()
    {
        await using var f = new Fixture(); await f.Seed(); var proposal = await f.Propose();
        await f.Approve(proposal.ProposalId);
        await f.Consent.RespondAsync(f.OwnerId, f.Request.Id, new(proposal.ProposalId, false), default);
        Assert.Equal(ServiceRequestStatus.CANCELLED, f.Request.Status);
        Assert.All(f.Request.Assignments, x => Assert.Equal(AssignmentStatus.CANCELLED, x.Status));
        Assert.Null(f.Request.ServiceInvoiceId);
        Assert.Equal("REJECTED", (await f.Staff.GetByIdAsync(f.StaffId, f.Request.Id, default))!.AdditionalCostConsent!.Status);
        Assert.NotEqual(StaffServiceRequestOutcome.Success, await f.Staff.CompleteWorkAsync(f.StaffId, f.Request.Id,
            "Không được làm công việc đã từ chối", 50000, "Thay van", f.Now, default));
        Assert.Equal(ManagerServiceRequestOutcome.Conflict, await f.Manager.ApproveResolutionAsync(f.Request.Id, f.ManagerId, 50000, null, f.Now, default));
        Assert.Single(f.Db.ServiceRequestActivities.Where(x => x.ActivityType == ServiceActivityType.ADDITIONAL_COST_REJECTED));
    }

    [Fact]
    public async Task OwnerAndAssignmentScopesAreRequiredForEveryCostMutation()
    {
        await using var f = new Fixture(); await f.Seed();
        Assert.Equal(404, (await Assert.ThrowsAsync<ServiceRequestCostFailure>(() => f.Consent.ProposeAsync(Guid.NewGuid(), f.Request.Id, new(50000, "Thay van"), default))).Status);
        var proposal = await f.Propose();
        Assert.Equal(404, (await Assert.ThrowsAsync<ServiceRequestCostFailure>(() => f.Consent.RespondAsync(f.OtherOwnerId, f.Request.Id, new(proposal.ProposalId, true), default))).Status);
        Assert.Equal("AWAITING_MANAGER", f.Request.AdditionalCostConsentStatus);
    }

    [Fact]
    public async Task StaleProposalCannotBeAcceptedAndIdenticalDecisionReplayIsIdempotent()
    {
        await using var f = new Fixture(); await f.Seed(); var first = await f.Propose();
        var second = await f.Consent.ProposeAsync(f.StaffId, f.Request.Id, new(60000, "Thay van khác", first.ProposalId), default);
        Assert.Equal(409, (await Assert.ThrowsAsync<ServiceRequestCostFailure>(() => f.Approve(first.ProposalId))).Status);
        await f.Approve(second.ProposalId);
        Assert.Equal(409, (await Assert.ThrowsAsync<ServiceRequestCostFailure>(() => f.Consent.RespondAsync(f.OwnerId, f.Request.Id, new(first.ProposalId, true), default))).Status);
        await f.Consent.RespondAsync(f.OwnerId, f.Request.Id, new(second.ProposalId, true), default);
        await f.Consent.RespondAsync(f.OwnerId, f.Request.Id, new(second.ProposalId, true), default);
        Assert.Single(f.Db.ServiceRequestActivities.Where(x => x.ActivityType == ServiceActivityType.ADDITIONAL_COST_ACCEPTED));
        Assert.Equal(409, (await Assert.ThrowsAsync<ServiceRequestCostFailure>(() => f.Consent.RespondAsync(f.OwnerId, f.Request.Id, new(second.ProposalId, false), default))).Status);
        Assert.Equal(3, await f.Db.ResidentEvents.CountAsync());
    }

    [Fact]
    public async Task CompletionCannotIntroduceOrChangeAnUnconsentedCost()
    {
        await using var f = new Fixture(); await f.Seed();
        Assert.Equal(StaffServiceRequestOutcome.Conflict, await f.Staff.CompleteWorkAsync(f.StaffId, f.Request.Id, "Đã sửa và tự thêm phí", 50000, "Thay van", f.Now, default));
        var proposal = await f.Propose();
        await f.Approve(proposal.ProposalId);
        await f.Consent.RespondAsync(f.OwnerId, f.Request.Id, new(proposal.ProposalId, true), default);
        Assert.Equal(StaffServiceRequestOutcome.Conflict, await f.Staff.CompleteWorkAsync(f.StaffId, f.Request.Id, "Đã sửa và đổi phí", 60000, "Thay van", f.Now, default));
        Assert.Equal(50000, f.Request.ProposedAdditionalCost);
    }

    private sealed class Fixture : IAsyncDisposable, IResidentResidenceSource, ICurrentBuildingTimeZone
    {
        public Guid StaffId = Guid.NewGuid(), OwnerId = Guid.NewGuid(), OtherOwnerId = Guid.NewGuid(), ResidentId = Guid.NewGuid(), ManagerId = Guid.NewGuid();
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public ServiceRequestsDbContext Db = new(new DbContextOptionsBuilder<ServiceRequestsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public ServiceRequest Request = null!;
        public StaffServiceRequestStore Staff => new(Db);
        public ManagerServiceRequestStore Manager => new(Db);
        public AdditionalCostConsentService Consent => new(Db, this, this, TimeProvider.System);
        public async Task Seed(decimal legacyCharge = 0)
        {
            Request = new("YC-COST", ResidentId, Guid.NewGuid(), "Sửa vòi nước", "Vòi nước cần sửa chữa", Now, submittedByUserId: OwnerId);
            Request.MarkUnderReview(Now); Request.SetBaseServiceCost(legacyCharge, Now); Request.MarkAssigned(Now); Request.MarkInProgress(Now);
            var assignment = new ServiceRequestAssignment(Request.Id, StaffId, ManagerId, Now); assignment.Start(Now);
            Db.AddRange(Request, assignment); await Db.SaveChangesAsync();
        }
        public Task<AdditionalCostConsentView> Propose() => Consent.ProposeAsync(StaffId, Request.Id, new(50000, "Thay van"), default);
        public Task<AdditionalCostConsentView> Approve(Guid proposal) => Consent.ApproveAsync(ManagerId, Request.Id, new(proposal), default);
        public Task<ActiveResidentResidence?> FindActiveAsync(Guid userId, DateOnly date, CancellationToken ct) =>
            Task.FromResult<ActiveResidentResidence?>(new(userId == OwnerId ? ResidentId : Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
        public Task<string> GetAsync(CancellationToken ct) => Task.FromResult("Asia/Ho_Chi_Minh");
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
