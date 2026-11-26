using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;
using PropFlow.Modules.Apartments.Infrastructure;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Shared.Spreadsheets;

namespace PropFlow.UnitTests;

[Trait("UseCase","FE-02/FE-03 Excel directory extension")]
public sealed class DirectoryExcelTests
{
    private static readonly XNamespace Ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    [Theory]
    [InlineData("=HYPERLINK(\"https://example.invalid\")")]
    [InlineData("+SUM(1,2)")]
    [InlineData("-1+2")]
    [InlineData("@SUM(A1)")]
    [InlineData("000012345678")]
    [InlineData("Nguyễn Thị Ánh — căn hộ 01")]
    [InlineData("Căn hộ 🧪")]
    public void Export_preserves_text_and_cannot_create_formula_cells(string value)
    {
        var bytes=XlsxTable.Write(new XlsxSheet("DuLieu",[new("Giá trị")],[new object?[]{value}]));
        var table=XlsxTable.Read(bytes,["Giá trị"]);
        Assert.Equal(value,table.Rows[0].Cells[0]);
        using var zip=new ZipArchive(new MemoryStream(bytes));
        using var source=zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();var sheet=XDocument.Load(source);
        Assert.Empty(sheet.Descendants(Ns+"f"));Assert.NotEmpty(sheet.Descendants(Ns+"autoFilter"));
        Assert.Equal("frozen",(string?)sheet.Descendants(Ns+"pane").Single().Attribute("state"));
        Assert.All(sheet.Descendants(Ns+"c"),cell=>Assert.Equal("inlineStr",(string?)cell.Attribute("t")));
    }
    [Fact]
    public void Workbook_has_consistent_explicit_left_alignment_without_external_workbooks_or_open_actions()
    {
        var bytes=XlsxTable.Write(new XlsxSheet("Báo cáo",[new("Mã"),new("Số",Format:"integer")],[new object?[]{"0001",12}]));
        using var zip=new ZipArchive(new MemoryStream(bytes));
        using var styles=zip.GetEntry("xl/styles.xml")!.Open();var styleXml=XDocument.Load(styles);
        Assert.All(styleXml.Descendants(Ns+"alignment"),x=>Assert.Equal("left",(string?)x.Attribute("horizontal")));
        Assert.DoesNotContain(zip.Entries,x=>x.FullName.Contains("external",StringComparison.OrdinalIgnoreCase)||x.FullName.Contains("vba",StringComparison.OrdinalIgnoreCase));
        using var workbook=zip.GetEntry("xl/workbook.xml")!.Open();var workbookXml=XDocument.Load(workbook);
        Assert.Empty(workbookXml.Descendants(Ns+"externalReferences"));Assert.Empty(workbookXml.Descendants(Ns+"definedName"));
    }
    [Fact]
    public void Report_rows_expand_to_show_multiple_owners_or_residents_on_separate_lines()
    {
        var bytes=XlsxTable.Write(new XlsxSheet("Căn hộ",[new("Chủ sở hữu",32)],[new object?[]{"Nguyễn An\nTrần Bình\nLê Chi"}]));
        using var zip=new ZipArchive(new MemoryStream(bytes));using var source=zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var sheet=XDocument.Load(source);var row=sheet.Descendants(Ns+"row").Single(x=>(int)x.Attribute("r")! == 2);
        Assert.True((double)row.Attribute("ht")!>=53,"Three names need three readable lines, not a single fixed-height row.");
    }
    private static byte[] Workbook(params object?[][] rows)=>XlsxTable.Write(new XlsxSheet(ApartmentExcelImport.DataSheet,ApartmentExcelImport.Headers.Select(x=>new XlsxColumn(x)).ToArray(),rows));
    private static object?[] Valid(string number="DEMO-01",object? date=null)=>[number,2,"Loại A",75.5m,2,2,date,"Mô tả tổng hợp","ACTIVE"];
    [Fact]
    public void Reader_rejects_invalid_headers_bad_zip_extra_columns_and_row_limit()
    {
        Assert.Throws<XlsxFormatException>(()=>XlsxTable.Read(Encoding.UTF8.GetBytes("not zip"),ApartmentExcelImport.Headers));
        Assert.Throws<XlsxFormatException>(()=>XlsxTable.Read(Workbook(Valid()),["Sai tên cột"],sheetName:ApartmentExcelImport.DataSheet));
        Assert.Throws<XlsxFormatException>(()=>XlsxTable.Read(Workbook(Valid().Concat(new object?[]{"extra"}).ToArray()),ApartmentExcelImport.Headers,sheetName:ApartmentExcelImport.DataSheet));
        Assert.Throws<XlsxFormatException>(()=>XlsxTable.Read(Workbook(Enumerable.Range(1,201).Select(x=>Valid("D-"+x)).ToArray()),ApartmentExcelImport.Headers,sheetName:ApartmentExcelImport.DataSheet));
    }
    [Theory]
    [InlineData("formula")]
    [InlineData("doctype")]
    [InlineData("external")]
    [InlineData("macro")]
    public void Reader_rejects_active_or_unsafe_workbooks(string kind)
    {
        using var stream=new MemoryStream();stream.Write(Workbook(Valid()));
        using(var zip=new ZipArchive(stream,ZipArchiveMode.Update,true))
        {
            var entry=zip.GetEntry("xl/worksheets/sheet1.xml")!;
            if(kind=="formula"||kind=="doctype")
            {
                XDocument doc;using(var input=entry.Open())doc=XDocument.Load(input);entry.Delete();
                if(kind=="formula")doc.Descendants(Ns+"c").Last().Add(new XElement(Ns+"f","1+1"));
                using var writer=new StreamWriter(zip.CreateEntry("xl/worksheets/sheet1.xml").Open());
                if(kind=="doctype")writer.Write("<!DOCTYPE worksheet [<!ENTITY x SYSTEM 'file:///must-not-read'>]>");
                writer.Write(doc.ToString());
            }
            else {using var writer=new StreamWriter(zip.CreateEntry(kind=="macro"?"xl/vbaProject.bin":"xl/externalLinks/externalLink1.xml").Open());writer.Write("blocked");}
        }
        Assert.Throws<XlsxFormatException>(()=>XlsxTable.Read(stream.ToArray(),ApartmentExcelImport.Headers,sheetName:ApartmentExcelImport.DataSheet));
    }
    [Fact]
    public async Task Preview_aggregates_field_errors_and_duplicates_without_writes()
    {
        await using var db=new ApartmentsDbContext(new DbContextOptionsBuilder<ApartmentsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var type=new ApartmentUnitType("Loại A",DateTimeOffset.UtcNow);db.ApartmentUnitTypes.Add(type);
        db.ApartmentUnits.Add(new ApartmentUnit("EXISTS",1,DateTimeOffset.UtcNow,type.Id));await db.SaveChangesAsync();
        var service=new ApartmentExcelImport(db,new Floors(),TimeProvider.System,new EphemeralDataProtectionProvider());
        var bad=Valid("EXISTS");bad[1]=99;bad[3]="25.123";bad[6]="31/02/2026";bad[8]="UNKNOWN";
        var invalid=Valid("INVALID");invalid[3]="bad area";
        var preview=await service.PreviewAsync(Workbook(bad,Valid("EXISTS"),invalid),Guid.NewGuid(),Guid.NewGuid(),default);
        Assert.Null(preview.Ticket);Assert.Equal(0,preview.ValidRows);Assert.Equal(2,preview.DuplicateRows);Assert.Equal(1,preview.ErrorRows);
        Assert.True(preview.Rows[0].Issues.Count>=5);Assert.Equal(2,preview.Rows[0].ExcelRow);
        Assert.Equal(preview.TotalRows,preview.ValidRows+preview.ErrorRows+preview.DuplicateRows);
        Assert.Single(db.ApartmentUnits);Assert.Empty(db.ChangeTracker.Entries().Where(x=>x.State==EntityState.Added));
    }
    [Theory]
    [InlineData("24/11/2026","75,50","ACTIVE",true)]
    [InlineData("2026-11-24","75.50","INACTIVE",true)]
    [InlineData("31/02/2026","75.50","ACTIVE",false)]
    [InlineData("24/11/2026","75.501","ACTIVE",false)]
    [InlineData("24/11/2026","24.99","ACTIVE",false)]
    [InlineData("24/11/2026","75.50","UNKNOWN",false)]
    public async Task Preview_parses_dates_decimals_and_enums(string date,string area,string status,bool expected)
    {
        await using var db=new ApartmentsDbContext(new DbContextOptionsBuilder<ApartmentsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.ApartmentUnitTypes.Add(new ApartmentUnitType("Loại A",DateTimeOffset.UtcNow));await db.SaveChangesAsync();
        var row=Valid(date:date);row[3]=area;row[8]=status;
        var preview=await new ApartmentExcelImport(db,new Floors(),TimeProvider.System,new EphemeralDataProtectionProvider()).PreviewAsync(Workbook(row),Guid.NewGuid(),Guid.NewGuid(),default);
        Assert.Equal(expected,preview.Ticket is not null);Assert.Empty(db.ApartmentUnits);
    }
    private sealed class Floors:ICurrentBuildingFloorLimit {public Task<int?> GetMaximumFloorAsync(CancellationToken ct)=>Task.FromResult<int?>(30);}

    [Theory]
    [InlineData("worksheet-external-link")]
    [InlineData("guide-formula")]
    public void Reader_rejects_external_links_and_formulas_even_outside_the_data_cells(string attack)
    {
        var bytes=XlsxTable.Write(new XlsxSheet(ApartmentExcelImport.DataSheet,ApartmentExcelImport.Headers.Select(x=>new XlsxColumn(x)).ToArray(),[Valid()]),new XlsxSheet("Hướng dẫn",[new("Mục")],[new object?[]{"Chỉ dữ liệu thuần"}]));
        using var stream=new MemoryStream();stream.Write(bytes);
        using(var zip=new ZipArchive(stream,ZipArchiveMode.Update,true))
        {
            if(attack=="worksheet-external-link")
            {
                using var writer=new StreamWriter(zip.CreateEntry("xl/worksheets/_rels/sheet1.xml.rels").Open());
                writer.Write("<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='external' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink' Target='https://example.invalid/' TargetMode='External'/></Relationships>");
            }
            else
            {
                var entry=zip.GetEntry("xl/worksheets/sheet2.xml")!;XDocument doc;using(var input=entry.Open())doc=XDocument.Load(input);entry.Delete();
                var cell=doc.Descendants(Ns+"c").Last();cell.SetAttributeValue("t",null);cell.RemoveNodes();cell.Add(new XElement(Ns+"f","1+1"),new XElement(Ns+"v","2"));
                using var writer=new StreamWriter(zip.CreateEntry("xl/worksheets/sheet2.xml").Open());writer.Write(doc.ToString());
            }
        }
        Assert.Throws<XlsxFormatException>(()=>XlsxTable.Read(stream.ToArray(),ApartmentExcelImport.Headers,sheetName:ApartmentExcelImport.DataSheet));
    }

    [Fact]
    public async Task Template_has_separate_Vietnamese_guide_typed_columns_and_status_dropdown()
    {
        await using var db=new ApartmentsDbContext(new DbContextOptionsBuilder<ApartmentsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var bytes=await new ApartmentExcelImport(db,new Floors(),TimeProvider.System,new EphemeralDataProtectionProvider()).TemplateAsync(default);
        using var zip=new ZipArchive(new MemoryStream(bytes));
        XDocument Xml(string name){using var stream=zip.GetEntry(name)!.Open();return XDocument.Load(stream);}
        Assert.Equal(new[]{"Hướng dẫn","Danh sách căn hộ"},Xml("xl/workbook.xml").Descendants(Ns+"sheet").Select(x=>(string)x.Attribute("name")!));
        var sheet=Xml("xl/worksheets/sheet2.xml");
        Assert.Equal("I2:I201",(string?)sheet.Descendants(Ns+"dataValidation").Single().Attribute("sqref"));
        Assert.Equal("\"ACTIVE,INACTIVE\"",sheet.Descendants(Ns+"formula1").Single().Value);
        Assert.Equal("A2",(string?)sheet.Descendants(Ns+"pane").Single().Attribute("topLeftCell"));
        Assert.Equal("A1:I1",(string?)sheet.Descendants(Ns+"autoFilter").Single().Attribute("ref"));
        Assert.Equal("1",(string?)sheet.Descendants(Ns+"col").First().Attribute("style"));
        Assert.Empty(db.ApartmentUnits);
    }

    [Theory]
    [InlineData("renamed")]
    [InlineData("missing-column")]
    [InlineData("reordered-column")]
    [InlineData("guide-only")]
    public void Reader_rejects_structural_edits_without_reading_guide_as_data(string edit)
    {
        var columns=ApartmentExcelImport.Headers.ToList();
        if(edit=="missing-column")columns.RemoveAt(8);
        if(edit=="reordered-column")(columns[0],columns[1])=(columns[1],columns[0]);
        var name=edit=="renamed"?"Sai tên":edit=="guide-only"?"Hướng dẫn":ApartmentExcelImport.DataSheet;
        var bytes=XlsxTable.Write(new XlsxSheet(name,columns.Select(x=>new XlsxColumn(x)).ToArray(),[Valid().Take(columns.Count).ToArray()]));
        Assert.Throws<XlsxFormatException>(()=>XlsxTable.Read(bytes,ApartmentExcelImport.Headers,sheetName:ApartmentExcelImport.DataSheet));
    }
}
