using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.Residents.Domain.ResidentApartments;
using PropFlow.Shared.Spreadsheets;

namespace PropFlow.Modules.Residents.Presentation;

public sealed partial class ResidentsController
{
    [HttpGet("excel/export"), Authorize(Policy = ResidentsAuthorizationPolicies.Read)]
    [ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> ExportExcel(string? search,ResidentStatus? status,Guid? apartmentUnitId,
        ResidentDirectoryRelationshipFilter? relationshipKind,ResidencyType? residencyType,HouseholdRole? householdRole,CancellationToken ct,int? floorNumber=null,bool? hasAccount=null,
        [FromQuery] ResidentStatus[]? statuses=null,[FromQuery] Guid[]? apartmentUnitIds=null,[FromQuery] int[]? floorNumbers=null,
        [FromQuery] ResidentDirectoryRelationshipFilter[]? relationshipKinds=null)
    {
        using var limit=CancellationTokenSource.CreateLinkedTokenSource(ct);limit.CancelAfter(TimeSpan.FromSeconds(30));
        var people=new List<ResidentListItem>();var total=-1;
        for(var page=1;page<=100;page++)
        {
            var response=await List(search,status,apartmentUnitId,relationshipKind,residencyType,householdRole,page,100,limit.Token,floorNumber,hasAccount,statuses,apartmentUnitIds,floorNumbers,relationshipKinds);
            if(response.Result is not OkObjectResult {Value:PagedResidentsResponse batch})return response.Result??StatusCode(500);
            if(batch.TotalCount>10000)return Problem(statusCode:413,title:"Có hơn 10.000 cư dân. Vui lòng thu hẹp bộ lọc trước khi xuất.");
            if(total>=0&&total!=batch.TotalCount)return Conflict(new ProblemDetails {Status=409,Title="Danh sách vừa thay đổi. Vui lòng xuất lại."});
            total=batch.TotalCount;people.AddRange(batch.Items);
            if(people.Count>=total)break;
        }
        if(people.Count!=total||people.Select(x=>x.Id).Distinct().Count()!=total)return Conflict(new ProblemDetails {Status=409,Title="Danh sách vừa thay đổi. Vui lòng xuất lại."});
        var contacts=User.IsInRole("MANAGER")||User.IsInRole("STAFF");
        var columns=new List<XlsxColumn> {new("Mã cư dân",20),new("Họ và tên",30),new("Trạng thái",22),new("Tài khoản",22),new("Căn hộ sở hữu",32),new("Căn hộ đang cư trú",32)};
        if(contacts)columns.AddRange([new("Số điện thoại",22),new("Email liên hệ",35)]);
        var rows=people.Select(person=>
        {
            var values=new List<object?> {person.ResidentCode,person.FullName,person.Status switch {"ACTIVE"=>"Đang hoạt động","INACTIVE"=>"Ngừng hoạt động",_=>"Đã chuyển đi"},person.HasAccount?"Đã liên kết tài khoản":"Chưa có tài khoản",
                string.Join(", ",person.ApartmentRelationships.Where(x=>x.IsCurrentOwner).Select(x=>x.UnitNumber)),string.Join(", ",person.ApartmentRelationships.Where(x=>x.IsCurrentResident).Select(x=>x.UnitNumber))};
            if(contacts)values.AddRange([person.PhoneNumber,person.Email]);return values.ToArray();
        }).ToArray();
        var selectedApartments=ResidentDirectorySelection.Merge(apartmentUnitId,apartmentUnitIds);
        var refs=await apartmentRelationships.GetApartmentsAsync(selectedApartments,limit.Token);
        var apartmentLabel=selectedApartments.Length==0?"Tất cả":string.Join(", ",selectedApartments.Select(id=>refs.FirstOrDefault(x=>x.Id==id)?.UnitNumber??"Không tìm thấy căn hộ"));
        static string RelationLabel(ResidentDirectoryRelationshipFilter value)=>value switch {ResidentDirectoryRelationshipFilter.OWNER_ONLY=>"Chỉ sở hữu, không cư trú",ResidentDirectoryRelationshipFilter.RESIDENT_ONLY=>"Chỉ cư trú, không sở hữu",ResidentDirectoryRelationshipFilter.OWNER_AND_RESIDENT=>"Vừa sở hữu vừa cư trú",ResidentDirectoryRelationshipFilter.ANY_OWNER=>"Có sở hữu căn hộ",ResidentDirectoryRelationshipFilter.ANY_RESIDENT=>"Đang cư trú",ResidentDirectoryRelationshipFilter.NONE=>"Không có quan hệ hiện hành",_=>"Tất cả"};
        static string StateLabel(ResidentStatus value)=>value switch {ResidentStatus.ACTIVE=>"Đang hoạt động",ResidentStatus.INACTIVE=>"Ngừng hoạt động",ResidentStatus.MOVED_OUT=>"Đã chuyển đi",_=>"Tất cả"};
        var selectedRelationships=ResidentDirectorySelection.Merge(relationshipKind,relationshipKinds);
        var selectedStatuses=ResidentDirectorySelection.Merge(status,statuses);
        var selectedFloors=ResidentDirectorySelection.Merge(floorNumber,floorNumbers);
        var relationshipLabel=selectedRelationships.Length==0?"Tất cả":string.Join(", ",selectedRelationships.Select(RelationLabel));
        var statusLabel=selectedStatuses.Length==0?"Tất cả":string.Join(", ",selectedStatuses.Select(StateLabel));
        var floorLabel=selectedFloors.Length==0?"Tất cả":string.Join(", ",selectedFloors.Order());
        var filters=$"Bộ lọc: Tìm kiếm: {(string.IsNullOrWhiteSpace(search)?"Tất cả":search.Trim())} · Trạng thái: {statusLabel} · Căn hộ: {apartmentLabel} · Quan hệ: {relationshipLabel} · Cư trú: {residencyType switch {ResidencyType.OWNER_OCCUPIED=>"Chủ sở hữu đang cư trú",ResidencyType.TENANT=>"Người thuê",ResidencyType.AUTHORIZED_OCCUPANT=>"Người được phép cư trú",_=>"Tất cả"}} · Vai trò hộ: {householdRole switch {HouseholdRole.HOUSEHOLD_HEAD=>"Chủ hộ",HouseholdRole.HOUSEHOLD_MEMBER=>"Thành viên hộ",_=>"Tất cả"}} · Tầng: {floorLabel} · Tài khoản: {(hasAccount.HasValue?(hasAccount.Value?"Đã liên kết":"Chưa liên kết"):"Tất cả")}";
        return File(XlsxTable.Write(new XlsxSheet("Danh sách cư dân",columns,rows,"PropFlow — DANH SÁCH CƯ DÂN",$"Xuất lúc: {clock.GetUtcNow():dd/MM/yyyy HH:mm:ss} UTC · {total} cư dân",filters)),XlsxTable.ContentType,$"PropFlow_DanhSachCuDan_{await residencyService.BusinessDateAsync(ct):yyyyMMdd}.xlsx");
    }
}

