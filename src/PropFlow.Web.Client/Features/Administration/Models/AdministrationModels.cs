namespace PropFlow.Web.Client.Features.Administration.Models;

public sealed record InternalAccountSummary(int Total, int Active, int Disabled, int Locked, Dictionary<string, int> RoleCounts);
public sealed record PagedInternalAccounts(int Total, int Page, int PageSize, InternalAccount[] Items);
public sealed record InternalAccount(Guid Id, string Username, string DisplayName, string? Email, string Role, string Status);
public sealed record InternalAccountProfile(Guid Id, string Username, string DisplayName, string? Email,
    string? PhoneNumber, string Role, string Status, bool EmailVerified);
public sealed record UpdateInternalAccountProfile(string DisplayName, string? PhoneNumber);
public sealed record CreateInternalAccount(string Username, string DisplayName, string Email, string Password, string Role, string? PhoneNumber);
public sealed record ChangeRole(string Role);
public sealed record ChangeStatus(string Status);
public sealed record AdministrationPermission(string Code, string Name, string Module, string? Description);
public sealed record AdministrationRoleAccess(string Code, string Name, string? Description, bool IsActive, AdministrationPermission[] Permissions);
public sealed record InternalAccountAccess(Guid UserId, string Username, string DisplayName, string Status, string Role, AdministrationPermission[] Permissions);
public sealed record PagedAdministrationActivities(int Total, int Page, int PageSize, AdministrationActivity[] Items, string? TimeZoneId = null);
public sealed record AdministrationActivity(Guid Id, DateTimeOffset Timestamp, string Action,
    Guid? ActorUserId, string? ActorUsername, string? ActorDisplayName,
    Guid? TargetAccountId, string? TargetUsername, string? TargetDisplayName, string? TargetRole, string? TargetStatus,
    string? OldRole, string? NewRole, string? OldStatus, string? NewStatus,
    string Module = "Administration", string? EntityType = null, Guid? EntityId = null, string? Label = null, string Outcome = "SUCCESS",
    AuditFieldChange[]? Changes = null, string? CorrelationId = null, int? HttpStatus = null);
public sealed record AuditFieldChange(string Field, string? Before, string? After);
public sealed record SystemActivityFilter(string? Module = null, string? Action = null, string? Outcome = null, string? Actor = null, DateOnly? DateFrom = null, DateOnly? DateTo = null);
public sealed record AdministrationOverviewStatus(string Status, int Count);
public sealed record AdministrationOverviewTrend(DateOnly Date, int Count);
public sealed record AdministrationOverview(
    int TotalApartments,
    int VacantApartments,
    int TotalResidents,
    int OpenServiceRequests,
    int OccupiedApartments,
    AdministrationOverviewStatus[] ServiceRequestsByStatus,
    AdministrationOverviewTrend[] ServiceRequestTrend);
