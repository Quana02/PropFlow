using System.ComponentModel.DataAnnotations;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.Modules.AiRecommendation.Application.FacilityDiscovery;

public sealed record FacilityRecommendationRequest([Required,StringLength(1200,MinimumLength=2)] string Need, DateOnly? PreferredDate=null);
public sealed record ResidentFacilityCard(Guid Id,string Name,string? Type,string? Location,string Description,string Audience,
    int? MinimumAge,int? MaximumAge,string ChildPolicy,string SupervisionNote,string Rules,FacilityOpening[] Hours,
    DateOnly[] ClosedDates,string ResponsibleName,string ContactDetails);
public sealed record ResidentFacilityCatalogue(ResidentFacilityCard[] Facilities,DateOnly Today);
public sealed record FacilityVisitTime(DateOnly Date,TimeOnly OpensAt,TimeOnly ClosesAt);
public sealed record FacilitySuggestion(ResidentFacilityCard Facility,FacilityVisitTime[] OpeningTimes,string Reason,string Availability="",bool IsOpenNow=false);
public sealed record FacilityRecommendationView(FacilitySuggestion[] Suggestions,string Mode,string Message,string? FollowUp,DateTimeOffset CalculatedAt,Guid? SpeechId=null,bool ForChildren=false);
public sealed record FacilityAdvisorInput(string Key,string Name,string? Type,string Description,string Audience,int? MinimumAge,int? MaximumAge,string ChildPolicy,DateTime? LocalNow=null,FacilityVisitTime[]? OpeningTimes=null);
public sealed record FacilityAdvisorResult(string[] Keys,bool ForChildren,bool NeedsSupervision,string Model,int? InputTokens,int? OutputTokens,string? Clarification=null);
public interface IFacilityNeedAdvisor
{ bool Ready {get;} Task<FacilityAdvisorResult?> RecommendAsync(string need,FacilityAdvisorInput[] choices,CancellationToken ct); }
public interface IFacilityResidentAccess { Task<bool> ActiveAsync(Guid actor,CancellationToken ct); }
public sealed class FacilityRecommendationOptions
{
    public int AiRequestsPerUserDay {get;set;}=20;
    public int AiRequestsPerDay {get;set;}=200;
    public int CacheMinutes {get;set;}=10;
    public int MaxCandidates {get;set;}=20;
}
public sealed class FacilityRecommendationFailure(int status,string code,string message):Exception(message)
{ public int Status {get;}=status;public string Code {get;}=code; }
