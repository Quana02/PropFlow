namespace PropFlow.Modules.ServiceRequests.Contracts;

public sealed record ServiceRequestMaintenanceReference(
    Guid Id,
    string RequestNumber,
    string Title,
    string Status,
    Guid? ApartmentUnitId,
    Guid? FacilityId,
    Guid? EquipmentId,
    string? PriorityCode,
    DateTimeOffset SubmittedAt,
    bool IsLinkable,
    string? IneligibilityCode);

public interface IServiceRequestMaintenanceSource
{
    Task<ServiceRequestMaintenanceReference?> GetAsync(
        Guid serviceRequestId,
        CancellationToken ct);

    Task<IReadOnlyList<ServiceRequestMaintenanceReference>> SearchAsync(
        string? search,
        int limit,
        CancellationToken ct);
}
