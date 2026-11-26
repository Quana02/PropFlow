using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Text.Json;
using PropFlow.Shared.Messaging;
using PropFlow.Modules.ServiceRequests.Application.ReviewManagerServiceRequests;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.Modules.ServiceRequests.Infrastructure;

public sealed class ManagerServiceRequestStore(ServiceRequestsDbContext db, TimeProvider? clock = null) : IManagerServiceRequestStore
{
    public async Task<(ManagerRequestFilterOption[] Categories,Guid[] StaffIds)> FilterOptionsAsync(CancellationToken ct) =>
        (await db.ServiceRequestCategories.AsNoTracking().OrderBy(x=>x.Name).Select(x=>new ManagerRequestFilterOption(x.Id,x.Name)).Take(1001).ToArrayAsync(ct),
         await db.ServiceRequests.AsNoTracking().Where(x=>x.Assignments.Any(a=>a.Status==AssignmentStatus.ASSIGNED || a.Status==AssignmentStatus.IN_PROGRESS))
            .Select(x=>x.Assignments.Where(a=>a.Status==AssignmentStatus.ASSIGNED || a.Status==AssignmentStatus.IN_PROGRESS).OrderByDescending(a=>a.AssignedAt).Select(a=>a.StaffUserId).First()).Distinct().OrderBy(id=>id).Take(1001).ToArrayAsync(ct));
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;

    public Task<ManagerServiceRequestOutcome> AssignAsync(
        Guid id, Guid managerUserId, Guid staffUserId, string? note, DateTimeOffset now, CancellationToken ct) =>
        AssignAsync(id, managerUserId, staffUserId, 0, note, now, ct);

    public Task<ManagerServiceRequestOutcome> ApproveResolutionAsync(
        Guid id, Guid managerUserId, string? note, DateTimeOffset now, CancellationToken ct) =>
        ApproveResolutionAsync(id, managerUserId, 0, note, now, ct);

    public async Task<ManagerServiceRequestPage> ListAsync(ManagerServiceRequestListQuery query, CancellationToken ct)
    {
        var requests = db.ServiceRequests.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            requests = requests.Where(x => EF.Functions.ILike(x.RequestNumber, $"%{term}%") ||
                                           EF.Functions.ILike(x.Title, $"%{term}%"));
        }
        var statuses=(query.Statuses??[]).Select(Enum.Parse<ServiceRequestStatus>).ToList();
        if(query.Status=="ACTIVE")statuses.AddRange([ServiceRequestStatus.UNDER_REVIEW,ServiceRequestStatus.ASSIGNED,ServiceRequestStatus.IN_PROGRESS]);
        else if(Enum.TryParse<ServiceRequestStatus>(query.Status,out var legacyStatus))statuses.Add(legacyStatus);
        var statusSet=statuses.Distinct().ToArray();
        var priorities=(query.Priorities??[]).Concat(string.IsNullOrWhiteSpace(query.Priority)?[]:new[]{query.Priority}).Distinct().ToArray();
        if(statusSet.Length>0)requests=requests.Where(x=>statusSet.Contains(x.Status));
        if(priorities.Length>0)requests=requests.Where(x=>x.FinalPriorityCode!=null && priorities.Contains(x.FinalPriorityCode));
        if(query.CategoryIds is {Length:>0} categories)requests=requests.Where(x=>x.CategoryId.HasValue && categories.Contains(x.CategoryId.Value));
        if(query.AssignedStaffUserIds is {Length:>0} staffIds)requests=requests.Where(x=>staffIds.Contains(x.Assignments.Where(a=>a.Status==AssignmentStatus.ASSIGNED || a.Status==AssignmentStatus.IN_PROGRESS).OrderByDescending(a=>a.AssignedAt).Select(a=>a.StaffUserId).FirstOrDefault()));
        if(query.From is {} from)requests=requests.Where(x=>x.SubmittedAt>=from);
        if(query.To is {} to)requests=requests.Where(x=>x.SubmittedAt<=to);
        if(query.View=="ACTIVE")requests=requests.Where(x=>x.Status==ServiceRequestStatus.UNDER_REVIEW || x.Status==ServiceRequestStatus.ASSIGNED || x.Status==ServiceRequestStatus.IN_PROGRESS);
        else if(query.View=="RESOLVED")requests=requests.Where(x=>x.Status==ServiceRequestStatus.RESOLVED || x.Status==ServiceRequestStatus.CLOSED);
        else if(query.View=="SUBMITTED")requests=requests.Where(x=>x.Status==ServiceRequestStatus.SUBMITTED);
        if (query.UnassignedOnly)
            requests = requests.Where(x => !x.Assignments.Any(a => a.Status == AssignmentStatus.ASSIGNED || a.Status == AssignmentStatus.IN_PROGRESS));
        if (query.SubmittedWithinDays is { } days)
        {
            var submittedFrom = timeProvider.GetUtcNow().AddDays(-days);
            requests = requests.Where(x => x.SubmittedAt >= submittedFrom);
        }

        var totalCount = await requests.CountAsync(ct);
        var items = await requests
            .OrderByDescending(x => x.SubmittedAt)
            .ThenBy(x=>x.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new ManagerServiceRequestCore(
                x.Id, x.RequestNumber, x.Title, x.Description,
                x.Category == null ? null : x.Category.Code,
                x.Category == null ? null : x.Category.Name,
                x.ResidentId, x.ResidentApartmentId, x.ApartmentUnitId, x.FacilityId, x.EquipmentId,
                x.Status.ToString(), x.FinalPriorityCode, x.ServiceAreaCode, x.PreferredDate,
                x.PreferredTimeCode, x.SubmittedAt, x.UpdatedAt,
                x.Assignments.Where(a => a.Status == AssignmentStatus.ASSIGNED || a.Status == AssignmentStatus.IN_PROGRESS)
                    .OrderByDescending(a => a.AssignedAt).Select(a => (Guid?)a.StaffUserId).FirstOrDefault(),
                x.SubmittedByUserId, x.BaseServiceCost, x.ProposedAdditionalCost, x.AdditionalCostNote,
                x.ApprovedAdditionalCost, x.ServiceInvoiceId, x.AdditionalCostProposalId.HasValue ? new AdditionalCostConsentView(x.AdditionalCostProposalId.Value, x.ProposedAdditionalCost, x.AdditionalCostNote, x.AdditionalCostConsentStatus, x.AdditionalCostProposedAt, x.AdditionalCostRespondedAt) : null))
            .ToArrayAsync(ct);

        var summary = await requests.GroupBy(_ => 1).Select(group =>
            new ManagerServiceRequestSummary(
                group.Count(),
                group.Count(x => x.Status == ServiceRequestStatus.SUBMITTED),
                group.Count(x => x.Status == ServiceRequestStatus.UNDER_REVIEW || x.Status == ServiceRequestStatus.ASSIGNED || x.Status == ServiceRequestStatus.IN_PROGRESS),
                group.Count(x => x.Status == ServiceRequestStatus.RESOLVED || x.Status == ServiceRequestStatus.CLOSED)))
            .SingleOrDefaultAsync(ct) ?? new(0, 0, 0, 0);

        return new(items, totalCount, summary);
    }

    public Task<ManagerServiceRequestCore?> GetAsync(Guid id, CancellationToken ct) => db.ServiceRequests
        .AsNoTracking()
        .Where(x => x.Id == id)
        .Select(x => new ManagerServiceRequestCore(
            x.Id, x.RequestNumber, x.Title, x.Description,
            x.Category == null ? null : x.Category.Code,
            x.Category == null ? null : x.Category.Name,
            x.ResidentId, x.ResidentApartmentId, x.ApartmentUnitId, x.FacilityId, x.EquipmentId,
            x.Status.ToString(), x.FinalPriorityCode, x.ServiceAreaCode, x.PreferredDate,
            x.PreferredTimeCode, x.SubmittedAt, x.UpdatedAt,
            x.Assignments.Where(a => a.Status == AssignmentStatus.ASSIGNED || a.Status == AssignmentStatus.IN_PROGRESS)
                .OrderByDescending(a => a.AssignedAt).Select(a => (Guid?)a.StaffUserId).FirstOrDefault(),
            x.SubmittedByUserId, x.BaseServiceCost, x.ProposedAdditionalCost, x.AdditionalCostNote,
            x.ApprovedAdditionalCost, x.ServiceInvoiceId, x.AdditionalCostProposalId.HasValue ? new AdditionalCostConsentView(x.AdditionalCostProposalId.Value, x.ProposedAdditionalCost, x.AdditionalCostNote, x.AdditionalCostConsentStatus, x.AdditionalCostProposedAt, x.AdditionalCostRespondedAt) : null))
        .SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ManagerServiceRequestActivityItem>> GetActivitiesAsync(Guid id, CancellationToken ct) =>
        await db.ServiceRequestActivities.AsNoTracking()
            .Where(x => x.ServiceRequestId == id)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => new ManagerServiceRequestActivityItem(
                x.Id, x.ActivityType.ToString(), x.FromStatus == null ? null : x.FromStatus.ToString(),
                x.ToStatus == null ? null : x.ToStatus.ToString(), x.Title,
                x.ActivityType == ServiceActivityType.PROPERTY_CONTEXT_ASSOCIATED && x.Detail != null && x.Detail.StartsWith("ApartmentUnitId=")
                    ? "Đã cập nhật vị trí và tài sản liên quan." : x.Detail,
                x.WorkResult, x.PerformedBy, x.CreatedAt,
                x.Assignment != null && x.Assignment.Status == AssignmentStatus.IN_PROGRESS))
            .ToArrayAsync(ct);

    public async Task<IReadOnlyList<ManagerServiceRequestEvidenceMetadata>> GetEvidenceAsync(Guid id, CancellationToken ct) =>
        await db.ServiceRequestEvidenceFiles.AsNoTracking()
            .Where(x => x.ServiceRequestId == id)
            .OrderBy(x => x.UploadedAt)
            .Select(x => new ManagerServiceRequestEvidenceMetadata(
                x.Id, x.ServiceRequestId, x.ActivityId, x.FileName, x.ContentType,
                x.SizeBytes, x.StorageKey, x.UploadedAt))
            .ToArrayAsync(ct);

    public Task<ManagerServiceRequestEvidenceMetadata?> GetEvidenceFileAsync(Guid id, Guid evidenceId, CancellationToken ct) =>
        db.ServiceRequestEvidenceFiles.AsNoTracking()
            .Where(x => x.ServiceRequestId == id && x.Id == evidenceId)
            .Select(x => new ManagerServiceRequestEvidenceMetadata(
                x.Id, x.ServiceRequestId, x.ActivityId, x.FileName, x.ContentType,
                x.SizeBytes, x.StorageKey, x.UploadedAt))
            .SingleOrDefaultAsync(ct);

    public async Task<ManagerServiceRequestOutcome> ReviewAsync(
        Guid id, Guid managerUserId, string? note, DateTimeOffset now, CancellationToken ct)
    {
        var request = await db.ServiceRequests.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (request is null) return ManagerServiceRequestOutcome.NotFound;
        if (request.Status != ServiceRequestStatus.SUBMITTED) return ManagerServiceRequestOutcome.Conflict;

        request.MarkUnderReview(now);
        db.ServiceRequestActivities.Add(new ServiceRequestActivity(
            request.Id, ServiceActivityType.MANAGER_REVIEWED, now,
            fromStatus: ServiceRequestStatus.SUBMITTED,
            toStatus: ServiceRequestStatus.UNDER_REVIEW,
            title: "Ban Quản lý đã tiếp nhận yêu cầu",
            detail: note,
            performedBy: managerUserId));
        try
        {
            await db.SaveChangesAsync(ct);
            return ManagerServiceRequestOutcome.Success;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ManagerServiceRequestOutcome.Conflict;
        }
    }

    public async Task<ManagerServiceRequestOutcome> ConfirmPropertyContextAsync(
        Guid id, Guid managerUserId, Guid? apartmentUnitId, Guid? facilityId, Guid? equipmentId,
        string? serviceAreaCode, string? note, DateTimeOffset now, CancellationToken ct)
    {
        var request = await db.ServiceRequests.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (request is null) return ManagerServiceRequestOutcome.NotFound;
        if (request.Status != ServiceRequestStatus.SUBMITTED) return ManagerServiceRequestOutcome.Conflict;

        request.AssociatePropertyContext(apartmentUnitId, facilityId, equipmentId, now);
        if (!string.Equals(request.ServiceAreaCode, serviceAreaCode, StringComparison.OrdinalIgnoreCase))
            request.UpdateServiceArea(serviceAreaCode, now);
        request.MarkUnderReview(now);

        db.ServiceRequestActivities.Add(new ServiceRequestActivity(
            request.Id, ServiceActivityType.PROPERTY_CONTEXT_ASSOCIATED, now,
            title: "Đã gắn bối cảnh tài sản cho đơn dịch vụ",
            detail: "Đã cập nhật vị trí và tài sản liên quan.",
            performedBy: managerUserId));
        db.ServiceRequestActivities.Add(new ServiceRequestActivity(
            request.Id, ServiceActivityType.MANAGER_REVIEWED, now,
            fromStatus: ServiceRequestStatus.SUBMITTED,
            toStatus: ServiceRequestStatus.UNDER_REVIEW,
            title: "Ban Quản lý đã xác nhận đơn dịch vụ",
            detail: note,
            performedBy: managerUserId));
        try
        {
            await db.SaveChangesAsync(ct);
            return ManagerServiceRequestOutcome.Success;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ManagerServiceRequestOutcome.Conflict;
        }
    }

    public async Task<ManagerServiceRequestOutcome> AssignAsync(
        Guid id, Guid managerUserId, Guid staffUserId, decimal baseServiceCost, string? note, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.BeginWorkflowMutationAsync(id, ct);
        var request = await db.ServiceRequests
            .Include(x => x.Assignments)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        if (request is null) return ManagerServiceRequestOutcome.NotFound;
        if (request.Status is not (ServiceRequestStatus.UNDER_REVIEW or ServiceRequestStatus.ASSIGNED or ServiceRequestStatus.IN_PROGRESS))
            return ManagerServiceRequestOutcome.Conflict;
        if (baseServiceCost != 0)
            return ManagerServiceRequestOutcome.InvalidInput;

        var activeAssignment = request.Assignments
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.AssignedAt)
            .FirstOrDefault();
        if (activeAssignment?.StaffUserId == staffUserId)
            return ManagerServiceRequestOutcome.Conflict;

        var previousStatus = request.Status;
        var isReassignment = activeAssignment is not null;
        if (activeAssignment is not null)
            activeAssignment.EndAsReassigned(now);

        var assignment = new ServiceRequestAssignment(request.Id, staffUserId, managerUserId, now, note);
        db.ServiceRequestAssignments.Add(assignment);
        request.MarkAssigned(now);
        db.ServiceRequestActivities.Add(new ServiceRequestActivity(
            request.Id,
            isReassignment ? ServiceActivityType.REASSIGNED : ServiceActivityType.ASSIGNED,
            now,
            assignmentId: assignment.Id,
            fromStatus: previousStatus,
            toStatus: ServiceRequestStatus.ASSIGNED,
            title: isReassignment ? "Ban Quản lý đã đổi nhân sự phụ trách" : "Ban Quản lý đã phân công nhân sự",
            detail: note,
            performedBy: managerUserId));
        db.ResidentEvents.Add(new ServiceRequestWorkflowNotice(managerUserId, staffUserId, request.RequestNumber, assignment.Id)
            .ToEvent(ServiceRequestWorkflowNotice.Assigned, id, now));

        return await SaveAsync(ct, transaction);
    }

    public Task<ManagerServiceRequestOutcome> RequestReworkAsync(
        Guid id, Guid managerUserId, string? note, DateTimeOffset now, CancellationToken ct) =>
        ReviewResolutionAsync(id, managerUserId, 0, note, approve: false, now, ct);

    public Task<ManagerServiceRequestOutcome> ApproveResolutionAsync(
        Guid id, Guid managerUserId, decimal approvedAdditionalCost, string? note, DateTimeOffset now, CancellationToken ct) =>
        ReviewResolutionAsync(id, managerUserId, approvedAdditionalCost, note, approve: true, now, ct);

    public async Task<ManagerServiceRequestOutcome> CloseAsync(
        Guid id, Guid managerUserId, string? note, DateTimeOffset now, CancellationToken ct)
    {
        var request = await db.ServiceRequests.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (request is null) return ManagerServiceRequestOutcome.NotFound;
        if (request.Status != ServiceRequestStatus.RESOLVED) return ManagerServiceRequestOutcome.Conflict;

        request.Close(managerUserId, now);
        db.ServiceRequestActivities.Add(new ServiceRequestActivity(
            request.Id,
            ServiceActivityType.CLOSED,
            now,
            fromStatus: ServiceRequestStatus.RESOLVED,
            toStatus: ServiceRequestStatus.CLOSED,
            title: "Ban Quản lý đã đóng yêu cầu",
            detail: note,
            performedBy: managerUserId));

        return await SaveAsync(ct);
    }

    public async Task<ManagerServiceRequestOutcome> LinkInvoiceAsync(
        Guid id, Guid invoiceId, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.BeginWorkflowMutationAsync(id, ct);
        var request = await db.ServiceRequests.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (request is null) return ManagerServiceRequestOutcome.NotFound;
        if (request.ServiceInvoiceId == invoiceId) return ManagerServiceRequestOutcome.Success;
        try
        {
            request.LinkServiceInvoice(invoiceId, now);
            db.ResidentEvents.Add(new OutboxEvent("ServiceRequestInvoiceReady", id,
                JsonSerializer.Serialize(new ServiceRequestInvoiceNotice(request.ResidentId, request.RequestNumber, invoiceId)), now));
            return await SaveAsync(ct, transaction);
        }
        catch (InvalidOperationException)
        {
            return ManagerServiceRequestOutcome.Conflict;
        }
    }

    private async Task<ManagerServiceRequestOutcome> ReviewResolutionAsync(
        Guid id, Guid managerUserId, decimal approvedAdditionalCost, string? note, bool approve, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.BeginWorkflowMutationAsync(id, ct);
        var request = await db.ServiceRequests
            .Include(x => x.Assignments)
            .Include(x => x.Activities)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        if (request is null) return ManagerServiceRequestOutcome.NotFound;
        if (request.Status != ServiceRequestStatus.IN_PROGRESS || request.IsWaitingForCostConsent || request.ProposedAdditionalCost > 0 && request.AdditionalCostConsentStatus != "ACCEPTED") return ManagerServiceRequestOutcome.Conflict;
        var activeAssignment = request.Assignments.SingleOrDefault(x => x.Status == AssignmentStatus.IN_PROGRESS);
        if (activeAssignment is null) return ManagerServiceRequestOutcome.Conflict;

        var latestWorkResult = request.Activities
            .Where(x => x.AssignmentId == activeAssignment.Id && x.ActivityType == ServiceActivityType.WORK_RESULT_ADDED)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
        var latestRework = request.Activities
            .Where(x => x.AssignmentId == activeAssignment.Id && x.ActivityType == ServiceActivityType.REWORK_REQUESTED)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
        if (latestWorkResult is null ||
            (latestRework is not null && latestRework.CreatedAt >= latestWorkResult.CreatedAt))
            return ManagerServiceRequestOutcome.Conflict;
        if (!await db.ServiceRequestEvidenceFiles.AsNoTracking()
                .AnyAsync(x => x.ServiceRequestId == id && x.ActivityId == latestWorkResult.Id, ct))
            return ManagerServiceRequestOutcome.Conflict;

        if (approve)
        {
            request.ApproveAdditionalCost(approvedAdditionalCost, now);
            activeAssignment.Complete(now);
            request.Resolve(now);
            db.ServiceRequestActivities.Add(new ServiceRequestActivity(
                request.Id,
                ServiceActivityType.RESOLVED,
                now,
                assignmentId: activeAssignment.Id,
                fromStatus: ServiceRequestStatus.IN_PROGRESS,
                toStatus: ServiceRequestStatus.RESOLVED,
                title: "Ban Quản lý đã xác nhận kết quả xử lý",
                detail: note,
                performedBy: managerUserId));
        }
        else
        {
            request.MarkInProgress(now);
            db.ServiceRequestActivities.Add(new ServiceRequestActivity(
                request.Id,
                ServiceActivityType.REWORK_REQUESTED,
                now,
                assignmentId: activeAssignment.Id,
                fromStatus: ServiceRequestStatus.IN_PROGRESS,
                toStatus: ServiceRequestStatus.IN_PROGRESS,
                title: "Ban Quản lý yêu cầu Staff xử lý lại",
                detail: note,
                performedBy: managerUserId));
        }

        return await SaveAsync(ct, transaction);
    }

    private async Task<ManagerServiceRequestOutcome> SaveAsync(CancellationToken ct, IDbContextTransaction? transaction = null)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return ManagerServiceRequestOutcome.Success;
        }
        catch (DbUpdateException)
        {
            return ManagerServiceRequestOutcome.Conflict;
        }
    }
}
