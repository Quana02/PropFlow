using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.ServiceRequests.Application;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;
using PropFlow.Shared.Messaging;

namespace PropFlow.Modules.ServiceRequests.Infrastructure;

public sealed class ServiceRequestCostFailure(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public sealed record AdditionalCostNotice(Guid ResidentId, string RequestNumber, decimal Amount, string Note, Guid ProposalId);

public sealed class AdditionalCostConsentService(ServiceRequestsDbContext db, IResidentResidenceSource residences,
    ICurrentBuildingTimeZone buildingZone, TimeProvider clock, IServiceRequestRealtimeNotifier? realtime = null)
{
    public async Task<AdditionalCostConsentView> ProposeAsync(Guid staffId, Guid id, ProposeAdditionalCostRequest input, CancellationToken ct)
    {
        if (input.Amount <= 0 || input.Amount >= 1000000000000000000m || input.Amount != decimal.Round(input.Amount) ||
            string.IsNullOrWhiteSpace(input.Note) || input.Note.Trim().Length > 1000)
            throw new ServiceRequestCostFailure(400, "invalid_additional_cost", "Nhập số tiền VND nguyên dương và lý do phát sinh tối đa 1000 ký tự.");
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        await LockAsync(id, ct);
        var request = await db.ServiceRequests.Include(x => x.Assignments).Include(x => x.Activities)
            .SingleOrDefaultAsync(x => x.Id == id && x.Assignments.Any(a => a.StaffUserId == staffId && (a.Status == PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments.AssignmentStatus.ASSIGNED || a.Status == PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments.AssignmentStatus.IN_PROGRESS)), ct);
        if (request is null) throw Missing();
        if (ServiceRequestAreaCodes.IsCommonArea(request.ServiceAreaCode))
            throw new ServiceRequestCostFailure(400, "common_area_not_chargeable", "Công việc tại khu vực chung của chung cư không thu phí cư dân.");
        var assignment = request.Assignments.SingleOrDefault(a => a.StaffUserId == staffId && a.IsActive);
        if (assignment is null) throw Missing();
        var result = request.Activities.Where(x => x.ActivityType == ServiceActivityType.WORK_RESULT_ADDED).MaxBy(x => x.CreatedAt);
        var rework = request.Activities.Where(x => x.ActivityType == ServiceActivityType.REWORK_REQUESTED).MaxBy(x => x.CreatedAt);
        if (request.Status != ServiceRequestStatus.IN_PROGRESS || assignment.Status != AssignmentStatus.IN_PROGRESS || request.AdditionalCostProposalId != input.ExpectedProposalId ||
            result is not null && (rework is null || result.CreatedAt > rework.CreatedAt)) throw Conflict();
        if (request.BaseServiceCost + input.Amount >= 1000000000000000000m)
            throw new ServiceRequestCostFailure(400, "service_cost_too_large", "Tổng chi phí vượt giới hạn cho phép.");
        var now = clock.GetUtcNow();
        try { request.ProposeAdditionalCost(input.Amount, input.Note, now); }
        catch (InvalidOperationException) { throw Conflict(); }
        db.ServiceRequestActivities.Add(new(id, ServiceActivityType.ADDITIONAL_COST_PROPOSED, now, assignment.Id,
            title: "Staff đề nghị cư dân xác nhận chi phí phát sinh", detail: input.Note.Trim(), performedBy: staffId));
        db.ResidentEvents.Add(new OutboxEvent("ServiceRequestAdditionalCostProposed", id,
            JsonSerializer.Serialize(new AdditionalCostNotice(request.ResidentId, request.RequestNumber, input.Amount,
                input.Note.Trim(), request.AdditionalCostProposalId!.Value)), now));
        await SaveAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        if (realtime is not null) await realtime.NotifyChangedAsync(id, ct: ct);
        return View(request)!;
    }

    public async Task<AdditionalCostConsentView> RespondAsync(Guid userId, Guid id, RespondAdditionalCostRequest input, CancellationToken ct)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(await buildingZone.GetAsync(ct));
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        var residence = await residences.FindActiveAsync(userId, today, ct);
        if (residence is null) throw new ServiceRequestCostFailure(403, "active_residence_not_found", "Tài khoản chưa được liên kết với căn hộ đang cư trú.");
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        await LockAsync(id, ct);
        var request = await db.ServiceRequests.Include(x => x.Assignments)
            .SingleOrDefaultAsync(x => x.Id == id && x.ResidentId == residence.ResidentId, ct);
        if (request is null) throw Missing();
        if (request.AdditionalCostProposalId == input.ProposalId && request.AdditionalCostRespondedBy == userId &&
            request.AdditionalCostConsentStatus == (input.Accepted ? "ACCEPTED" : "REJECTED")) return View(request)!;
        var now = clock.GetUtcNow();
        try { request.RespondToAdditionalCost(input.ProposalId, userId, input.Accepted, now); }
        catch (InvalidOperationException) { throw Conflict(); }
        if (!input.Accepted)
            foreach (var assignment in request.Assignments.Where(x => x.IsActive)) assignment.Cancel(now);
        db.ServiceRequestActivities.Add(new(id, input.Accepted ? ServiceActivityType.ADDITIONAL_COST_ACCEPTED : ServiceActivityType.ADDITIONAL_COST_REJECTED,
            now, fromStatus: input.Accepted ? null : ServiceRequestStatus.IN_PROGRESS,
            toStatus: input.Accepted ? null : ServiceRequestStatus.CANCELLED,
            title: input.Accepted ? "Cư dân đã chấp nhận chi phí phát sinh" : "Yêu cầu tự động bị từ chối do cư dân không chấp nhận chi phí phát sinh",
            performedBy: userId));
        await SaveAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        if (realtime is not null) await realtime.NotifyChangedAsync(id, userId, ct);
        return View(request)!;
    }

    public static AdditionalCostConsentView? View(ServiceRequest request) => request.AdditionalCostProposalId is { } id
        ? new(id, request.ProposedAdditionalCost, request.AdditionalCostNote, request.AdditionalCostConsentStatus,
            request.AdditionalCostProposedAt, request.AdditionalCostRespondedAt) : null;
    private Task LockAsync(Guid id, CancellationToken ct) => db.Database.IsNpgsql()
        ? db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({id.ToString()}, 5))", ct) : Task.CompletedTask;
    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw Conflict(); }
    }
    private static ServiceRequestCostFailure Missing() => new(404, "service_request_not_found", "Không tìm thấy yêu cầu thuộc phạm vi của bạn.");
    private static ServiceRequestCostFailure Conflict() => new(409, "additional_cost_changed", "Yêu cầu hoặc đề xuất chi phí đã thay đổi. Vui lòng tải lại để kiểm tra.");
}
