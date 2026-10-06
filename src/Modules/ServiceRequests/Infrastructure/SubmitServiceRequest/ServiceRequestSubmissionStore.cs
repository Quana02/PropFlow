using Microsoft.EntityFrameworkCore;
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

    public async Task AddAsync(
        ServiceRequest request,
        ServiceRequestActivity initialActivity,
        CancellationToken ct)
    {
        db.ServiceRequests.Add(request);
        db.ServiceRequestActivities.Add(initialActivity);
        await db.SaveChangesAsync(ct);
    }
}
