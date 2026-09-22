using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.Modules.ServiceRequests.Infrastructure;

public sealed class ServiceRequestOverviewSource(ServiceRequestsDbContext db) : IServiceRequestOverviewSource
{
    public Task<int> CountOpenAsync(CancellationToken ct) => db.ServiceRequests.AsNoTracking()
        .CountAsync(request => request.Status != ServiceRequestStatus.CLOSED && request.Status != ServiceRequestStatus.CANCELLED, ct);
}
