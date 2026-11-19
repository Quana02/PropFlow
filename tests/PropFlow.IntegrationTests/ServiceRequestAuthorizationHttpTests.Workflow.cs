using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Billing.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Domain.Payments;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Domain.Equipment;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
using PropFlow.Api.Composition;

namespace PropFlow.IntegrationTests;

public sealed partial class ServiceRequestAuthorizationHttpTests
{
    private static readonly UpdateStaffServiceRequestProgressRequest Progress = new("Đã kiểm tra đường ống");
    private static readonly CompleteStaffServiceRequestWorkRequest Work = new("Đã sửa đường ống và kiểm tra rò rỉ.");
    private static readonly ReviewManagerServiceRequestResolutionRequest Resolution = new("Kiểm tra kết quả tại căn hộ");
    private static readonly CloseManagerServiceRequestRequest Closure = new("Đóng yêu cầu sau nghiệm thu");
    private static readonly RateResidentServiceRequestRequest Feedback = new(5, "Đã xử lý đúng yêu cầu.");

    [Fact]
    public async Task Manager_review_and_property_context_preserve_the_reporter_and_require_submitted_state()
    {
        var review = await Seed();
        var context = await Seed(secondHome: true);
        var facility = new Facility("B3-FACILITY", "Khu vực thử nghiệm", Now);
        var unrelatedFacility = new Facility("B3-OTHER", "Khu vực khác", Now);
        var equipment = new Equipment("B3-EQUIPMENT", "Thiết bị thử nghiệm", Now, facilityId: facility.Id);
        using (var scope = host.Services.CreateScope())
        {
            var assets = scope.ServiceProvider.GetRequiredService<PropertyAssetsDbContext>();
            assets.AddRange(facility, unrelatedFacility, equipment);
            await assets.SaveChangesAsync();
        }
        var note = new ReviewManagerServiceRequestRequest("Tiếp nhận yêu cầu");
        var link = new ConfirmManagerServiceRequestPropertyContextRequest(homeB.ApartmentId, facility.Id, equipment.Id, "IN_APARTMENT");
        await Denied("MANAGER", review.Id, "review", note);
        await Denied("MANAGER", context.Id, "property-context", link);
        await Reject(manager, context.Id, "property-context", HttpStatusCode.BadRequest, link with { EquipmentId = Guid.NewGuid() });
        await Reject(manager, context.Id, "property-context", HttpStatusCode.BadRequest, link with { ApartmentUnitId = Guid.NewGuid() });
        await Reject(manager, context.Id, "property-context", HttpStatusCode.BadRequest, link with { FacilityId = unrelatedFacility.Id });
        await Reject(manager, Guid.NewGuid(), "review", HttpStatusCode.NotFound, note);
        await Reject(manager, Guid.NewGuid(), "property-context", HttpStatusCode.NotFound, link);
        await Call(manager, review.Id, "review", note);
        await Call(manager, context.Id, "property-context", new
        {
            link.ApartmentUnitId, link.FacilityId, link.EquipmentId, link.ServiceAreaCode,
            ResidentId = homeA.ResidentId, SubmittedByUserId = residentA.Id
        });
        var reviewed = await Read(review.Id);
        var linked = await Read(context.Id);
        Assert.Equal(ServiceRequestStatus.UNDER_REVIEW, reviewed.Status);
        Assert.Equal(ServiceRequestStatus.UNDER_REVIEW, linked.Status);
        Assert.Equal(homeB.ResidentId, linked.ResidentId);
        Assert.Equal(homeB.ResidencyId, linked.ResidentApartmentId);
        Assert.Equal(residentB.Id, linked.SubmittedByUserId);
        Assert.Equal(homeB.ApartmentId, linked.ApartmentUnitId);
        Assert.Equal(facility.Id, linked.FacilityId);
        Assert.Equal(equipment.Id, linked.EquipmentId);
        Assert.Equal(manager.Id, Assert.Single(reviewed.Activities).PerformedBy);
        Assert.Contains(linked.Activities, row => row.ActivityType == ServiceActivityType.PROPERTY_CONTEXT_ASSOCIATED && row.PerformedBy == manager.Id);
        await Reject(manager, review.Id, "review", HttpStatusCode.Conflict, note);
        await Reject(manager, context.Id, "property-context", HttpStatusCode.Conflict, link);
    }

    [Fact]
    public async Task Manager_assignment_and_staff_accept_progress_require_an_active_assignee()
    {
        var request = await Seed(reviewed: true);
        var notReviewed = await Seed();
        var assignment = new AssignManagerServiceRequestRequest(staffA.Id, "Kiểm tra tại căn hộ");
        await Denied("MANAGER", request.Id, "assignment", assignment);
        await Reject(manager, notReviewed.Id, "assignment", HttpStatusCode.Conflict, assignment);
        await Reject(manager, request.Id, "assignment", HttpStatusCode.BadRequest, assignment with { StaffUserId = residentB.Id });
        await Reject(manager, request.Id, "assignment", HttpStatusCode.BadRequest, assignment with { StaffUserId = Guid.NewGuid() });
        await Reject(manager, request.Id, "assignment", HttpStatusCode.BadRequest, assignment with { BaseServiceCost = 50000 });
        var disabled = await Login("STAFF");
        var grantLost = await Login("STAFF");
        var grantRequest = await Seed(reviewed: true);
        await Call(manager, grantRequest.Id, "assignment", assignment with { StaffUserId = grantLost.Id });
        await Call(grantLost, grantRequest.Id, "accept");
        using (var scope = host.Services.CreateScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
            var user = await auth.UserAccounts.SingleAsync(row => row.Id == disabled.Id);
            user.Disable(manager.Id, Now);
            await auth.SaveChangesAsync();
            var access = scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
            await access.UserRoleAssignments.Where(row => row.UserId == grantLost.Id).ExecuteDeleteAsync();
        }
        await Reject(manager, request.Id, "assignment", HttpStatusCode.BadRequest, assignment with { StaffUserId = disabled.Id });
        await Reject(manager, request.Id, "assignment", HttpStatusCode.BadRequest, assignment with { StaffUserId = grantLost.Id });
        await Reject(grantLost, grantRequest.Id, "progress", HttpStatusCode.Unauthorized, Progress);
        await Reject(grantLost, grantRequest.Id, "complete", HttpStatusCode.Unauthorized, Work);
        await Call(manager, request.Id, "assignment", assignment);
        await Reject(manager, request.Id, "assignment", HttpStatusCode.Conflict, assignment);
        await Call(manager, request.Id, "assignment", assignment with { StaffUserId = staffB.Id });
        await Denied("STAFF", request.Id, "accept");
        await Reject(staffA, request.Id, "accept", HttpStatusCode.NotFound);
        await Reject(unrelatedStaff, request.Id, "accept", HttpStatusCode.NotFound);
        await Reject(staffB, request.Id, "progress", HttpStatusCode.Conflict, Progress);
        await Call(manager, request.Id, "assignment", assignment);
        await Reject(staffB, request.Id, "accept", HttpStatusCode.NotFound);
        await Call(staffA, request.Id, "accept");
        await Reject(staffA, request.Id, "accept", HttpStatusCode.Conflict);
        await Denied("STAFF", request.Id, "progress", Progress);
        await Reject(staffB, request.Id, "progress", HttpStatusCode.NotFound, Progress);
        await Reject(unrelatedStaff, request.Id, "progress", HttpStatusCode.NotFound, Progress);
        await Reject(staffA, request.Id, "progress", HttpStatusCode.BadRequest, new { Detail = "x" });
        await Call(staffA, request.Id, "progress", new { Progress.Detail, StaffUserId = staffB.Id, Status = "RESOLVED" });
        var state = await Read(request.Id);
        Assert.Equal(ServiceRequestStatus.IN_PROGRESS, state.Status);
        Assert.Equal(3, state.Assignments.Count);
        var current = Assert.Single(state.Assignments, row => row.IsActive);
        Assert.Equal(staffA.Id, current.StaffUserId);
        var progress = Assert.Single(state.Activities, row => row.ActivityType == ServiceActivityType.PROGRESS_UPDATED);
        Assert.Equal(current.Id, progress.AssignmentId);
        Assert.Equal(staffA.Id, progress.PerformedBy);
        using var finalScope = host.Services.CreateScope();
        var events = await finalScope.ServiceProvider.GetRequiredService<ServiceRequestsDbContext>().ResidentEvents
            .Where(row => row.SourceId == request.Id).ToListAsync();
        Assert.Equal(3, events.Count);
        Assert.Contains(events, row => row.Payload.Contains(current.Id.ToString(), StringComparison.OrdinalIgnoreCase));
        await AssertNoPaymentOrInvoice();
    }

    [Fact]
    public async Task Staff_completion_evidence_and_manager_rework_keep_history_private()
    {
        var id = await Start();
        await Denied("STAFF", id, "complete", Work);
        await Reject(staffA, id, "complete", HttpStatusCode.NotFound, Work);
        await Reject(unrelatedStaff, id, "complete", HttpStatusCode.NotFound, Work);
        await Reject(staffB, id, "complete", HttpStatusCode.BadRequest, new { WorkResult = "x" });
        await Reject(staffB, id, "complete", HttpStatusCode.Conflict, Work with { AdditionalCost = 2000, AdditionalCostNote = "Chưa có đồng ý" });
        await Reject(staffB, id, "evidence", HttpStatusCode.Conflict);
        await Call(staffB, id, "complete", Work);
        await Reject(staffB, id, "complete", HttpStatusCode.Conflict, Work);
        await Reject(manager, id, "resolution/rework", HttpStatusCode.Conflict, Resolution);
        await Denied("STAFF", id, "evidence");
        await Reject(staffA, id, "evidence", HttpStatusCode.Conflict);
        await Reject(unrelatedStaff, id, "evidence", HttpStatusCode.Conflict);
        await Call(staffB, id, "evidence");
        Assert.Single(storage.Uploads);
        Assert.Empty(storage.Deletes);
        await Reject(staffB, id, "evidence", HttpStatusCode.Conflict);
        var evidenceId = await EvidenceId(id, staffB.Id);
        await AssertPrivateEvidence(id, evidenceId, staffB, HttpStatusCode.OK);
        await AssertPrivateEvidence(id, evidenceId, staffA, HttpStatusCode.NotFound);
        await AssertPrivateEvidence(Guid.NewGuid(), evidenceId, staffB, HttpStatusCode.NotFound);
        await Denied("MANAGER", id, "resolution/rework", Resolution);
        await Call(manager, id, "resolution/rework", Resolution);
        await Reject(manager, id, "resolution/rework", HttpStatusCode.Conflict, Resolution);
        var state = await Read(id);
        Assert.Equal(ServiceRequestStatus.IN_PROGRESS, state.Status);
        Assert.Equal(staffB.Id, Assert.Single(state.Assignments, row => row.IsActive).StaffUserId);
        Assert.Equal(manager.Id, Assert.Single(state.Activities, row => row.ActivityType == ServiceActivityType.REWORK_REQUESTED).PerformedBy);
        await Call(manager, id, "assignment", new AssignManagerServiceRequestRequest(staffA.Id));
        await AssertPrivateEvidence(id, evidenceId, staffB, HttpStatusCode.NotFound);
        await AssertPrivateEvidence(id, evidenceId, manager, HttpStatusCode.OK);
        Assert.Equal(evidenceId, await EvidenceId(id, staffB.Id));
        await AssertNoPaymentOrInvoice();
    }

    [Fact]
    public async Task Former_assignment_result_cannot_be_uploaded_or_approved_after_reassignment()
    {
        var id = await Start(staffA);
        await Call(staffA, id, "complete", Work);
        await Call(manager, id, "assignment", new AssignManagerServiceRequestRequest(staffB.Id));
        await Reject(staffA, id, "evidence", HttpStatusCode.Conflict);
        await Call(manager, id, "assignment", new AssignManagerServiceRequestRequest(staffA.Id));
        var reassigned = await Call(staffA, id, "accept");
        var history = reassigned.GetProperty("activities").EnumerateArray()
            .Single(row => row.GetProperty("activityType").GetString() == "WORK_RESULT_ADDED");
        Assert.False(history.GetProperty("isFromCurrentAssignment").GetBoolean());
        await Reject(staffA, id, "evidence", HttpStatusCode.Conflict);
        Assert.Empty(storage.Uploads);
        await Call(staffA, id, "complete", Work);
        await Call(staffA, id, "evidence");
        var evidenceId = await EvidenceId(id, staffA.Id);

        await Call(manager, id, "assignment", new AssignManagerServiceRequestRequest(staffB.Id));
        await Call(staffB, id, "accept");
        await Reject(manager, id, "resolution/approve", HttpStatusCode.Conflict, Resolution);
        await Reject(manager, id, "resolution/rework", HttpStatusCode.Conflict, Resolution);
        await AssertPrivateEvidence(id, evidenceId, manager, HttpStatusCode.OK);
        await Call(staffB, id, "complete", Work);
        await AssertNoPaymentOrInvoice();
    }

    [Fact]
    public async Task Evidence_upload_compensates_if_reassigned_while_storage_is_in_flight()
    {
        var id = await Start(staffA);
        await Call(staffA, id, "complete", Work);
        storage.BeforeReturn = async () =>
        {
            await Call(manager, id, "assignment", new AssignManagerServiceRequestRequest(staffB.Id));
        };
        await Expect(staffA.Client, "staff", id, "evidence", null, HttpStatusCode.NotFound);
        Assert.Equal(Assert.Single(storage.Uploads), Assert.Single(storage.Deletes));
        using var scope = host.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ServiceRequestsDbContext>().ServiceRequestEvidenceFiles.ToListAsync());
        Assert.Equal(1, (await Read(id)).Activities.Count(row => row.ActivityType == ServiceActivityType.WORK_RESULT_ADDED));
    }

    [Fact]
    public async Task Manager_resolution_and_close_issue_one_invoice_for_the_consented_amount_and_owner()
    {
        var id = await Start();
        var propose = new ProposeAdditionalCostRequest(30_000, "Vật tư thay thế");
        var proposal = await Call(staffB, id, "additional-cost", propose);
        await Call(manager, id, "additional-cost/approve", new ApproveAdditionalCostProposalRequest(proposal.GetProperty("proposalId").GetGuid()));
        await Call(residentA, id, "additional-cost/response", new RespondAdditionalCostRequest(proposal.GetProperty("proposalId").GetGuid(), true));
        var finish = Work with { AdditionalCost = 30_000, AdditionalCostNote = propose.Note };
        await Call(staffB, id, "complete", finish);
        await Reject(manager, id, "resolution/approve", HttpStatusCode.Conflict, Resolution);
        await Reject(manager, id, "close", HttpStatusCode.Conflict, Closure);
        await Call(staffB, id, "evidence");
        await Denied("MANAGER", id, "resolution/approve", Resolution);
        await Reject(manager, id, "resolution/approve", HttpStatusCode.BadRequest, Resolution with { ApprovedAdditionalCost = 30_001 });
        await AssertNoPaymentOrInvoice();
        var approve = new
        {
            Resolution.Note, ApprovedAdditionalCost = 25_000, ReviewerId = unrelatedStaff.Id, Status = "CLOSED", BaseServiceCost = 1
        };
        var approvals = await Task.WhenAll(
            Send(manager.Client, "manager", id, "resolution/approve", approve),
            Send(manager.Client, "manager", id, "resolution/approve", approve));
        try
        {
            Assert.Contains(approvals, response => response.StatusCode == HttpStatusCode.OK);
            Assert.All(approvals, response => Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        }
        finally
        {
            foreach (var response in approvals) response.Dispose();
        }
        await Reject(manager, id, "resolution/approve", HttpStatusCode.Conflict, Resolution);
        await Denied("MANAGER", id, "close", Closure);
        await Call(manager, id, "close", Closure);
        await Reject(manager, id, "close", HttpStatusCode.Conflict, Closure);
        await Reject(residentA, id, "feedback", HttpStatusCode.Conflict, Feedback);
        using var scope = host.Services.CreateScope();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var invoice = Assert.Single(await billing.Invoices.ToListAsync());
        var recipient = Assert.Single(await billing.InvoiceRecipients.ToListAsync());
        var state = await Read(id);
        Assert.Equal(invoice.Id, state.ServiceInvoiceId);
        Assert.Equal(25_000m, invoice.TotalAmount);
        Assert.Equal(homeA.ApartmentId, invoice.ApartmentUnitId);
        Assert.Equal(homeA.ResidentId, recipient.ResidentId);
        Assert.Equal(residentA.Id, recipient.UserId);
        Assert.Equal(ServiceRequestStatus.CLOSED, state.Status);
        Assert.Equal(manager.Id, state.ClosedBy);
        Assert.Equal(manager.Id, Assert.Single(state.Activities, row => row.ActivityType == ServiceActivityType.RESOLVED).PerformedBy);
        await scope.ServiceProvider.GetRequiredService<ServiceRequestCostNoticeDispatcher>().DispatchAsync(default);
        await scope.ServiceProvider.GetRequiredService<ServiceRequestCostNoticeDispatcher>().DispatchAsync(default);
        var communication = scope.ServiceProvider.GetRequiredService<CommunicationDbContext>();
        var billNotice = Assert.Single(await communication.Notifications.Where(x => x.SourceId == id && x.Title == "Hóa đơn dịch vụ đã sẵn sàng").ToArrayAsync());
        Assert.Equal(residentA.Id, billNotice.RecipientUserId);
        var payments = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        Assert.Empty(await payments.Payments.ToListAsync());
        var payment = new Payment("B3-PAID-FIXTURE", invoice.Id, invoice.TotalAmount, "CASH", Now, Now);
        payment.Confirm(accountant.Id, invoice.TotalAmount, Now);
        payments.Payments.Add(payment);
        await payments.SaveChangesAsync();
        await Call(residentA, id, "feedback", Feedback);
        Assert.Single(await payments.Payments.ToListAsync());
    }

    [Fact]
    public async Task Additional_cost_replacement_and_resident_decision_use_current_assignment_and_owner()
    {
        var id = await Start();
        var propose = new ProposeAdditionalCostRequest(30_000, "Vật tư thay thế");
        await Denied("STAFF", id, "additional-cost", propose);
        await Reject(staffA, id, "additional-cost", HttpStatusCode.NotFound, propose);
        await Reject(unrelatedStaff, id, "additional-cost", HttpStatusCode.NotFound, propose);
        await Reject(staffB, id, "additional-cost", HttpStatusCode.BadRequest, propose with { Amount = -1 });
        var first = (await Call(staffB, id, "additional-cost", propose)).GetProperty("proposalId").GetGuid();
        var pending = await Read(id);
        Assert.Equal("AWAITING_MANAGER", pending.AdditionalCostConsentStatus);
        Assert.Null(pending.AdditionalCostRespondedBy);
        await Reject(staffB, id, "additional-cost", HttpStatusCode.Conflict, propose);
        await Reject(staffB, id, "progress", HttpStatusCode.Conflict, Progress);
        await Reject(staffB, id, "complete", HttpStatusCode.Conflict, Work);
        await Reject(staffB, id, "evidence", HttpStatusCode.Conflict);
        var replacement = propose with { ExpectedProposalId = first, Amount = 35_000 };
        var current = (await Call(staffB, id, "additional-cost", replacement)).GetProperty("proposalId").GetGuid();
        Assert.NotEqual(first, current);
        var decision = new RespondAdditionalCostRequest(current, true);
        await Reject(residentA, id, "additional-cost/response", HttpStatusCode.Conflict, decision);
        await Reject(manager, id, "additional-cost/approve", HttpStatusCode.Conflict, new ApproveAdditionalCostProposalRequest(first));
        await Denied("MANAGER", id, "additional-cost/approve", new ApproveAdditionalCostProposalRequest(current));
        await Call(manager, id, "additional-cost/approve", new ApproveAdditionalCostProposalRequest(current));
        await Call(manager, id, "additional-cost/approve", new ApproveAdditionalCostProposalRequest(current));
        await Denied("RESIDENT", id, "additional-cost/response", decision);
        await Reject(residentB, id, "additional-cost/response", HttpStatusCode.NotFound, decision);
        await Reject(residentA, id, "additional-cost/response", HttpStatusCode.Conflict, decision with { ProposalId = first });
        await Reject(residentA, id, "additional-cost/response", HttpStatusCode.Conflict, decision with { ProposalId = Guid.NewGuid() });
        await Call(residentA, id, "additional-cost/response", new
        {
            decision.ProposalId, decision.Accepted, Amount = 1, Status = "REJECTED", ResidentId = homeB.ResidentId
        });
        var accepted = await Read(id);
        Assert.Equal(35_000m, accepted.ProposedAdditionalCost);
        Assert.Equal("ACCEPTED", accepted.AdditionalCostConsentStatus);
        Assert.Equal(residentA.Id, accepted.AdditionalCostRespondedBy);
        Assert.Single(accepted.Activities, row => row.ActivityType == ServiceActivityType.ADDITIONAL_COST_ACCEPTED);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceRequestsDbContext>();
            var costEvents = await db.ResidentEvents.Where(row => row.SourceId == id && row.Name == "ServiceRequestAdditionalCostProposed").ToListAsync();
            Assert.Single(costEvents);
            Assert.Contains(costEvents, row => row.Payload.Contains(current.ToString(), StringComparison.OrdinalIgnoreCase) &&
                row.Payload.Contains(homeA.ResidentId.ToString(), StringComparison.OrdinalIgnoreCase));
        }
        var after = await Snapshot();
        await Call(residentA, id, "additional-cost/response", decision);
        Assert.Equal(after, await Snapshot());
        await Reject(residentA, id, "additional-cost/response", HttpStatusCode.Conflict, decision with { Accepted = false });
        await AssertNoPaymentOrInvoice();
    }

    [Fact]
    public async Task Only_the_eligible_owner_can_create_and_update_feedback()
    {
        var id = await Start();
        await Denied("RESIDENT", id, "feedback", Feedback);
        await Reject(residentB, id, "feedback", HttpStatusCode.NotFound, Feedback);
        await Reject(residentA, id, "feedback", HttpStatusCode.Conflict, new
        {
            Feedback.Rating, Feedback.Comment, Status = "RESOLVED", OutstandingAmount = 0
        });
        await Call(staffB, id, "complete", Work);
        await Call(staffB, id, "evidence");
        await Call(manager, id, "resolution/approve", Resolution);
        await Reject(residentA, id, "feedback", HttpStatusCode.BadRequest, Feedback with { Rating = 0 });
        await Reject(residentA, id, "feedback", HttpStatusCode.BadRequest, Feedback with { Comment = new string('x', 5000) });
        await Call(residentA, id, "feedback", new
        {
            Feedback.Rating, Feedback.Comment, ResidentId = homeB.ResidentId, UserId = residentB.Id
        });
        await Reject(residentB, id, "feedback", HttpStatusCode.NotFound, Feedback);
        var updated = Feedback with { Rating = 4, Comment = "Đã sửa xong và gọi lại." };
        await Call(residentA, id, "feedback", updated);
        await Call(residentA, id, "feedback", updated);
        using var scope = host.Services.CreateScope();
        var row = Assert.Single(await scope.ServiceProvider.GetRequiredService<ServiceRequestsDbContext>().ServiceRequestFeedbacks.ToListAsync());
        Assert.Equal(id, row.ServiceRequestId);
        Assert.Equal(homeA.ResidentId, row.ResidentId);
        Assert.Equal(4, row.Rating);
        Assert.Equal(updated.Comment, row.Comment);
        Assert.Equal(ServiceRequestStatus.RESOLVED, (await Read(id)).Status);
        await AssertNoPaymentOrInvoice();
    }

    private async Task<Guid> Start(Actor? assignee = null)
    {
        var id = (await Seed(reviewed: true)).Id;
        await Call(manager, id, "assignment", new AssignManagerServiceRequestRequest(staffA.Id));
        if (assignee != staffA)
            await Call(manager, id, "assignment", new AssignManagerServiceRequestRequest(staffB.Id));
        await Call(assignee ?? staffB, id, "accept");
        return id;
    }

    private async Task<Guid> EvidenceId(Guid id, Guid uploader)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceRequestsDbContext>();
        var evidence = Assert.Single(await db.ServiceRequestEvidenceFiles.Where(row => row.ServiceRequestId == id).ToListAsync());
        Assert.Equal(uploader, evidence.UploadedBy);
        var result = await db.ServiceRequestActivities.SingleAsync(row => row.Id == evidence.ActivityId);
        Assert.Equal(ServiceActivityType.WORK_RESULT_ADDED, result.ActivityType);
        Assert.Equal(uploader, result.PerformedBy);
        return evidence.Id;
    }

    private static async Task AssertPrivateEvidence(Guid id, Guid evidenceId, Actor actor, HttpStatusCode status)
    {
        using var response = await actor.Client.GetAsync(Route(actor.Role.ToLowerInvariant(), id, $"evidence/{evidenceId}"));
        Assert.Equal(status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        if (status == HttpStatusCode.OK)
            Assert.Equal(EvidenceStorage.Image, await response.Content.ReadAsByteArrayAsync());
    }

    private async Task AssertNoPaymentOrInvoice()
    {
        using var scope = host.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Invoices.ToListAsync());
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Payments.ToListAsync());
    }

    [Fact]
    public async Task Concurrent_evidence_uploads_keep_one_batch_for_the_current_result()
    {
        var id = await Start();
        await Call(staffB, id, "complete", Work);
        var uploadsInFlight = 0;
        var bothUploaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.BeforeReturn = async () =>
        {
            if (Interlocked.Increment(ref uploadsInFlight) == 2) bothUploaded.TrySetResult();
            await bothUploaded.Task.WaitAsync(TimeSpan.FromSeconds(15));
        };
        var responses = await Task.WhenAll(
            Send(staffB.Client, "staff", id, "evidence", null),
            Send(staffB.Client, "staff", id, "evidence", null));
        try
        {
            Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, responses.Select(response => response.StatusCode).Order());
            Assert.Equal(2, storage.Uploads.Count);
            Assert.Single(storage.Deletes);
            await EvidenceId(id, staffB.Id);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task Progress_completion_and_evidence_wait_for_earlier_reassignment_then_deny_former_staff()
    {
        var id = await Start(staffA);
        await Call(staffA, id, "complete", Work);
        using var blockingScope = host.Services.CreateScope();
        using var observingScope = host.Services.CreateScope();
        var blocker = blockingScope.ServiceProvider.GetRequiredService<ServiceRequestsDbContext>();
        var observer = observingScope.ServiceProvider.GetRequiredService<ServiceRequestsDbContext>();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({id.ToString()}, 5))");
        var reassignment = Send(manager.Client, "manager", id, "assignment", new AssignManagerServiceRequestRequest(staffB.Id));
        await WaitForMutations(observer, 1);
        var progress = Send(staffA.Client, "staff", id, "progress", Progress);
        var completion = Send(staffA.Client, "staff", id, "complete", Work);
        var evidence = Send(staffA.Client, "staff", id, "evidence", null);
        await WaitForMutations(observer, 4);
        await transaction.CommitAsync();
        using var assigned = await reassignment;
        using var progressResult = await progress;
        using var completionResult = await completion;
        using var evidenceResult = await evidence;
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, progressResult.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, completionResult.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, evidenceResult.StatusCode);
        var state = await Read(id);
        Assert.Equal(staffB.Id, Assert.Single(state.Assignments, row => row.IsActive).StaffUserId);
        Assert.DoesNotContain(state.Activities, row => row.ActivityType == ServiceActivityType.PROGRESS_UPDATED);
        Assert.Single(state.Activities, row => row.ActivityType == ServiceActivityType.WORK_RESULT_ADDED);
        Assert.Empty(storage.Uploads);
        Assert.Empty(storage.Deletes);
        await AssertNoPaymentOrInvoice();
    }

    private static async Task WaitForMutations(ServiceRequestsDbContext observer, int expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await observer.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM pg_stat_activity
            WHERE datname = current_database() AND wait_event = 'advisory'
            """).SingleAsync(timeout.Token) < expected)
            await Task.Delay(25, timeout.Token);
    }
}
