using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;
using PropFlow.Modules.Apartments.Infrastructure;
using PropFlow.Shared.Spreadsheets;

namespace PropFlow.Modules.Apartments.Presentation;

public sealed partial class ApartmentsController
{
    [HttpGet("excel/export"),Authorize(Policy=ApartmentsAuthorizationPolicies.Read)]
    [ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> ExportExcel(string? search,MasterDataStatus? status,int? floorNumber,Guid? apartmentUnitTypeId,ApartmentOccupancyFilter? occupancy,CancellationToken ct,bool? hasOwner=null,decimal? minArea=null,decimal? maxArea=null,[FromQuery] MasterDataStatus[]? statuses=null,[FromQuery] int[]? floorNumbers=null,[FromQuery] Guid[]? apartmentUnitTypeIds=null)
    {
        using var limit=CancellationTokenSource.CreateLinkedTokenSource(ct);limit.CancelAfter(TimeSpan.FromSeconds(30));
        var units=new List<ApartmentListItem>();var total=-1;
        for(var page=1;page<=100;page++)
        {
            var response=await List(search,status,floorNumber,apartmentUnitTypeId,occupancy,page,100,limit.Token,hasOwner,minArea,maxArea,statuses,floorNumbers,apartmentUnitTypeIds);
            if(response.Result is not OkObjectResult {Value:PagedApartmentsResponse batch})return response.Result??StatusCode(500);
            if(batch.TotalCount>10000)return Problem(statusCode:413,title:"Có hơn 10.000 căn hộ. Vui lòng thu hẹp bộ lọc trước khi xuất.");
            if(total>=0&&total!=batch.TotalCount)return Conflict(new ProblemDetails {Status=409,Title="Danh sách vừa thay đổi. Vui lòng xuất lại."});
            total=batch.TotalCount;units.AddRange(batch.Items);if(units.Count>=total)break;
        }
        if(units.Count!=total||units.Select(x=>x.Id).Distinct().Count()!=total)return Conflict(new ProblemDetails {Status=409,Title="Danh sách vừa thay đổi. Vui lòng xuất lại."});
        if(units.Any(x=>x.CurrentResidents is null||x.IsOccupied!=(x.CurrentResidents.Count>0)))return Conflict(new ProblemDetails{Status=409,Title="Chưa tải đủ thông tin cư dân. Vui lòng tải lại danh sách trước khi xuất."});
        var selectedTypes=ApartmentDirectorySelection.Merge(apartmentUnitTypeId,apartmentUnitTypeIds);
        var typeNames=await db.ApartmentUnitTypes.AsNoTracking().Where(x=>selectedTypes.Contains(x.Id)).Select(x=>new{x.Id,x.Name}).ToDictionaryAsync(x=>x.Id,x=>x.Name,limit.Token);
        var typeLabel=selectedTypes.Length==0?"Tất cả":string.Join(", ",selectedTypes.Select(id=>typeNames.GetValueOrDefault(id,"Không tìm thấy loại căn hộ")));
        var selectedStatuses=ApartmentDirectorySelection.Merge(status,statuses);
        var statusLabel=selectedStatuses.Length==0?"Tất cả":string.Join(", ",selectedStatuses.Select(x=>x==MasterDataStatus.ACTIVE?"Đang quản lý":"Ngừng quản lý"));
        var selectedFloors=ApartmentDirectorySelection.Merge(floorNumber,floorNumbers);
        var floorLabel=selectedFloors.Length==0?"Tất cả":string.Join(", ",selectedFloors.Order());
        var exportedAt=TimeZoneInfo.ConvertTime(clock.GetUtcNow(),TimeZoneInfo.FindSystemTimeZoneById(await buildingTimeZone.GetAsync(limit.Token)));
        var rows=units.Select(x=>new object?[]{x.UnitNumber,x.FloorNumber,x.UnitType,x.UsableAreaM2,x.Owners.Count==0?"Chưa xác định":string.Join('\n',x.Owners.Select(r=>r.FullName)),(x.CurrentResidents?.Count??0)==0?"Căn hộ trống":string.Join('\n',x.CurrentResidents!.Select(r=>$"{r.FullName} · {r.ResidentCode}")),x.Status=="ACTIVE"?"Đang quản lý":"Ngừng quản lý",x.IsOccupied?"Đang có cư dân":"Căn hộ trống"}).ToArray();
        var filters=$"Bộ lọc: Tìm kiếm: {(string.IsNullOrWhiteSpace(search)?"Tất cả":search.Trim())} · Trạng thái: {statusLabel} · Tầng: {floorLabel} · Loại: {typeLabel} · Cư trú: {occupancy switch {ApartmentOccupancyFilter.OCCUPIED=>"Có cư dân",ApartmentOccupancyFilter.VACANT=>"Trống",_=>"Tất cả"}} · Chủ sở hữu: {(hasOwner.HasValue?(hasOwner.Value?"Có chủ sở hữu":"Chưa có chủ sở hữu"):"Tất cả")} · Diện tích từ: {minArea?.ToString(System.Globalization.CultureInfo.InvariantCulture)??"—"} · đến: {maxArea?.ToString(System.Globalization.CultureInfo.InvariantCulture)??"—"}";
        return File(XlsxTable.Write(new XlsxSheet("Danh sách căn hộ",[new("Mã căn hộ",22),new("Tầng",12,"integer"),new("Loại căn hộ",30),new("Diện tích (m²)",20,"decimal"),new("Chủ sở hữu",32),new("Cư dân đang cư trú",34),new("Trạng thái",22),new("Tình trạng cư trú",25)],rows,
            "PropFlow — DANH SÁCH CĂN HỘ",$"Xuất lúc: {exportedAt:dd/MM/yyyy HH:mm:ss} UTC{exportedAt:zzz} · {total} căn hộ",filters)),XlsxTable.ContentType,$"PropFlow_DanhSachCanHo_{await TodayAsync(ct):yyyyMMdd}.xlsx");
    }
    [HttpGet("excel/template"),Authorize(Policy=ApartmentsAuthorizationPolicies.Manage)]
    [ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> ExcelTemplate([FromServices]IDataProtectionProvider protection,CancellationToken ct)=>File(await new ApartmentExcelImport(db,floorLimit,clock,protection).TemplateAsync(ct),XlsxTable.ContentType,"PropFlow_MauNhapCanHo.xlsx");

    [HttpPost("excel/preview"),Authorize(Policy=ApartmentsAuthorizationPolicies.Manage)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(XlsxTable.MaximumFileBytes+65536),RequestFormLimits(MultipartBodyLengthLimit=XlsxTable.MaximumFileBytes+65536)]
    [ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<ActionResult<ApartmentExcelPreview>> Preview(IFormFile? file,[FromServices]IDataProtectionProvider protection,CancellationToken ct)
    {
        if(file is null||!file.FileName.EndsWith(".xlsx",StringComparison.OrdinalIgnoreCase)||file.Length is <1 or >XlsxTable.MaximumFileBytes)return BadRequest(new ProblemDetails {Status=400,Title="Chọn file .xlsx không quá 2 MB."});
        if(Actor() is not {} actor)return Unauthorized();
        if(!Guid.TryParse(User.FindFirst("sid")?.Value,out var session)||session==Guid.Empty)return Unauthorized();
        using var limit=CancellationTokenSource.CreateLinkedTokenSource(ct);limit.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            await using var stream=new MemoryStream();await file.CopyToAsync(stream,limit.Token);
            return Ok(await new ApartmentExcelImport(db,floorLimit,clock,protection).PreviewAsync(stream.ToArray(),actor,session,limit.Token));
        }
        catch(XlsxFormatException error){return BadRequest(new ProblemDetails {Status=400,Title=error.Message});}
        catch(OperationCanceledException) when(!ct.IsCancellationRequested){return StatusCode(408,new ProblemDetails {Status=408,Title="Kiểm tra file quá thời gian. Vui lòng giảm số dòng."});}
    }
    [HttpPost("excel/commit"),Authorize(Policy=ApartmentsAuthorizationPolicies.Manage)]
    [RequestSizeLimit(3*1024*1024)]
    [ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<ActionResult<ApartmentExcelResult>> ExcelCommit(CommitApartmentExcel request,[FromServices]IDataProtectionProvider protection,CancellationToken ct)
    {
        if(Actor() is not {} actor)return Unauthorized();
        if(!Guid.TryParse(User.FindFirst("sid")?.Value,out var session)||session==Guid.Empty)return Unauthorized();
        using var limit=CancellationTokenSource.CreateLinkedTokenSource(ct);limit.CancelAfter(TimeSpan.FromSeconds(30));
        try {return Ok(await new ApartmentExcelImport(db,floorLimit,clock,protection).CommitAsync(request.Ticket,actor,session,limit.Token));}
        catch(ApartmentCreationException error){return StatusCode(error.Status,new ProblemDetails {Status=error.Status,Title=error.Message,Extensions={["code"]=error.Code}});}
        catch(OperationCanceledException) when(!ct.IsCancellationRequested){return StatusCode(408,new ProblemDetails {Status=408,Title="Nhập file quá thời gian. Vui lòng xem trước lại file."});}
    }
}

