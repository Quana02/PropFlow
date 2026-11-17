using System.ComponentModel.DataAnnotations;

namespace PropFlow.Modules.PropertyAssets.Contracts;

public sealed record FacilityOpening(int DayOfWeek, TimeOnly OpensAt, TimeOnly ClosesAt);
public sealed record FacilityGuideView(Guid FacilityId, string Name, string? Type, string? Location, string Status,
    bool Published, string Description, string Audience, int? MinimumAge, int? MaximumAge, string ChildPolicy,
    string SupervisionNote, string Rules, FacilityOpening[] Hours, DateOnly[] ClosedDates,
    Guid? ResponsibleStaffId, string? ResponsibleName, string ContactDetails, Guid Token, DateTimeOffset UpdatedAt);
public sealed record SaveFacilityGuide(bool Published, [Required, StringLength(2000)] string Description,
    [Required] string Audience, int? MinimumAge, int? MaximumAge, [Required] string ChildPolicy,
    [StringLength(1000)] string? SupervisionNote, [StringLength(4000)] string? Rules,
    [Required] FacilityOpening[] Hours, [Required] DateOnly[] ClosedDates, Guid? ResponsibleStaffId,
    [StringLength(300)] string? ContactDetails, Guid? ExpectedToken);
public sealed record FacilityContactOption(Guid Id, string Name);
public sealed record FacilityGuidesManagement(FacilityGuideView[] Facilities, FacilityContactOption[] Staff, DateOnly Today);
public sealed record FacilityGuideWindow(FacilityGuideView[] Guides, string Fingerprint);
public interface IFacilityGuideSource { Task<FacilityGuideWindow> ReadAsync(CancellationToken ct); }
public sealed class FacilityGuideFailure(int status, string code, string message) : Exception(message)
{ public int Status { get; } = status; public string Code { get; } = code; }
