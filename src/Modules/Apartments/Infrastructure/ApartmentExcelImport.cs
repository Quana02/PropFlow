using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Shared.Spreadsheets;

namespace PropFlow.Modules.Apartments.Infrastructure;

public sealed class ApartmentExcelImport(ApartmentsDbContext db,ICurrentBuildingFloorLimit floorLimit,TimeProvider clock,IDataProtectionProvider protection)
{
    public const int MaximumRows=200;
    public const string DataSheet="Danh sách căn hộ";
    public static readonly string[] Headers=["Mã căn hộ *","Tầng *","Loại căn hộ *","Diện tích (m²) *","Số phòng ngủ","Số phòng tắm","Ngày bàn giao","Mô tả","Trạng thái *"];
    private ITimeLimitedDataProtector Protector=>protection.CreateProtector("PropFlow.Apartments.Excel.CreateOnly.v1").ToTimeLimitedDataProtector();
    private sealed record Payload(Guid Actor,Guid Session,IReadOnlyList<XlsxRow> Rows,bool Date1904);

    public async Task<byte[]> TemplateAsync(CancellationToken ct)
    {
        var types=await db.ApartmentUnitTypes.AsNoTracking().OrderBy(x=>x.Name).Select(x=>x.Name).Take(1000).ToArrayAsync(ct);
        List<object?[]> guide = [
            ["Phạm vi","Chỉ tạo căn hộ mới. Không nhập chủ sở hữu, người cư trú hoặc dữ liệu tài chính."],
            ["Cột bắt buộc","Các cột có dấu * và header cyan. Cột tùy chọn có header xanh nhạt hơn. Giữ nguyên tên và thứ tự cột, giữ sheet Danh sách căn hộ."],
            ["Giới hạn","Tối đa 200 dòng, 2 MB. Tất cả dòng phải hợp lệ; có lỗi hoặc trùng sẽ không nhập dòng nào."],
            ["Mã căn hộ","Tối đa 30 ký tự, chưa tồn tại. Mã căn hộ được giữ dưới dạng text."],
            ["Tầng","Số nguyên từ 0 đến số tầng chung cư đang cấu hình."],
            ["Loại căn hộ","Nhập đúng tên loại căn hộ hiện có ở cuối hướng dẫn, không nhập UUID; không tự tạo loại mới."],
            ["Diện tích","Từ 25 m², tối đa 2 chữ số thập phân. Dùng 75.5 hoặc 75,5, không dùng dấu phân cách hàng nghìn."],
            ["Số phòng","Số nguyên không âm, có thể để trống."],
            ["Ngày bàn giao","Để trống hoặc dd/MM/yyyy, yyyy-MM-dd, hoặc ô ngày Excel không kèm giờ."],
            ["Trạng thái","ACTIVE hoặc INACTIVE."],
            ["Ví dụ tổng hợp","DEMO-2901 | 2 | tên loại hiện có | 75.5 | 2 | 2 | 24/11/2026 | Căn hộ mẫu tổng hợp | ACTIVE"],
            ["Bảo mật","Không dùng công thức, macro hoặc liên kết ngoài. File chỉ xử lý trong bộ nhớ; phiên xem trước hết hạn sau 10 phút."]
        ];
        guide.AddRange(types.Select(x=>new object?[]{"Loại căn hộ hiện có",x}));
        var columns=Headers.Select((x,i)=>new XlsxColumn(x,i==7?45:26,i switch {1 or 4 or 5=>"integer",3=>"decimal",6=>"date",_=>"text"},i==8?["ACTIVE","INACTIVE"]:null)).ToArray();
        return XlsxTable.Write(new XlsxSheet("Hướng dẫn",[new("Mục",25),new("Hướng dẫn",95)],guide),new XlsxSheet(DataSheet,columns,[],InputRows:MaximumRows));
    }

    public async Task<ApartmentExcelPreview> PreviewAsync(byte[] bytes,Guid actor,Guid session,CancellationToken ct)
    {
        var input=XlsxTable.Read(bytes,Headers,MaximumRows,DataSheet);
        var validation=await ValidateAsync(input,ct);
        var errors=validation.Rows.Count(x=>x.Issues.Count>0&&!x.Issues.Any(i=>i.Duplicate));
        var duplicates=validation.Rows.Count(x=>x.Issues.Any(i=>i.Duplicate));
        var valid=validation.Rows.Count(x=>x.Issues.Count==0);
        var ticket=valid==input.Rows.Count?Protector.Protect(JsonSerializer.Serialize(new Payload(actor,session,input.Rows,input.Date1904)),TimeSpan.FromMinutes(10)):null;
        return new(input.Rows.Count,valid,errors,duplicates,Headers,validation.Rows,ticket,ticket is null?null:clock.GetUtcNow().AddMinutes(10));
    }
    public async Task<ApartmentExcelResult> CommitAsync(string ticket,Guid actor,Guid session,CancellationToken ct)
    {
        Payload payload;
        try {payload=JsonSerializer.Deserialize<Payload>(Protector.Unprotect(ticket,out _))??throw new CryptographicException();}
        catch(Exception e) when(e is CryptographicException or JsonException or FormatException or ArgumentException)
        {throw new ApartmentCreationException(409,"EXCEL_PREVIEW_EXPIRED","Phiên xem trước hết hạn hoặc không hợp lệ. Vui lòng chọn lại file.");}
        if(payload.Actor!=actor)throw new ApartmentCreationException(403,"EXCEL_PREVIEW_ACTOR_MISMATCH","Phiên xem trước thuộc tài khoản khác.");
        if(session==Guid.Empty||payload.Session!=session)throw new ApartmentCreationException(403,"EXCEL_PREVIEW_SESSION_MISMATCH","Phiên xem trước thuộc lần đăng nhập khác. Vui lòng chọn lại file.");
        if(payload.Rows.Count is <1 or >MaximumRows)throw new ApartmentCreationException(400,"EXCEL_ROWS_INVALID","Số dòng không hợp lệ.");
        await using var transaction=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71241006)",ct);
        var validation=await ValidateAsync(new(payload.Rows,payload.Date1904),ct);
        if(validation.Rows.Any(x=>x.Issues.Count>0))throw new ApartmentCreationException(409,"EXCEL_DATA_CHANGED","Dữ liệu đã thay đổi hoặc mã căn hộ đã tồn tại. Không có dòng nào được nhập. Vui lòng xem trước lại file.");
        var creation=new ApartmentCreation(db,floorLimit,clock);
        foreach(var command in validation.Commands)await creation.StageAsync(command,actor,ct);
        await db.SaveChangesAsync(ct);await transaction.CommitAsync(ct);
        return new(validation.Commands.Count);
    }
    private async Task<(IReadOnlyList<ApartmentExcelPreviewRow> Rows,IReadOnlyList<NewApartment> Commands)> ValidateAsync(XlsxInput input,CancellationToken ct)
    {
        var types=await db.ApartmentUnitTypes.AsNoTracking().Select(x=>new{x.Id,x.Name}).ToArrayAsync(ct);
        var numbers=input.Rows.Select(x=>x.Cells[0].Trim()).ToArray();
        var existing=await db.ApartmentUnits.AsNoTracking().Where(x=>numbers.Contains(x.UnitNumber)).Select(x=>x.UnitNumber).ToArrayAsync(ct);
        var repeats=numbers.GroupBy(x=>x,StringComparer.Ordinal).Where(x=>x.Count()>1).Select(x=>x.Key).ToHashSet(StringComparer.Ordinal);
        var maximum=await floorLimit.GetMaximumFloorAsync(ct);
        var rows=new List<ApartmentExcelPreviewRow>();var commands=new List<NewApartment>();
        foreach(var row in input.Rows)
        {
            ct.ThrowIfCancellationRequested();
            var values=row.Cells.Select(x=>x.Trim()).ToArray();var issues=new List<ApartmentExcelIssue>();
            void Error(int col,string message,bool duplicate=false)=>issues.Add(new(Headers[col],message,duplicate));
            int? Integer(int col,bool required)
            {
                if(values[col].Length==0&&!required)return null;
                if(!int.TryParse(values[col],NumberStyles.None,CultureInfo.InvariantCulture,out var n)){Error(col,"Nhập số nguyên không âm.");return null;}return n;
            }
            var floor=Integer(1,true);var bedrooms=Integer(4,false);var bathrooms=Integer(5,false);
            var matches=types.Where(x=>string.Equals(x.Name.Trim(),values[2],StringComparison.OrdinalIgnoreCase)).ToArray();
            if(matches.Length!=1)Error(2,matches.Length==0?"Không tìm thấy loại căn hộ. Dùng tên trong sheet Hướng dẫn.":"Tên loại căn hộ không xác định duy nhất. Vui lòng liên hệ quản lý.");
            var areaText=values[3].Contains(',')&&!values[3].Contains('.')?values[3].Replace(',','.'):values[3];
            if(!decimal.TryParse(areaText,NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var area)||area<25||area>99999999.99m||decimal.Round(area,2)!=area)Error(3,"Diện tích từ 25 m², tối đa 99.999.999,99 và 2 chữ số thập phân.");
            if(values[0].Length is <1 or >30||values[0].Any(char.IsControl))Error(0,"Mã căn hộ bắt buộc, tối đa 30 ký tự, không có ký tự điều khiển.");
            if(maximum is null or <=0)Error(1,"Chưa thiết lập số tầng chung cư.");
            else if(floor>maximum)Error(1,$"Tầng không được vượt quá {maximum}.");
            if(repeats.Contains(values[0]))Error(0,"Mã căn hộ lặp trong file.",true);
            if(existing.Contains(values[0],StringComparer.Ordinal))Error(0,"Mã căn hộ đã tồn tại, không được ghi đè.",true);
            var status=values[8].ToUpperInvariant();if(status is not("ACTIVE" or "INACTIVE"))Error(8,"Chỉ nhập ACTIVE hoặc INACTIVE.");
            DateOnly? handover=null;
            if(values[6].Length>0)
            {
                if(DateOnly.TryParseExact(values[6],["dd/MM/yyyy","yyyy-MM-dd"],CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))handover=date;
                else if(double.TryParse(values[6],NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var serial)&&serial>=1&&serial<=2958465&&serial==Math.Floor(serial))
                {
                    try {handover=DateOnly.FromDateTime(DateTime.FromOADate(serial+(input.Date1904?1462:0)));}catch(ArgumentException){Error(6,"Ngày Excel nằm ngoài phạm vi hợp lệ.");}
                }
                else Error(6,"Nhập dd/MM/yyyy, yyyy-MM-dd hoặc ô ngày Excel không kèm giờ.");
            }
            rows.Add(new(row.ExcelRow,values,issues));
            if(issues.Count==0)commands.Add(new(values[0],floor!.Value,matches[0].Id,area,bedrooms,bathrooms,handover,string.IsNullOrWhiteSpace(values[7])?null:values[7],status));
        }
        return(rows,commands);
    }
}

