using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Web.Client.Features.Apartment.Models;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Features.Resident.Shared.Models;

namespace PropFlow.Web.Client.Shared.Spreadsheets;

// Only the two approved directory endpoints; no arbitrary request or key/value editor.
public sealed class DirectoryExcelApi(AuthenticatedApiClient api)
{
    private static string Query(params (string Name,string? Value)[] values)=>string.Join("&",values.Where(x=>!string.IsNullOrWhiteSpace(x.Value)).Select(x=>Uri.EscapeDataString(x.Name)+"="+Uri.EscapeDataString(x.Value!)));
    public Task<ApiResult<ApiFileContent>> ResidentsAsync(string? search,string? status,string? apartment,string? relationship,string? residency,string? household,CancellationToken ct,int? floorNumber=null,bool? hasAccount=null,ResidentDirectoryMultiFilter? multi=null)=>api.GetFileAsync("api/v1/residents/excel/export?"+Query(("search",search),("status",status),("apartmentUnitId",apartment),("relationshipKind",relationship),("residencyType",residency),("householdRole",household),("floorNumber",floorNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture)),("hasAccount",hasAccount?.ToString()))+multi?.Query,ct);
    public Task<ApiResult<ApiFileContent>> ApartmentsAsync(string? search,string? status,string? floor,string? type,string? occupancy,CancellationToken ct,bool? hasOwner=null,decimal? minArea=null,decimal? maxArea=null,ApartmentDirectoryMultiFilter? multi=null)=>api.GetFileAsync("api/v1/apartments/excel/export?"+Query(("search",search),("status",status),("floorNumber",floor),("apartmentUnitTypeId",type),("occupancy",occupancy),("hasOwner",hasOwner?.ToString()),("minArea",minArea?.ToString(System.Globalization.CultureInfo.InvariantCulture)),("maxArea",maxArea?.ToString(System.Globalization.CultureInfo.InvariantCulture)))+multi?.Query,ct);
    public Task<ApiResult<ApiFileContent>> TemplateAsync(CancellationToken ct)=>api.GetFileAsync("api/v1/apartments/excel/template",ct);
    public Task<ApiResult<ApartmentExcelPreview>> PreviewAsync(MultipartFormDataContent content,CancellationToken ct)=>api.SendMultipartAsync<ApartmentExcelPreview>("api/v1/apartments/excel/preview",content,ct);
    public Task<ApiResult<ApartmentExcelResult>> CommitAsync(string ticket,CancellationToken ct)=>api.SendAsync<ApartmentExcelResult>(HttpMethod.Post,"api/v1/apartments/excel/commit",new CommitApartmentExcel {Ticket=ticket},csrf:true,ct:ct);
}
