using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Complaints.Domain.Complaints;
using PropFlow.Modules.Complaints.Domain.ComplaintActivities;
using PropFlow.Modules.Complaints.Domain.ComplaintFollowups;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;

namespace PropFlow.Modules.Complaints.Application;

public sealed class ComplaintUseCases(IComplaintStore store, IComplaintCommitter committer,
    IResidentResidenceSource residences, ICurrentBuildingTimeZone timeZone, IMaintenanceStaffDirectory staff,
    IComplaintContextSource contexts, TimeProvider clock)
{
    private async Task<ActiveResidentResidence?> Residence(Guid actor, CancellationToken ct)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(await timeZone.GetAsync(ct));
        return await residences.FindActiveAsync(actor,
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime), ct);
    }

    public async Task<ComplaintResult<ComplaintPage>> ListAsync(Guid actor, string role, string? search, string? status, int page, CancellationToken ct)
    {
        if (page < 1 || page > 100000 || search?.Length > 200) return new(400, Message: "Bộ lọc không hợp lệ.");
        ComplaintStatus? filter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<ComplaintStatus>(status, out var parsed) || !Enum.IsDefined(parsed)) return new(400, Message: "Trạng thái không hợp lệ.");
            filter = parsed;
        }
        var residence = role == "RESIDENT" ? await Residence(actor, ct) : null;
        if (role == "RESIDENT" && residence is null) return new(403, Message: "Tài khoản chưa có liên kết cư trú được xác minh.");
        return new(200, await store.ListAsync(residence?.ResidentId, role == "STAFF" ? actor : null, search, filter, page, ct));
    }

    public async Task<ComplaintResult<ComplaintDetail>> GetAsync(Guid actor, string role, Guid id, CancellationToken ct)
    {
        var complaint = await AccessibleAsync(actor, role, id, ct);
        return complaint is null ? new(404, Message: "Không tìm thấy phản ánh.") : new(200, await DetailAsync(complaint, role, actor, ct));
    }

    private async Task<Complaint?> AccessibleAsync(Guid actor, string role, Guid id, CancellationToken ct)
    {
        var complaint = await store.FindAsync(id, ct);
        if (complaint is null) return null;
        if (role == "MANAGER") return complaint;
        if (role == "STAFF") return complaint.Followups.Any(x => x.StaffUserId == actor) ? complaint : null;
        var residence = await Residence(actor, ct);
        return residence is not null && residence.ResidentId == complaint.ResidentId ? complaint : null;
    }

    public async Task<ComplaintResult<ComplaintDetail>> SubmitAsync(Guid actor, SubmitComplaint command, CancellationToken ct)
    {
        if (!Text(command.Subject, 200) || !Text(command.Description)) return new(400, Message: "Vui lòng nhập tiêu đề (tối đa 200 ký tự) và nội dung phản ánh.");
        var residence = await Residence(actor, ct);
        if (residence is null) return new(403, Message: "Tài khoản chưa có liên kết cư trú được xác minh.");
        var now = clock.GetUtcNow();
        var complaint = new Complaint($"PA-{Guid.NewGuid():N}"[..30].ToUpperInvariant(), residence.ResidentId,
            residence.ResidentApartmentId, command.Subject.Normalize(), command.Description.Normalize(), now, residence.ApartmentUnitId);
        store.Add(complaint);
        store.Add(new ComplaintActivity(complaint.Id, ComplaintActivityType.CREATED, now, toStatus: complaint.Status, performedBy: actor));
        await committer.SaveAsync(null, ct);
        return new(201, await DetailAsync(complaint, "RESIDENT", actor, ct));
    }

    public async Task<ComplaintResult<ComplaintDetail>> ReviewAsync(Guid actor, Guid id, CancellationToken ct) =>
        await MutateAsync(actor, id, ct, c => c.MarkUnderReview(clock.GetUtcNow()), ComplaintActivityType.MANAGER_REVIEWED);

    public async Task<ComplaintResult<ComplaintDetail>> ContextAsync(Guid actor, Guid id, ComplaintContext context, CancellationToken ct)
    {
        if (!await contexts.IsValidAsync(context, ct)) return new(400, Message: "Ngữ cảnh liên kết không tồn tại hoặc không phù hợp.");
        return await MutateAsync(actor, id, ct,
            c => c.AssociateContext(context.ApartmentUnitId, context.RelatedServiceRequestId, context.FacilityId, context.EquipmentId, clock.GetUtcNow()),
            ComplaintActivityType.MANAGER_REVIEWED, "Đã cập nhật liên kết ngữ cảnh.");
    }

    public async Task<ComplaintResult<ComplaintDetail>> AssignAsync(Guid actor, Guid id, AssignComplaintFollowup command, CancellationToken ct)
    {
        if (!Text(command.Title, 200) || await staff.GetActiveStaffAsync(command.StaffUserId, ct) is null)
            return new(400, Message: "Chọn nhân viên đang hoạt động và nhập tên công việc (tối đa 200 ký tự).");
        var complaint = await store.FindAsync(id, ct);
        if (complaint is null) return new(404, Message: "Không tìm thấy phản ánh.");
        var previousStatus = complaint.Status;
        try { complaint.MarkFollowUp(clock.GetUtcNow()); }
        catch (InvalidOperationException) { return Conflict(); }
        var now = clock.GetUtcNow();
        var followup = new ComplaintFollowup(id, command.StaffUserId, actor, command.Title, now, command.Instruction);
        store.Add(followup);
        if (previousStatus != complaint.Status)
            store.Add(new ComplaintActivity(id, ComplaintActivityType.STATUS_CHANGED, now,
                fromStatus: previousStatus, toStatus: complaint.Status, performedBy: actor));
        store.Add(new ComplaintActivity(id, ComplaintActivityType.FOLLOWUP_ASSIGNED, now, followup.Id, performedBy: actor));
        await committer.SaveAsync(null, ct);
        return new(200, await DetailAsync(complaint, "MANAGER", actor, ct));
    }

    public async Task<ComplaintResult<ComplaintDetail>> UpdateFollowupAsync(Guid actor, Guid id, Guid followupId, FollowupUpdate command, CancellationToken ct)
    {
        var complaint = await AccessibleAsync(actor, "STAFF", id, ct);
        var followup = complaint?.Followups.SingleOrDefault(x => x.Id == followupId && x.StaffUserId == actor);
        if (complaint is null || followup is null) return new(404, Message: "Không tìm thấy công việc được giao.");
        if (command.Complete && !Text(command.Result)) return new(400, Message: "Vui lòng nhập kết quả công việc.");
        if (complaint.Status != ComplaintStatus.FOLLOW_UP) return Conflict();
        var now = clock.GetUtcNow();
        var previousStatus = complaint.Status;
        try
        {
            if (command.Complete) followup.Complete(command.Result, now);
            else followup.Start(now);
            if (command.Complete && !complaint.Followups.Any(item => item.IsActive))
                complaint.FinishFollowUp(now);
            else
                complaint.MarkFollowUp(now);
        }
        catch (InvalidOperationException) { return Conflict(); }
        store.Add(new ComplaintActivity(id, ComplaintActivityType.FOLLOWUP_UPDATED, now, followup.Id,
            detail: command.Complete ? command.Result : "Đang thực hiện", performedBy: actor));
        if (complaint.Status != previousStatus)
            store.Add(new ComplaintActivity(id, ComplaintActivityType.STATUS_CHANGED, now,
                fromStatus: previousStatus, toStatus: complaint.Status, performedBy: actor));
        await committer.SaveAsync(null, ct);
        return new(200, await DetailAsync(complaint, "STAFF", actor, ct));
    }

    public async Task<ComplaintResult<ComplaintDetail>> ResolveAsync(Guid actor, Guid id, OfficialComplaintResponse command, CancellationToken ct)
    {
        if (!Text(command.Response)) return new(400, Message: "Vui lòng nhập phản hồi chính thức.");
        return await MutateAsync(actor, id, ct, c =>
        {
            if (c.Followups.Any(f => f.IsActive)) throw new InvalidOperationException();
            c.Resolve(clock.GetUtcNow());
            c.AddOfficialResponse(command.Response, clock.GetUtcNow());
        }, ComplaintActivityType.RESOLVED, command.Response);
    }

    public async Task<ComplaintResult<ComplaintDetail>> CloseAsync(Guid actor, Guid id, CancellationToken ct) =>
        await MutateAsync(actor, id, ct, c => c.Close(actor, clock.GetUtcNow()), ComplaintActivityType.CLOSED);

    private async Task<ComplaintResult<ComplaintDetail>> MutateAsync(Guid actor, Guid id, CancellationToken ct,
        Action<Complaint> action, ComplaintActivityType type, string? detail = null)
    {
        var complaint = await store.FindAsync(id, ct);
        if (complaint is null) return new(404, Message: "Không tìm thấy phản ánh.");
        var from = complaint.Status;
        try { action(complaint); }
        catch (InvalidOperationException) { return Conflict(); }
        var activity = new ComplaintActivity(id, type, clock.GetUtcNow(), fromStatus: from, toStatus: complaint.Status, detail: detail, performedBy: actor);
        store.Add(activity);
        await committer.SaveAsync(type == ComplaintActivityType.CLOSED ? new(activity.Id, id, complaint.ResidentId, activity.CreatedAt) : null, ct);
        return new(200, await DetailAsync(complaint, "MANAGER", actor, ct));
    }

    private static bool Text(string? value, int? max = null) => !string.IsNullOrWhiteSpace(value) && (!max.HasValue || value.Trim().Length <= max);
    private static ComplaintResult<ComplaintDetail> Conflict() => new(409, Message: "Trạng thái hiện tại không cho phép thao tác. Hoàn thành công việc được giao trước khi giải quyết; tải lại để kiểm tra.");

    private async Task<ComplaintDetail> DetailAsync(Complaint c, string role, Guid actor, CancellationToken ct)
    {
        var names = role == "MANAGER"
            ? await contexts.DisplayNamesAsync(c.Activities.Where(a => a.PerformedBy.HasValue).Select(a => a.PerformedBy!.Value)
                .Concat(c.Followups.Select(f => f.StaffUserId)).Distinct().ToArray(), ct)
            : null;
        var staffFollowups = c.Followups.Where(f => f.StaffUserId == actor).Select(f => f.Id).ToHashSet();
        var history = c.Activities.Where(a => role == "MANAGER" || role == "STAFF" && a.FollowupId is { } followupId && staffFollowups.Contains(followupId) || role == "RESIDENT" &&
            (a.ActivityType is ComplaintActivityType.CREATED or ComplaintActivityType.STATUS_CHANGED or ComplaintActivityType.RESOLVED or ComplaintActivityType.CLOSED ||
             a.ActivityType == ComplaintActivityType.MANAGER_REVIEWED && a.FromStatus != a.ToStatus))
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Select(a => new ComplaintHistory(a.ActivityType.ToString(), a.FromStatus?.ToString(), a.ToStatus?.ToString(),
                role == "MANAGER" || role == "STAFF" && a.PerformedBy == actor || a.ActivityType == ComplaintActivityType.RESOLVED
                    ? SafeHistoryDetail(a.Detail) : null, a.CreatedAt,
                a.PerformedBy == actor ? "Bạn" : a.PerformedBy is not { } performer ? "Hệ thống" :
                    names?.GetValueOrDefault(performer) ??
                    (
                    a.ActivityType == ComplaintActivityType.CREATED ? "Cư dân" :
                    a.ActivityType == ComplaintActivityType.FOLLOWUP_UPDATED ? "Nhân viên phụ trách" : "Ban Quản lý"))).ToArray();
        var followups = c.Followups.Where(f => role == "MANAGER" || role == "STAFF" && f.StaffUserId == actor)
            .OrderBy(f => f.CreatedAt).Select(f => new FollowupView(f.Id, f.StaffUserId, f.Title, f.Instruction, f.Status.ToString(),
                f.Result, names?.GetValueOrDefault(f.StaffUserId))).ToArray();
        return new(c.Id, c.ComplaintNumber, c.Subject, c.Description, c.Status.ToString(),
            role == "STAFF" ? null : c.OfficialResponse, c.SubmittedAt,
            new(c.ApartmentUnitId, c.RelatedServiceRequestId, c.FacilityId, c.EquipmentId), history, followups, await timeZone.GetAsync(ct));
    }

    private static string? SafeHistoryDetail(string? value) =>
        value?.StartsWith("Cập nhật liên kết ngữ cảnh: ", StringComparison.Ordinal) == true
            ? "Đã cập nhật liên kết ngữ cảnh." : value;
}
