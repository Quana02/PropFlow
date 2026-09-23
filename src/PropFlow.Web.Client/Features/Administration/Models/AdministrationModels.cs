namespace PropFlow.Web.Client.Features.Administration.Models;

public sealed record InternalAccountSummary(int Total, int Active, int Disabled, int Locked, Dictionary<string, int> RoleCounts);
public sealed record PagedInternalAccounts(int Total, int Page, int PageSize, InternalAccount[] Items);
public sealed record InternalAccount(Guid Id, string Username, string DisplayName, string? Email, string Role, string Status);
public sealed record CreateInternalAccount(string Username, string DisplayName, string Email, string Password, string Role, string? PhoneNumber);
public sealed record ChangeRole(string Role);
public sealed record ChangeStatus(string Status);
public sealed record PagedAdministrationActivities(int Total, int Page, int PageSize, AdministrationActivity[] Items);
public sealed record AdministrationActivity(Guid Id, DateTimeOffset Timestamp, string Action,
    Guid? ActorUserId, string? ActorUsername, string? ActorDisplayName,
    Guid? TargetAccountId, string? TargetUsername, string? TargetDisplayName, string? TargetRole, string? TargetStatus,
    string? OldRole, string? NewRole, string? OldStatus, string? NewStatus);
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
