using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.Modules.ServiceRequests.Infrastructure;

public sealed class ServiceRequestMaintenanceSource(
    ServiceRequestsDbContext db,
    IApartmentResidentRelationshipSource apartments)
    : IServiceRequestMaintenanceSource
{
    public async Task<ServiceRequestMaintenanceReference?> GetAsync(
        Guid serviceRequestId,
        CancellationToken ct)
    {
        if (serviceRequestId == Guid.Empty)
            return null;

        var request = await db.ServiceRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == serviceRequestId, ct);

        if (request is null)
            return null;

        var apartment = await FindApartmentAsync(request.ApartmentUnitId, ct);
        return Map(request, apartment);
    }

    public async Task<IReadOnlyList<ServiceRequestMaintenanceReference>> SearchAsync(
        string? search,
        int limit,
        CancellationToken ct)
    {
        if (limit is < 1 or > 50)
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 50.");

        var normalizedSearch = search?.Trim();
        if (normalizedSearch?.Length > 100)
            throw new ArgumentException("Search must not exceed 100 characters.", nameof(search));

        var query = db.ServiceRequests
            .AsNoTracking()
            .Where(request =>
                request.Status == ServiceRequestStatus.SUBMITTED ||
                request.Status == ServiceRequestStatus.UNDER_REVIEW ||
                request.Status == ServiceRequestStatus.ASSIGNED ||
                request.Status == ServiceRequestStatus.IN_PROGRESS);

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var term = normalizedSearch.ToUpperInvariant();
            query = query.Where(request =>
                request.RequestNumber.ToUpper().Contains(term) ||
                request.Title.ToUpper().Contains(term));
        }

        var requests = await query
            .OrderByDescending(request => request.SubmittedAt)
            .ThenBy(request => request.RequestNumber)
            .Take(limit)
            .ToArrayAsync(ct);

        var apartmentIds = requests
            .Where(request => request.ApartmentUnitId.HasValue)
            .Select(request => request.ApartmentUnitId!.Value)
            .Distinct()
            .ToArray();
        var apartmentsById = apartmentIds.Length == 0
            ? new Dictionary<Guid, ApartmentReference>()
            : (await apartments.GetApartmentsAsync(apartmentIds, ct))
                .ToDictionary(apartment => apartment.Id);

        return requests
            .Select(request => Map(
                request,
                request.ApartmentUnitId is { } apartmentId && apartmentsById.TryGetValue(apartmentId, out var apartment)
                    ? apartment
                    : null))
            .ToArray();
    }

    private async Task<ApartmentReference?> FindApartmentAsync(Guid? apartmentUnitId, CancellationToken ct)
    {
        if (apartmentUnitId is not { } id)
            return null;

        return (await apartments.GetApartmentsAsync([id], ct)).SingleOrDefault();
    }

    private static ServiceRequestMaintenanceReference Map(ServiceRequest request, ApartmentReference? apartment)
    {
        var isLinkable = request.Status is
            ServiceRequestStatus.SUBMITTED or
            ServiceRequestStatus.UNDER_REVIEW or
            ServiceRequestStatus.ASSIGNED or
            ServiceRequestStatus.IN_PROGRESS;

        return new ServiceRequestMaintenanceReference(
            request.Id,
            request.RequestNumber,
            request.Title,
            request.Status.ToString(),
            request.ApartmentUnitId,
            request.FacilityId,
            request.EquipmentId,
            request.FinalPriorityCode,
            request.SubmittedAt,
            isLinkable,
            isLinkable ? null : "service_request_not_linkable",
            apartment?.UnitNumber,
            apartment?.FloorNumber);
    }
}
