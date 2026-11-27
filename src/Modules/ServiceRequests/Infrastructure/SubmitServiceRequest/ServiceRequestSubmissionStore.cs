using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Modules.ServiceRequests.Application.SubmitServiceRequest;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.Modules.ServiceRequests.Infrastructure.SubmitServiceRequest;

public sealed class ServiceRequestSubmissionStore(ServiceRequestsDbContext db)
    : IServiceRequestSubmissionStore
{
    public Task<Guid?> FindActiveCategoryIdAsync(string categoryCode, CancellationToken ct) =>
        db.ServiceRequestCategories
            .AsNoTracking()
            .Where(category => category.IsActive && category.Code == categoryCode)
            .Select(category => (Guid?)category.Id)
            .SingleOrDefaultAsync(ct);

    public Task<ServiceRequest?> FindByRequestNumberAsync(string requestNumber, CancellationToken ct) =>
        db.ServiceRequests.AsNoTracking().SingleOrDefaultAsync(x => x.RequestNumber == requestNumber, ct);

    public async Task<ServiceRequest> AddAsync(
        ServiceRequest request,
        ServiceRequestActivity initialActivity,
        CancellationToken ct)
    {
        db.ServiceRequests.Add(request);
        db.ServiceRequestActivities.Add(initialActivity);
        var notice = request.SubmittedByUserId is { } sender
            ? new ServiceRequestWorkflowNotice(sender, null, request.RequestNumber).ToEvent(ServiceRequestWorkflowNotice.Submitted, request.Id, request.SubmittedAt) : null;
        if (notice is not null) db.ResidentEvents.Add(notice);
        try { await db.SaveChangesAsync(ct); return request; }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } && request.RequestNumber.StartsWith("YC-AI-", StringComparison.Ordinal))
        {
            // SaveChanges rolls back the request, activity and notice together; only detach this attempted write.
            // Detach required dependents before their principal, otherwise EF fixup severs a required relationship.
            db.Entry(initialActivity).State = EntityState.Detached;
            if (notice is not null) db.Entry(notice).State = EntityState.Detached;
            db.Entry(request).State = EntityState.Detached;
            var saved = await FindByRequestNumberAsync(request.RequestNumber, ct);
            if (saved is null) throw;
            return saved;
        }
    }
}
