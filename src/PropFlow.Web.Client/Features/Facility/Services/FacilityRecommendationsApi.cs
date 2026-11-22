using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Web.Client.Services.Api;
namespace PropFlow.Web.Client.Features.Facility.Services;
public sealed record ResidentFacilityCard(Guid Id,string Name,string? Type,string? Location,string Description,string Audience,int? MinimumAge,int? MaximumAge,string ChildPolicy,string SupervisionNote,string Rules,FacilityOpening[] Hours,DateOnly[] ClosedDates,string ResponsibleName,string ContactDetails);
public sealed record ResidentFacilityCatalogue(ResidentFacilityCard[] Facilities,DateOnly Today);
public sealed record FacilityVisitTime(DateOnly Date,TimeOnly OpensAt,TimeOnly ClosesAt);
public sealed record FacilitySuggestion(ResidentFacilityCard Facility,FacilityVisitTime[] OpeningTimes,string Reason,string Availability="",bool IsOpenNow=false);
public sealed record FacilityRecommendationView(FacilitySuggestion[] Suggestions,string Mode,string Message,string? FollowUp,DateTimeOffset CalculatedAt,Guid? SpeechId=null,bool ForChildren=false);
public sealed record FacilitySpeechAudio(string Audio,string ContentType);
public sealed record FacilityLiveSession(string Token,string WebSocketUrl,System.Text.Json.JsonElement Setup,DateTimeOffset ExpiresAt);
public sealed class FacilityRecommendationsApi(AuthenticatedApiClient api)
{
    public Task<ApiResult<FacilityRecommendationView>> LiveLookupAsync(string need,CancellationToken ct)=>api.SendAsync<FacilityRecommendationView>(HttpMethod.Post,"api/v1/resident/facility-recommendations/live/lookup",new {need},ct:ct);
    public Task<ApiResult<FacilityLiveSession>> LiveAsync(CancellationToken ct)=>api.SendAsync<FacilityLiveSession>(HttpMethod.Post,"api/v1/resident/facility-recommendations/live",ct:ct);
    public Task<ApiResult<FacilityGuidesManagement>> ManagementAsync(CancellationToken ct)=>api.SendAsync<FacilityGuidesManagement>(HttpMethod.Get,"api/v1/facility-guides",ct:ct);
    public Task<ApiResult<EmptyResponse>> SaveAsync(Guid facility,SaveFacilityGuide request,CancellationToken ct)=>api.SendAsync<EmptyResponse>(HttpMethod.Put,$"api/v1/facility-guides/{facility}",request,ct:ct);
    public Task<ApiResult<ResidentFacilityCatalogue>> CatalogueAsync(CancellationToken ct)=>api.SendAsync<ResidentFacilityCatalogue>(HttpMethod.Get,"api/v1/resident/facility-recommendations",ct:ct);
    public Task<ApiResult<FacilityRecommendationView>> TodayAsync(Guid facilityId,CancellationToken ct)=>api.SendAsync<FacilityRecommendationView>(HttpMethod.Get,$"api/v1/resident/facility-recommendations/today?facilityId={facilityId:D}",ct:ct);
    public Task<ApiResult<FacilityRecommendationView>> RecommendAsync(string need,DateOnly? preferredDate,CancellationToken ct)=>api.SendAsync<FacilityRecommendationView>(HttpMethod.Post,"api/v1/resident/facility-recommendations",new {need,preferredDate},ct:ct);
    public Task<ApiResult<FacilitySpeechAudio>> SpeechAsync(Guid? speechId,string kind,CancellationToken ct)=>api.SendAsync<FacilitySpeechAudio>(HttpMethod.Post,"api/v1/resident/facility-recommendations/speech",new {speechId,kind},ct:ct);
    public static string Weekday(int day)=>day==0?"Chủ nhật":"Thứ "+(day+1);
    public static string Audience(string value)=>value switch {"CHILDREN"=>"Trẻ em","FAMILY"=>"Gia đình","ADULTS"=>"Người lớn",_=>"Cư dân nói chung"};
    public static string ChildPolicy(string value)=>value switch {"SUPERVISED_CONTACT_REQUIRED"=>"Có thông tin hoạt động có giám sát; cần liên hệ xác nhận tiếp nhận trẻ.","NOT_FOR_CHILDREN"=>"Không dành cho trẻ em.",_=>"Trẻ em phải có phụ huynh hoặc người giám hộ đi cùng; không phải dịch vụ trông trẻ."};
    public static string Age(int? min,int? max)=>min is null&&max is null?"Theo điều kiện BQL cung cấp":min is null?$"Đến {max} tuổi":max is null?$"Từ {min} tuổi":$"{min}–{max} tuổi";
}
