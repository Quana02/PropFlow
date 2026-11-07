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
        Assert.Equal("PENDING", proposal.Status);
        Assert.Single(f.Db.ResidentEvents);
        Assert.Equal(StaffServiceRequestOutcome.Conflict, await f.Staff.AddProgressAsync(f.StaffId, f.Request.Id, "Không được làm trước", f.Now, default));
        Assert.Equal(StaffServiceRequestOutcome.Conflict, await f.Staff.CompleteWorkAsync(f.StaffId, f.Request.Id, "Hoàn thành công việc phát sinh", 50000, "Thay van", f.Now, default));
        Assert.Equal(ManagerServiceRequestOutcome.Conflict, await f.Manager.ApproveResolutionAsync(f.Request.Id, f.ManagerId, 50000, null, f.Now, default));
        Assert.Throws<InvalidOperationException>(() => f.Request.ApproveAdditionalCost(50000, f.Now));
        Assert.Throws<InvalidOperationException>(() => f.Request.Resolve(f.Now));
        Assert.DoesNotContain(f.Db.ServiceRequestActivities, x => x.ActivityType == ServiceActivityType.WORK_RESULT_ADDED);
    }

    [Fact]
    public async Task AcceptanceAllowsWorkAndFinalManagerChargeNeverExceedsConsentedAmount()
    {
        await using var f = new Fixture(); await f.Seed(); var proposal = await f.Propose();
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
        Assert.Equal("PENDING", f.Request.AdditionalCostConsentStatus);
    }

    [Fact]
    public async Task StaleProposalCannotBeAcceptedAndIdenticalDecisionReplayIsIdempotent()
    {
        await using var f = new Fixture(); await f.Seed(); var first = await f.Propose();
        var second = await f.Consent.ProposeAsync(f.StaffId, f.Request.Id, new(60000, "Thay van khác", first.ProposalId), default);
        Assert.Equal(409, (await Assert.ThrowsAsync<ServiceRequestCostFailure>(() => f.Consent.RespondAsync(f.OwnerId, f.Request.Id, new(first.ProposalId, true), default))).Status);
        await f.Consent.RespondAsync(f.OwnerId, f.Request.Id, new(second.ProposalId, true), default);
        await f.Consent.RespondAsync(f.OwnerId, f.Request.Id, new(second.ProposalId, true), default);
        Assert.Single(f.Db.ServiceRequestActivities.Where(x => x.ActivityType == ServiceActivityType.ADDITIONAL_COST_ACCEPTED));
        Assert.Equal(409, (await Assert.ThrowsAsync<ServiceRequestCostFailure>(() => f.Consent.RespondAsync(f.OwnerId, f.Request.Id, new(second.ProposalId, false), default))).Status);
        Assert.Equal(2, await f.Db.ResidentEvents.CountAsync());
    }

    [Fact]
    public async Task CompletionCannotIntroduceOrChangeAnUnconsentedCost()
    {
        await using var f = new Fixture(); await f.Seed();
        Assert.Equal(StaffServiceRequestOutcome.Conflict, await f.Staff.CompleteWorkAsync(f.StaffId, f.Request.Id, "Đã sửa và tự thêm phí", 50000, "Thay van", f.Now, default));
        var proposal = await f.Propose();
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
        public async Task Seed()
        {
            Request = new("YC-COST", ResidentId, Guid.NewGuid(), "Sửa vòi nước", "Vòi nước cần sửa chữa", Now, submittedByUserId: OwnerId);
            Request.MarkUnderReview(Now); Request.MarkAssigned(Now); Request.MarkInProgress(Now);
            var assignment = new ServiceRequestAssignment(Request.Id, StaffId, ManagerId, Now); assignment.Start(Now);
            Db.AddRange(Request, assignment); await Db.SaveChangesAsync();
        }
        public Task<AdditionalCostConsentView> Propose() => Consent.ProposeAsync(StaffId, Request.Id, new(50000, "Thay van"), default);
        public Task<ActiveResidentResidence?> FindActiveAsync(Guid userId, DateOnly date, CancellationToken ct) =>
            Task.FromResult<ActiveResidentResidence?>(new(userId == OwnerId ? ResidentId : Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
        public Task<string> GetAsync(CancellationToken ct) => Task.FromResult("Asia/Ho_Chi_Minh");
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
