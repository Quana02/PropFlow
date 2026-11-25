using System.Data.Common;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;
using PropFlow.Modules.Administration.Infrastructure;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;
using PropFlow.Modules.Apartments.Infrastructure;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Domain.ResidentApartments;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Presentation;
using PropFlow.Shared.Spreadsheets;
using System.Xml.Linq;

namespace PropFlow.IntegrationTests;

[Trait("UseCase","FE-02/FE-03 Excel directory extension")]
public sealed class DirectoryExcelHttpTests:IAsyncLifetime
{
    private readonly AuthDatabaseFixture database=new();
    private readonly Dictionary<HttpClient,AccountResponse> issuedAccounts=new();
    private const string Preview="/api/v1/apartments/excel/preview",Commit="/api/v1/apartments/excel/commit";
    private static IReadOnlyList<XlsxRow> ReadReport(byte[] bytes,string[] headers,string name)
    {
        using var zip=new ZipArchive(new MemoryStream(bytes));
        using var workbook=zip.GetEntry("xl/workbook.xml")!.Open();var book=XDocument.Load(workbook);
        Assert.Equal(name,(string?)Assert.Single(book.Descendants(SheetNs+"sheet")).Attribute("name"));
        using var source=zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();var sheet=XDocument.Load(source);
        string Cell(XElement cell)=>cell.Element(SheetNs+"v")?.Value??string.Concat(cell.Descendants(SheetNs+"t").Select(x=>x.Value));
        Assert.Equal(headers,sheet.Descendants(SheetNs+"row").Single(x=>(int)x.Attribute("r")! ==5).Elements(SheetNs+"c").Select(Cell));
        return sheet.Descendants(SheetNs+"row").Where(x=>(int)x.Attribute("r")!>5).Select(x=>new XlsxRow((int)x.Attribute("r")!,x.Elements(SheetNs+"c").Select(Cell).ToArray())).ToArray();
    }
    public Task InitializeAsync()=>database.InitializeAsync();
    public Task DisposeAsync()=>database.DisposeAsync();
    private async Task<HttpClient> Actor(string role,WebApplicationFactory<Program>? host=null)
    {
        host??=database.Factory;using var scope=host.Services.CreateScope();var services=scope.ServiceProvider;
        var auth=services.GetRequiredService<AuthenticationDbContext>();var access=services.GetRequiredService<AdministrationDbContext>();var now=DateTimeOffset.UtcNow;
        var user=new UserAccount("excel_"+Guid.NewGuid().ToString("N")[..12],"fixture-hash","Excel fixture "+role,now,status:AccountStatus.ACTIVE,emailVerified:true);
        auth.UserAccounts.Add(user);await auth.SaveChangesAsync();
        var assigned=await access.Roles.Include(x=>x.RolePermissions).ThenInclude(x=>x.Permission).SingleAsync(x=>x.Code==role);
        access.UserRoleAssignments.Add(new UserRoleAssignment(user.Id,assigned.Id,now));await access.SaveChangesAsync();
        var account=new AccountResponse(user.Id,user.Username,user.DisplayName,null,null,"ACTIVE",true,role,assigned.RolePermissions.Select(x=>x.Permission.Code).ToArray());
        var client=host.CreateClient(new(){BaseAddress=new Uri("https://localhost")});
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",(await AuthenticationTestSessions.IssueAsync(services,account,now)).AccessToken);issuedAccounts[client]=account;return client;
    }
    private async Task<string> Type()
    {
        using var scope=database.Factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>();
        var type=new ApartmentUnitType("Loại Excel "+Guid.NewGuid().ToString("N")[..5],DateTimeOffset.UtcNow);db.ApartmentUnitTypes.Add(type);await db.SaveChangesAsync();return type.Name;
    }
    private static byte[] Workbook(string type,params string[] numbers)=>XlsxTable.Write(new XlsxSheet(ApartmentExcelImport.DataSheet,ApartmentExcelImport.Headers.Select(x=>new XlsxColumn(x)).ToArray(),numbers.Select(x=>new object?[]{x,2,type,75.5m,2,2,new DateOnly(2026,11,24),"Mô tả thử nghiệm tổng hợp","ACTIVE"}).ToArray()));
    private static async Task<HttpResponseMessage> Upload(HttpClient client,byte[] bytes)
    {
        using var form=new MultipartFormDataContent();form.Add(new ByteArrayContent(bytes),"file","fixture.xlsx");return await client.PostAsync(Preview,form);
    }
    [Fact]
    public async Task Apartment_multi_select_filters_preserve_legacy_count_summary_and_export()
    {
        using var manager=await Actor("MANAGER");using var accountant=await Actor("ACCOUNTANT");using var resident=await Actor("RESIDENT");
        Guid typeOne,typeTwo;
        using(var scope=database.Factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>();var people=scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();var now=DateTimeOffset.UtcNow;var today=DateOnly.FromDateTime(now.UtcDateTime);
            var one=new ApartmentUnitType("Multi type one",now,75m,2,2);var two=new ApartmentUnitType("Multi type two",now,90m,2,2);db.ApartmentUnitTypes.AddRange(one,two);await db.SaveChangesAsync();typeOne=one.Id;typeTwo=two.Id;
            var a=new ApartmentUnit("MULTI-A",2,now,one.Id,75m);var b=new ApartmentUnit("MULTI-B",3,now,two.Id,90m);var c=new ApartmentUnit("MULTI-C",4,now,one.Id,60m);var d=new ApartmentUnit("MULTI-D",2,now,two.Id,100m);c.Deactivate(null,now);db.ApartmentUnits.AddRange(a,b,c,d);await db.SaveChangesAsync();
            var owner=new Resident("MULTI-P1","Cư dân kiểm thử Một",now);var tenant=new Resident("MULTI-P2","Cư dân kiểm thử Hai",now);people.Residents.AddRange(owner,tenant);await people.SaveChangesAsync();
            db.ApartmentOwnerships.AddRange(new ApartmentOwnership(a.Id,owner.Id,today.AddDays(-20),now,null),new ApartmentOwnership(a.Id,tenant.Id,today.AddDays(-20),now,null),new ApartmentOwnership(d.Id,owner.Id,today.AddDays(-20),now,null));await db.SaveChangesAsync();
            people.ResidentApartments.AddRange(new ResidentApartment(owner.Id,a.Id,HouseholdRole.HOUSEHOLD_HEAD,ResidencyType.OWNER_OCCUPIED,today.AddDays(-10),now),new ResidentApartment(tenant.Id,d.Id,HouseholdRole.HOUSEHOLD_HEAD,ResidencyType.TENANT,today.AddDays(-10),now));await people.SaveChangesAsync();
        }
        var headers=new[]{"Mã căn hộ","Tầng","Loại căn hộ","Diện tích (m²)","Chủ sở hữu","Cư dân đang cư trú","Trạng thái","Tình trạng cư trú"};
        var cases=new (string Query,string[] Expected)[]{
            ("statuses=INACTIVE",["MULTI-C"]),
            ("statuses=ACTIVE&statuses=INACTIVE",["MULTI-A","MULTI-D","MULTI-B","MULTI-C"]),
            ("floorNumbers=2&floorNumbers=3",["MULTI-A","MULTI-D","MULTI-B"]),
            ($"apartmentUnitTypeIds={typeOne}&apartmentUnitTypeIds={typeTwo}",["MULTI-A","MULTI-D","MULTI-B","MULTI-C"]),
            ($"apartmentUnitTypeIds={typeOne}",["MULTI-A","MULTI-C"]),
            ($"apartmentUnitTypeId={typeOne}&apartmentUnitTypeIds={typeTwo}",["MULTI-A","MULTI-D","MULTI-B","MULTI-C"]),
            ("floorNumber=2&floorNumbers=3",["MULTI-A","MULTI-D","MULTI-B"]),
            ("status=INACTIVE&statuses=ACTIVE",["MULTI-A","MULTI-D","MULTI-B","MULTI-C"]),
            ($"statuses=ACTIVE&floorNumbers=2&floorNumbers=4&apartmentUnitTypeIds={typeOne}&apartmentUnitTypeIds={typeTwo}",["MULTI-A","MULTI-D"]),
            ($"floorNumbers=2&floorNumbers=3&apartmentUnitTypeIds={typeOne}",["MULTI-A"]),
            ($"floorNumbers=4&apartmentUnitTypeIds={typeTwo}",[]),
            ($"statuses=ACTIVE&floorNumbers=2&apartmentUnitTypeIds={typeTwo}",["MULTI-D"]),
            ($"statuses=ACTIVE&statuses=INACTIVE&floorNumbers=2&floorNumbers=3&floorNumbers=4&apartmentUnitTypeIds={typeOne}&apartmentUnitTypeIds={typeTwo}&minArea=60&maxArea=90",["MULTI-A","MULTI-B","MULTI-C"]),
            ("floorNumbers=2&floorNumbers=3&floorNumbers=4&hasOwner=false",["MULTI-B","MULTI-C"]),
            ("floorNumbers=2&floorNumbers=3&occupancy=OCCUPIED",["MULTI-A","MULTI-D"]),
            ("floorNumbers=2&floorNumbers=2&statuses=ACTIVE&statuses=ACTIVE",["MULTI-A","MULTI-D"]),
            ($"apartmentUnitTypeIds={Guid.NewGuid()}",[]),
            ($"apartmentUnitTypeId={Guid.Empty}",[])
        };
        var before=await Facts();
        foreach(var (query,expected) in cases)
        {
            using var list=JsonDocument.Parse(await manager.GetStringAsync("/api/v1/apartments?"+query));var body=list.RootElement;
            var actual=body.GetProperty("items").EnumerateArray().Select(x=>x.GetProperty("unitNumber").GetString()).ToArray();Assert.Equal(expected,actual);Assert.Equal(expected.Length,body.GetProperty("totalCount").GetInt32());Assert.Equal(actual.Length,actual.Distinct().Count());
            var summary=body.GetProperty("summary");var occupied=expected.Count(x=>x is "MULTI-A" or "MULTI-D");Assert.Equal(expected.Length,summary.GetProperty("totalCount").GetInt32());Assert.Equal(occupied,summary.GetProperty("occupiedCount").GetInt32());Assert.Equal(expected.Length-occupied,summary.GetProperty("vacantCount").GetInt32());Assert.Equal(expected.Contains("MULTI-C")?1:0,summary.GetProperty("inactiveCount").GetInt32());
            using var file=await manager.GetAsync("/api/v1/apartments/excel/export?"+query);Assert.Equal(HttpStatusCode.OK,file.StatusCode);Assert.Equal(expected,ReadReport(await file.Content.ReadAsByteArrayAsync(),headers,"Danh sách căn hộ").Select(x=>x.Cells[0]));
        }
        Assert.Equal(new[]{2,3,4},await manager.GetFromJsonAsync<int[]>("/api/v1/apartments/floors"));
        using(var file=await manager.GetAsync($"/api/v1/apartments/excel/export?statuses=ACTIVE&statuses=INACTIVE&floorNumbers=2&floorNumbers=3&apartmentUnitTypeIds={typeOne}&apartmentUnitTypeIds={typeTwo}"))
        {
            using var zip=new ZipArchive(new MemoryStream(await file.Content.ReadAsByteArrayAsync()));using var stream=zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();var metadata=string.Concat(XDocument.Load(stream).Descendants(SheetNs+"row").Single(x=>(int)x.Attribute("r")! == 3).Descendants(SheetNs+"t").Select(x=>x.Value));foreach(var text in new[]{"Multi type one","Multi type two","Đang quản lý","Ngừng quản lý","2, 3"})Assert.Contains(text,metadata);
        }
        for(var page=1;page<=3;page++)
        {
            using var result=JsonDocument.Parse(await manager.GetStringAsync($"/api/v1/apartments?floorNumbers=2&floorNumbers=3&statuses=ACTIVE&pageSize=1&pageIndex={page}"));Assert.Equal(3,result.RootElement.GetProperty("totalCount").GetInt32());Assert.Equal(new[]{"MULTI-A","MULTI-D","MULTI-B"}[page-1],Assert.Single(result.RootElement.GetProperty("items").EnumerateArray()).GetProperty("unitNumber").GetString());
        }
        foreach(var invalid in new[]{"statuses=99","statuses=unknown","floorNumbers=-1","floorNumbers=2.5","apartmentUnitTypeIds=not-guid","apartmentUnitTypeIds="+Guid.Empty,string.Join('&',Enumerable.Repeat("floorNumbers=2",101))})foreach(var endpoint in new[]{"/api/v1/apartments?","/api/v1/apartments/excel/export?"})Assert.Equal(HttpStatusCode.BadRequest,(await manager.GetAsync(endpoint+invalid)).StatusCode);
        foreach(var endpoint in new[]{"/api/v1/apartments?statuses=ACTIVE&statuses=INACTIVE","/api/v1/apartments/excel/export?floorNumbers=2&floorNumbers=3","/api/v1/apartments/floors"})
        {using var anonymous=database.Factory.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync(endpoint)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await resident.GetAsync(endpoint)).StatusCode);Assert.Equal(HttpStatusCode.OK,(await accountant.GetAsync(endpoint)).StatusCode);}
        Assert.Equal(before,await Facts());
    }

    private static readonly XNamespace SheetNs="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static byte[] Package(byte[] bytes,Action<ZipArchive> change)
    {
        using var stream=new MemoryStream();stream.Write(bytes);
        using(var zip=new ZipArchive(stream,ZipArchiveMode.Update,true))change(zip);
        return stream.ToArray();
    }
    private static byte[] XmlPart(byte[] bytes,string part,Action<XDocument> change)=>Package(bytes,zip=>
    {
        var entry=zip.GetEntry(part)!;XDocument doc;using(var stream=entry.Open())doc=XDocument.Load(stream);
        change(doc);entry.Delete();using var writer=new StreamWriter(zip.CreateEntry(part).Open());writer.Write(doc.ToString());
    });
    private static async Task<ApartmentExcelPreview> ValidPreview(HttpClient client,byte[] bytes)
    {
        using var response=await Upload(client,bytes);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var result=(await response.Content.ReadFromJsonAsync<ApartmentExcelPreview>())!;Assert.NotNull(result.Ticket);return result;
    }
    private async Task<(int Units,int Accounts,int Ownerships,int Residencies,int Audit)> Facts()
    {
        using var scope=database.Factory.Services.CreateScope();var services=scope.ServiceProvider;
        return(await services.GetRequiredService<ApartmentsDbContext>().ApartmentUnits.CountAsync(),
            await services.GetRequiredService<AuthenticationDbContext>().UserAccounts.CountAsync(),
            await services.GetRequiredService<ApartmentsDbContext>().ApartmentOwnerships.CountAsync(),
            await services.GetRequiredService<ResidentsDbContext>().ResidentApartments.CountAsync(),
            await services.GetRequiredService<AdministrationDbContext>().AuditLogs.CountAsync(x=>x.EntityType=="ApartmentUnit"));
    }
    [Fact]
    public async Task Preview_ticket_cannot_be_used_by_another_login_session_of_the_same_manager()
    {
        using var manager=await Actor("MANAGER");var type=await Type();
        using var secondLogin=database.Factory.CreateClient(new(){BaseAddress=new Uri("https://localhost")});
        secondLogin.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",(await AuthenticationTestSessions.IssueAsync(database.Factory.Services,issuedAccounts[manager],DateTimeOffset.UtcNow)).AccessToken);
        var preview=await ValidPreview(manager,Workbook(type,"SESSION-PRIVATE"));var before=await Facts();
        using var response=await secondLogin.PostAsJsonAsync(Commit,new CommitApartmentExcel{Ticket=preview.Ticket!});
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);Assert.Equal(before,await Facts());
        using var original=await manager.PostAsJsonAsync(Commit,new CommitApartmentExcel{Ticket=preview.Ticket!});
        Assert.Equal(HttpStatusCode.OK,original.StatusCode);
    }

    [Fact]
    public async Task Preview_and_exports_do_not_write_business_data_or_success_operation_audit()
    {
        using var manager=await Actor("MANAGER");var type=await Type();var before=await Facts();
        async Task<int> Audits(){using var scope=database.Factory.Services.CreateScope();return await scope.ServiceProvider.GetRequiredService<AdministrationDbContext>().AuditLogs.CountAsync();}
        var auditCount=await Audits();
        await ValidPreview(manager,Workbook(type,"PREVIEW-NO-WRITE"));
        using var apartments=await manager.GetAsync("/api/v1/apartments/excel/export");
        using var residents=await manager.GetAsync("/api/v1/residents/excel/export");
        Assert.Equal(HttpStatusCode.OK,apartments.StatusCode);Assert.Equal(HttpStatusCode.OK,residents.StatusCode);
        Assert.Equal(before,await Facts());Assert.Equal(auditCount,await Audits());
    }

    [Fact]
    public async Task Malformed_empty_oversized_and_unsafe_files_return_Vietnamese_errors_without_writes()
    {
        using var manager=await Actor("MANAGER");var type=await Type();var before=await Facts();
        var valid=Workbook(type,"FILE-VALID");
        var cases=new Dictionary<string,byte[]>
        {
            ["empty-file"]=[],["not-xlsx"]=[1,2,3],["broken-zip"]=[80,75,3,4],
            ["empty-data"]=XlsxTable.Write(new XlsxSheet(ApartmentExcelImport.DataSheet,ApartmentExcelImport.Headers.Select(x=>new XlsxColumn(x)).ToArray(),[])),
            ["wrong-sheet"]=XmlPart(valid,"xl/workbook.xml",doc=>doc.Descendants(SheetNs+"sheet").Single().SetAttributeValue("name","Đã đổi tên")),
            ["wrong-header"]=XmlPart(valid,"xl/worksheets/sheet1.xml",doc=>doc.Descendants(SheetNs+"t").First().Value="Sai header"),
            ["missing-header"]=XmlPart(valid,"xl/worksheets/sheet1.xml",doc=>doc.Descendants(SheetNs+"row").First().Elements().Last().Remove()),
            ["duplicate-header"]=XmlPart(valid,"xl/worksheets/sheet1.xml",doc=>doc.Descendants(SheetNs+"t").First().Value="Tầng *"),
            ["reordered-header"]=XmlPart(valid,"xl/worksheets/sheet1.xml",doc=>{var cells=doc.Descendants(SheetNs+"t").Take(2).ToArray();(cells[0].Value,cells[1].Value)=(cells[1].Value,cells[0].Value);}),
            ["extra-column"]=XmlPart(valid,"xl/worksheets/sheet1.xml",doc=>doc.Descendants(SheetNs+"row").First().Add(new XElement(SheetNs+"c",new XAttribute("r","J1"),new XAttribute("t","inlineStr"),new XElement(SheetNs+"is",new XElement(SheetNs+"t","Thừa"))))),
            ["row-limit"]=Workbook(type,Enumerable.Range(0,201).Select(i=>"LIMIT-"+i).ToArray()),
            ["file-limit"]=new byte[XlsxTable.MaximumFileBytes+1],
            ["formula"]=XmlPart(valid,"xl/worksheets/sheet1.xml",doc=>doc.Descendants(SheetNs+"c").Last().Add(new XElement(SheetNs+"f","1+1"))),
            ["macro"]=Package(valid,zip=>{using var s=zip.CreateEntry("xl/vbaProject.bin").Open();s.WriteByte(1);}),
            ["external-link"]=Package(valid,zip=>{using var w=new StreamWriter(zip.CreateEntry("xl/worksheets/_rels/sheet1.xml.rels").Open());w.Write("<Relationships><Relationship TargetMode='External' Target='https://example.invalid'/></Relationships>");}),
            ["zip-bomb"]=Package(valid,zip=>{using var w=new StreamWriter(zip.CreateEntry("oversized.xml",CompressionLevel.Fastest).Open());w.Write(new string('A',17*1024*1024));}),
            ["entry-limit"]=Package(valid,zip=>{for(var i=0;i<130;i++)zip.CreateEntry("extra/"+i);}),
            ["duplicate-part"]=Package(valid,zip=>{using var w=new StreamWriter(zip.CreateEntry("xl/workbook.xml").Open());w.Write("duplicate");}),
            ["xml-control"]=Package(valid,zip=>{var e=zip.GetEntry("xl/worksheets/sheet1.xml")!;string xml;using(var r=new StreamReader(e.Open()))xml=r.ReadToEnd();e.Delete();using var w=new StreamWriter(zip.CreateEntry("xl/worksheets/sheet1.xml").Open());w.Write(xml.Replace("FILE-VALID","BAD\u0001CODE"));})
        };
        foreach(var sample in cases)
        {
            using var response=await Upload(manager,sample.Value);
            Assert.True(response.StatusCode==HttpStatusCode.BadRequest,$"{sample.Key}: expected 400, got {(int)response.StatusCode}");
            var body=await response.Content.ReadAsStringAsync();Assert.Contains("title",body);Assert.DoesNotContain("StackTrace",body);
            Assert.True(body.Any(c=>c>127),$"{sample.Key}: expected Vietnamese error");Assert.Equal(before,await Facts());
        }
        using var wrongExtension=new MultipartFormDataContent();wrongExtension.Add(new ByteArrayContent(valid),"file","fixture.csv");
        using var rejected=await manager.PostAsync(Preview,wrongExtension);Assert.Equal(HttpStatusCode.BadRequest,rejected.StatusCode);
    }

    [Fact]
    public async Task Invalid_last_row_reports_exact_column_and_never_offers_a_commit_ticket()
    {
        using var manager=await Actor("MANAGER");var type=await Type();var before=await Facts();
        foreach(var (column,value) in new[]{(0,""),(0,"BAD\nCODE"),(1,"-1"),(1,"101"),(1,"1.5"),(2,"Không tồn tại"),(3,"24.99"),(3,"75.501"),(3,"NaN"),(4,"-1"),(5,"1.5"),(6,"31/02/2026"),(8,"UNKNOWN")})
        {
            object?[] good=["FIRST-VALID",2,type,75.5m,2,2,null,"Tiếng Việt 🧪","ACTIVE"];
            object?[] bad=["LAST-INVALID",2,type,75.5m,2,2,null,"Không được nhập một phần","ACTIVE"];bad[column]=value;
            var bytes=XlsxTable.Write(new XlsxSheet(ApartmentExcelImport.DataSheet,ApartmentExcelImport.Headers.Select(x=>new XlsxColumn(x)).ToArray(),[good,bad]));
            using var response=await Upload(manager,bytes);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            var preview=(await response.Content.ReadFromJsonAsync<ApartmentExcelPreview>())!;Assert.Null(preview.Ticket);
            Assert.Equal(1,preview.ValidRows);Assert.Equal(3,preview.Rows[1].ExcelRow);
            Assert.Contains(preview.Rows[1].Issues,x=>x.Column==ApartmentExcelImport.Headers[column]);Assert.Equal(before,await Facts());
        }
        using var bypass=await manager.PostAsJsonAsync(Commit,new{ticket="not-issued",rows=new[]{new{unitNumber="BYPASS"}}});
        Assert.Equal(HttpStatusCode.BadRequest,bypass.StatusCode);Assert.Equal(before,await Facts());
    }

    [Fact]
    public async Task Two_hundred_rows_and_single_inactive_unit_preserve_types_unicode_and_leading_zero_codes()
    {
        using var manager=await Actor("MANAGER");var type=await Type();var before=await Facts();
        var codes=Enumerable.Range(1,200).Select(i=>"0000-"+i.ToString("D4")+"-Ánh").ToArray();
        var preview=await ValidPreview(manager,Workbook(type,codes));Assert.Equal(200,preview.ValidRows);Assert.Equal(before,await Facts());
        using var committed=await manager.PostAsJsonAsync(Commit,new CommitApartmentExcel{Ticket=preview.Ticket!});Assert.Equal(HttpStatusCode.OK,committed.StatusCode);
        var one=XlsxTable.Write(new XlsxSheet(ApartmentExcelImport.DataSheet,ApartmentExcelImport.Headers.Select(x=>new XlsxColumn(x)).ToArray(),[new object?[]{"0000-INACTIVE",2,type,"75,50",null,0,"24/11/2026","Mô tả tiếng Việt","INACTIVE"}]));
        var single=await ValidPreview(manager,one);using var created=await manager.PostAsJsonAsync(Commit,new CommitApartmentExcel{Ticket=single.Ticket!});Assert.Equal(HttpStatusCode.OK,created.StatusCode);
        using var scope=database.Factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>();
        Assert.Equal(codes,await db.ApartmentUnits.Where(x=>x.UnitNumber.StartsWith("0000-")&&x.Status==MasterDataStatus.ACTIVE).OrderBy(x=>x.UnitNumber).Select(x=>x.UnitNumber).ToArrayAsync());
        var inactive=await db.ApartmentUnits.SingleAsync(x=>x.UnitNumber=="0000-INACTIVE");Assert.Equal(MasterDataStatus.INACTIVE,inactive.Status);Assert.Equal(75.5m,inactive.UsableAreaM2);Assert.Equal(new DateOnly(2026,11,24),inactive.HandoverDate);Assert.Null(inactive.BedroomCount);
        var after=await Facts();Assert.Equal(before.Units+201,after.Units);Assert.Equal(before.Accounts,after.Accounts);Assert.Equal(before.Ownerships,after.Ownerships);Assert.Equal(before.Residencies,after.Residencies);
        using var duplicate=await Upload(manager,one);Assert.Null((await duplicate.Content.ReadFromJsonAsync<ApartmentExcelPreview>())!.Ticket);Assert.Equal(after,await Facts());
    }

    [Fact]
    public async Task Expired_ticket_and_concurrent_manual_create_cannot_commit_stale_import()
    {
        using var manager=await Actor("MANAGER");var type=await Type();var before=await Facts();
        var preview=await ValidPreview(manager,Workbook(type,"EXPIRED-TICKET"));
        var protector=database.Factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("PropFlow.Apartments.Excel.CreateOnly.v1").ToTimeLimitedDataProtector();
        var plain=protector.Unprotect(preview.Ticket!,out _);var expired=protector.Protect(plain,DateTimeOffset.UtcNow.AddSeconds(-5));
        using var expiredResponse=await manager.PostAsJsonAsync(Commit,new CommitApartmentExcel{Ticket=expired});Assert.Equal(HttpStatusCode.Conflict,expiredResponse.StatusCode);Assert.Equal(before,await Facts());
        var race=await ValidPreview(manager,Workbook(type,"MANUAL-RACE"));Guid typeId;
        using(var scope=database.Factory.Services.CreateScope())typeId=await scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>().ApartmentUnitTypes.Where(x=>x.Name==type).Select(x=>x.Id).SingleAsync();
        var responses=await Task.WhenAll(manager.PostAsJsonAsync(Commit,new CommitApartmentExcel{Ticket=race.Ticket!}),manager.PostAsJsonAsync("/api/v1/apartments",new{unitNumber="MANUAL-RACE",floorNumber=2,apartmentUnitTypeId=typeId,usableAreaM2=75.5m,status="ACTIVE"}));
        Assert.Single(responses,x=>x.IsSuccessStatusCode);Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.Conflict);Assert.Equal(before.Units+1,(await Facts()).Units);foreach(var response in responses)response.Dispose();
    }

    [Fact]
    public async Task Import_creates_all_rows_atomically_and_filtered_export_spans_pages_without_side_effects()
    {
        using var manager=await Actor("MANAGER");var type=await Type();var before=await Facts();
        var numbers=Enumerable.Range(1,101).Select(x=>"XLS-"+x.ToString("D4")).ToArray();
        var preview=await ValidPreview(manager,Workbook(type,numbers));Assert.Equal(101,preview.ValidRows);Assert.Equal(before,await Facts());
        using var commit=await manager.PostAsJsonAsync(Commit,new CommitApartmentExcel{Ticket=preview.Ticket!});Assert.Equal(HttpStatusCode.OK,commit.StatusCode);
        Assert.Equal(101,(await commit.Content.ReadFromJsonAsync<ApartmentExcelResult>())!.CreatedCount);
        var after=await Facts();Assert.Equal(before.Units+101,after.Units);Assert.Equal(before.Accounts,after.Accounts);Assert.Equal(before.Ownerships,after.Ownerships);Assert.Equal(before.Residencies,after.Residencies);Assert.Equal(before.Audit+101,after.Audit);
        using var exported=await manager.GetAsync("/api/v1/apartments/excel/export?search=XLS-&floorNumber=2&status=ACTIVE&occupancy=VACANT");
        Assert.Equal(HttpStatusCode.OK,exported.StatusCode);Assert.Equal(XlsxTable.ContentType,exported.Content.Headers.ContentType!.MediaType);
        Assert.Contains("PropFlow_DanhSachCanHo_",exported.Content.Headers.ContentDisposition!.ToString());
        var content=await exported.Content.ReadAsByteArrayAsync();var table=XlsxTable.Read(content,["Mã căn hộ","Tầng","Loại căn hộ","Diện tích (m²)","Chủ sở hữu","Cư dân đang cư trú","Trạng thái","Tình trạng cư trú"],sheetName:"Danh sách căn hộ",headerRow:5);
        Assert.Equal(numbers,table.Rows.Select(x=>x.Cells[0]).ToArray());Assert.All(table.Rows,x=>Assert.Equal(type,x.Cells[2]));
        using var replay=await manager.PostAsJsonAsync(Commit,new CommitApartmentExcel{Ticket=preview.Ticket!});Assert.Equal(HttpStatusCode.Conflict,replay.StatusCode);Assert.Equal(after,await Facts());
    }
    [Fact]
    public async Task Invalid_duplicate_and_tampered_previews_never_write_and_all_rows_are_rechecked_at_commit()
    {
        using var manager=await Actor("MANAGER");using var other=await Actor("MANAGER");var type=await Type();var baseline=await Facts();
        using(var invalid=await Upload(manager,[1,2,3]))Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        using(var duplicate=await Upload(manager,Workbook(type,"REPEAT","REPEAT")))
        {
            var result=(await duplicate.Content.ReadFromJsonAsync<ApartmentExcelPreview>())!;Assert.Null(result.Ticket);Assert.Equal(2,result.DuplicateRows);
        }
        Assert.Equal(baseline,await Facts());
        var preview=await ValidPreview(manager,Workbook(type,"STALE-1","STALE-2"));
        using(var forbidden=await other.PostAsJsonAsync(Commit,new CommitApartmentExcel {Ticket=preview.Ticket!}))Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
        using(var tampered=await manager.PostAsJsonAsync(Commit,new CommitApartmentExcel {Ticket="tampered"+preview.Ticket}))Assert.Equal(HttpStatusCode.Conflict,tampered.StatusCode);
        using(var scope=database.Factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>();var id=await db.ApartmentUnitTypes.Where(x=>x.Name==type).Select(x=>x.Id).SingleAsync();
            db.ApartmentUnits.Add(new ApartmentUnit("STALE-2",2,DateTimeOffset.UtcNow,id));await db.SaveChangesAsync();
        }
        var changed=await Facts();
        using(var stale=await manager.PostAsJsonAsync(Commit,new CommitApartmentExcel {Ticket=preview.Ticket!}))Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        Assert.Equal(changed,await Facts());
        using(var scope=database.Factory.Services.CreateScope())Assert.False(await scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>().ApartmentUnits.AnyAsync(x=>x.UnitNumber=="STALE-1"));
    }
    [Fact]
    public async Task Concurrent_double_submit_has_one_success_one_conflict_and_no_duplicate_audit()
    {
        using var manager=await Actor("MANAGER");var type=await Type();var before=await Facts();
        var preview=await ValidPreview(manager,Workbook(type,"RACE-01","RACE-02"));
        var responses=await Task.WhenAll(manager.PostAsJsonAsync(Commit,new CommitApartmentExcel{Ticket=preview.Ticket!}),manager.PostAsJsonAsync(Commit,new CommitApartmentExcel{Ticket=preview.Ticket!}));
        Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.Conflict);
        var after=await Facts();Assert.Equal(before.Units+2,after.Units);Assert.Equal(before.Audit+2,after.Audit);foreach(var response in responses)response.Dispose();
    }
    [Fact]
    public async Task Audit_failure_rolls_back_the_entire_import()
    {
        var type=await Type();
        await using var broken=database.Factory.WithWebHostBuilder(builder=>builder.ConfigureTestServices(services=>
        {services.AddScoped<SystemAuditWriter>();services.RemoveAll<ISystemAuditWriter>();services.AddScoped<ISystemAuditWriter>(sp=>new FailAudit(sp.GetRequiredService<SystemAuditWriter>(),2));}));
        using var manager=await Actor("MANAGER",broken);var before=await Facts();var preview=await ValidPreview(manager,Workbook(type,"ROLLBACK-1","ROLLBACK-2"));
        using var response=await manager.PostAsJsonAsync(Commit,new CommitApartmentExcel{Ticket=preview.Ticket!});Assert.Equal(HttpStatusCode.InternalServerError,response.StatusCode);Assert.Equal(before,await Facts());
    }
    [Fact]
    public async Task Resident_relationship_exports_match_filtered_list_and_exclude_ended_and_future_relations()
    {
        using var manager=await Actor("MANAGER");var type=await Type();
        var today=DateOnly.FromDateTime(DateTime.UtcNow);Guid unitA,unitB;
        using(var scope=database.Factory.Services.CreateScope())
        {
            var units=scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>();var people=scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
            var typeId=await units.ApartmentUnitTypes.Where(x=>x.Name==type).Select(x=>x.Id).SingleAsync();
            var a=new ApartmentUnit("0000-A",2,DateTimeOffset.UtcNow,typeId,75.5m);var b=new ApartmentUnit("0000-B",3,DateTimeOffset.UtcNow,typeId,90m);unitA=a.Id;unitB=b.Id;units.ApartmentUnits.AddRange(a,b);await units.SaveChangesAsync();
            var residents=Enumerable.Range(1,4).Select(i=>new Resident("QA-"+i,"QA Excel "+new[]{"An","Bình","Chi","Diệp"}[i-1],new DateOnly(1990,1,i),null,"Việt Nam","CCCD","00000000000"+i,null,null,DateTimeOffset.UtcNow,phoneNumber:"090000000"+i,email:$"qa{i}@example.invalid")).ToArray();
            residents[3].Deactivate(null,DateTimeOffset.UtcNow);
            people.Residents.AddRange(residents);await people.SaveChangesAsync();
            units.ApartmentOwnerships.AddRange(new ApartmentOwnership(unitA,residents[0].Id,today.AddDays(-30),DateTimeOffset.UtcNow,null),new ApartmentOwnership(unitA,residents[1].Id,today.AddDays(-30),DateTimeOffset.UtcNow,null),new ApartmentOwnership(unitB,residents[1].Id,today.AddDays(30),DateTimeOffset.UtcNow,null));
            var ended=new ApartmentOwnership(unitB,residents[0].Id,today.AddDays(-90),DateTimeOffset.UtcNow,null);ended.End(today.AddDays(-31),DateTimeOffset.UtcNow,null);units.ApartmentOwnerships.Add(ended);await units.SaveChangesAsync();
            var head=new ResidentApartment(residents[2].Id,unitB,HouseholdRole.HOUSEHOLD_HEAD,ResidencyType.TENANT,today.AddDays(-20),DateTimeOffset.UtcNow);
            people.ResidentApartments.AddRange(head,new ResidentApartment(residents[0].Id,unitA,HouseholdRole.HOUSEHOLD_HEAD,ResidencyType.OWNER_OCCUPIED,today.AddDays(-20),DateTimeOffset.UtcNow),new ResidentApartment(residents[1].Id,unitB,HouseholdRole.HOUSEHOLD_MEMBER,ResidencyType.AUTHORIZED_OCCUPANT,today.AddDays(30),DateTimeOffset.UtcNow,head.Id,HouseholdRelationship.CHILD));
            var old=new ResidentApartment(residents[0].Id,unitB,HouseholdRole.HOUSEHOLD_HEAD,ResidencyType.TENANT,today.AddDays(-90),DateTimeOffset.UtcNow);old.EndResidency(today.AddDays(-40),null,DateTimeOffset.UtcNow);people.ResidentApartments.Add(old);await people.SaveChangesAsync();
        }
        var headers=new[]{"Mã cư dân","Họ và tên","Trạng thái","Tài khoản","Căn hộ sở hữu","Căn hộ đang cư trú","Số điện thoại","Email liên hệ"};
        using(var apartmentList=JsonDocument.Parse(await manager.GetStringAsync("/api/v1/apartments?search=0000-")))
        {
            var items=apartmentList.RootElement.GetProperty("items").EnumerateArray().ToArray();
            var a=items.Single(x=>x.GetProperty("id").GetGuid()==unitA);var b=items.Single(x=>x.GetProperty("id").GetGuid()==unitB);
            Assert.Equal(new[]{"QA Excel An","QA Excel Bình"},a.GetProperty("owners").EnumerateArray().Select(x=>x.GetProperty("fullName").GetString()));
            Assert.Equal(new[]{"QA Excel An"},a.GetProperty("currentResidents").EnumerateArray().Select(x=>x.GetProperty("fullName").GetString()));
            Assert.Empty(b.GetProperty("owners").EnumerateArray());
            Assert.Equal(new[]{"QA Excel Chi"},b.GetProperty("currentResidents").EnumerateArray().Select(x=>x.GetProperty("fullName").GetString()));
        }
        using(var apartmentExport=await manager.GetAsync("/api/v1/apartments/excel/export?search=0000-"))
        {
            var exported=XlsxTable.Read(await apartmentExport.Content.ReadAsByteArrayAsync(),["Mã căn hộ","Tầng","Loại căn hộ","Diện tích (m²)","Chủ sở hữu","Cư dân đang cư trú","Trạng thái","Tình trạng cư trú"],sheetName:"Danh sách căn hộ",headerRow:5).Rows;
            Assert.Equal("QA Excel An\nQA Excel Bình",exported[0].Cells[4]);Assert.Equal("QA Excel An · QA-1",exported[0].Cells[5]);
            Assert.Equal("Chưa xác định",exported[1].Cells[4]);Assert.Equal("QA Excel Chi · QA-3",exported[1].Cells[5]);
        }
        foreach(var (query,expected) in new (string,string[])[]{("search=0000-&hasOwner=false",["0000-B"]),("search=QA%20Excel%20Chi",["0000-B"]),("search=0000-&minArea=1000",[])})
        {
            using var list=JsonDocument.Parse(await manager.GetStringAsync("/api/v1/apartments?"+query));
            Assert.Equal(expected,list.RootElement.GetProperty("items").EnumerateArray().Select(x=>x.GetProperty("unitNumber").GetString()));
        }
        foreach(var (query,expected) in new (string,string[])[]{("floorNumber=3",["QA-3"]),("hasAccount=true",[]),("relationshipKind=ANY_OWNER",["QA-1","QA-2"]),("relationshipKind=ANY_RESIDENT",["QA-1","QA-3"]),("floorNumber=2&apartmentUnitId="+unitB,[])})
        {
            var list=(await manager.GetFromJsonAsync<PagedResidentsResponse>("/api/v1/residents?search=QA%20Excel&"+query))!;
            Assert.Equal(expected,list.Items.Select(x=>x.ResidentCode));
        }
        foreach(var filter in new[]{"","&relationshipKind=OWNER_AND_RESIDENT","&relationshipKind=OWNER_ONLY","&residencyType=TENANT&householdRole=HOUSEHOLD_HEAD","&apartmentUnitId="+unitB})
        {
            var query="?search=QA%20Excel&status=ACTIVE"+filter;
            var list=(await manager.GetFromJsonAsync<PagedResidentsResponse>("/api/v1/residents"+query))!;
            using var export=await manager.GetAsync("/api/v1/residents/excel/export"+query);Assert.Equal(HttpStatusCode.OK,export.StatusCode);
            var rows=XlsxTable.Read(await export.Content.ReadAsByteArrayAsync(),headers,sheetName:"Danh sách cư dân",headerRow:5).Rows;
            Assert.Equal(list.Items.Select(x=>x.ResidentCode),rows.Select(x=>x.Cells[0]));
            foreach(var row in rows)
            {
                var code=row.Cells[0];Assert.Equal(code is "QA-1" or "QA-2"?"0000-A":"",row.Cells[4]);Assert.Equal(code=="QA-1"?"0000-A":code=="QA-3"?"0000-B":"",row.Cells[5]);
                Assert.DoesNotContain("000000000001",row.Cells);Assert.DoesNotContain("000000000002",row.Cells);Assert.DoesNotContain("1990",row.Cells);
            }
        }
        using(var residentLogin=await Actor("RESIDENT"))
        using(var scope=database.Factory.Services.CreateScope())
        {
            var people=scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
            (await people.Residents.SingleAsync(x=>x.ResidentCode=="QA-1")).LinkUserAccount(issuedAccounts[residentLogin].Id,null,DateTimeOffset.UtcNow);await people.SaveChangesAsync();
            var units=scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>();var typeId=await units.ApartmentUnitTypes.Where(x=>x.Name==type).Select(x=>x.Id).SingleAsync();
            var inactive=new ApartmentUnit("MATRIX-C",2,DateTimeOffset.UtcNow,typeId,65m);inactive.Deactivate(null,DateTimeOffset.UtcNow);units.ApartmentUnits.Add(inactive);await units.SaveChangesAsync();
        }
        // Multi-value filters must narrow the real SQL query, not only decorate the UI.
        var multiInactive=(await manager.GetFromJsonAsync<PagedResidentsResponse>("/api/v1/residents?search=QA%20Excel&statuses=INACTIVE&statuses=MOVED_OUT"))!;
        Assert.Equal(new[]{"QA-4"},multiInactive.Items.Select(x=>x.ResidentCode));
        var multiCases=new (string Query,string[] Expected)[] {
            ("statuses=ACTIVE&statuses=INACTIVE",["QA-1","QA-2","QA-3","QA-4"]),
            ("status=INACTIVE&statuses=ACTIVE",["QA-1","QA-2","QA-3","QA-4"]),
            ($"apartmentUnitIds={unitA}&apartmentUnitIds={unitB}",["QA-1","QA-2","QA-3"]),
            ("floorNumbers=2&floorNumbers=3",["QA-1","QA-2","QA-3"]),
            ($"apartmentUnitIds={unitA}&apartmentUnitIds={unitB}&floorNumbers=2",["QA-1","QA-2"]),
            ($"apartmentUnitIds={unitA}&floorNumbers=3&floorNumbers=9",[]),
            ("relationshipKinds=OWNER_ONLY&relationshipKinds=RESIDENT_ONLY",["QA-2","QA-3"]),
            ("relationshipKinds=ANY_OWNER&relationshipKinds=ANY_RESIDENT",["QA-1","QA-2","QA-3"]),
            ("relationshipKinds=NONE&relationshipKinds=ANY_OWNER",["QA-1","QA-2","QA-4"]),
            ("statuses=INACTIVE&statuses=MOVED_OUT&relationshipKinds=NONE",["QA-4"]),
            ($"apartmentUnitIds={unitA}&apartmentUnitIds={unitB}&hasAccount=true",["QA-1"]),
            ($"apartmentUnitIds={unitA}&apartmentUnitIds={unitB}&residencyType=TENANT",["QA-3"]),
            ("floorNumber=2&floorNumbers=3",["QA-1","QA-2","QA-3"]),
            ($"apartmentUnitId={unitA}&apartmentUnitIds={unitB}&relationshipKinds=OWNER_ONLY",["QA-2"]),
            ($"apartmentUnitIds={unitA}&apartmentUnitIds={unitA}&apartmentUnitIds={unitB}&floorNumbers=2&floorNumbers=3&statuses=ACTIVE&relationshipKinds=ANY_OWNER&relationshipKinds=ANY_RESIDENT",["QA-1","QA-2","QA-3"])
        };
        foreach(var (filter,expected) in multiCases)
        {
            var query="?search=QA%20Excel&"+filter;
            var list=(await manager.GetFromJsonAsync<PagedResidentsResponse>("/api/v1/residents"+query))!;
            Assert.Equal(expected,list.Items.Select(x=>x.ResidentCode));Assert.Equal(expected.Length,list.TotalCount);
            Assert.Equal(expected.Length,list.Items.Select(x=>x.Id).Distinct().Count());
            using var file=await manager.GetAsync("/api/v1/residents/excel/export"+query);Assert.Equal(HttpStatusCode.OK,file.StatusCode);
            Assert.Equal(expected,ReadReport(await file.Content.ReadAsByteArrayAsync(),headers,"Danh sách cư dân").Select(x=>x.Cells[0]));
        }
        var pagedCodes=new List<string>();
        for(var page=1;page<=3;page++)
        {
            var result=(await manager.GetFromJsonAsync<PagedResidentsResponse>($"/api/v1/residents?search=QA%20Excel&floorNumbers=2&floorNumbers=3&pageSize=1&pageIndex={page}"))!;
            Assert.Equal(3,result.TotalCount);Assert.Equal(page,result.PageIndex);pagedCodes.Add(Assert.Single(result.Items).ResidentCode);
        }
        Assert.Equal(new[]{"QA-1","QA-2","QA-3"},pagedCodes);
        using(var report=await manager.GetAsync($"/api/v1/residents/excel/export?statuses=ACTIVE&statuses=INACTIVE&apartmentUnitIds={unitA}&apartmentUnitIds={unitB}&floorNumbers=2&floorNumbers=3&relationshipKinds=ANY_OWNER&relationshipKinds=ANY_RESIDENT"))
        {
            Assert.Equal(HttpStatusCode.OK,report.StatusCode);
            using var archive=new ZipArchive(new MemoryStream(await report.Content.ReadAsByteArrayAsync()));
            using var stream=archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
            var metadata=string.Concat(XDocument.Load(stream).Descendants(SheetNs+"row").Single(x=>(int)x.Attribute("r")! == 3).Descendants(SheetNs+"t").Select(x=>x.Value));
            foreach(var label in new[]{"0000-A","0000-B","Đang hoạt động","Ngừng hoạt động","Có sở hữu căn hộ","Đang cư trú","2, 3"})Assert.Contains(label,metadata);
        }
        foreach(var invalid in new[]{"statuses=999","statuses=UNKNOWN","floorNumbers=-1","floorNumbers=2.5","apartmentUnitIds=not-a-guid","apartmentUnitIds="+Guid.Empty,"relationshipKinds=999",string.Join('&',Enumerable.Repeat("floorNumbers=2",101))})
            foreach(var endpoint in new[]{"/api/v1/residents?","/api/v1/residents/excel/export?"})Assert.Equal(HttpStatusCode.BadRequest,(await manager.GetAsync(endpoint+invalid)).StatusCode);
        using(var accountant=await Actor("ACCOUNTANT"))
        {
            var restricted=(await accountant.GetFromJsonAsync<PagedResidentsResponse>("/api/v1/residents?search=QA%20Excel&floorNumbers=2&floorNumbers=3"))!;
            Assert.Equal(3,restricted.TotalCount);Assert.All(restricted.Items,x=>{Assert.Null(x.Email);Assert.Null(x.PhoneNumber);});
            using var file=await accountant.GetAsync("/api/v1/residents/excel/export?floorNumbers=2&floorNumbers=3");Assert.Equal(HttpStatusCode.OK,file.StatusCode);
            Assert.Equal(3,ReadReport(await file.Content.ReadAsByteArrayAsync(),headers[..6],"Danh sách cư dân").Count);
        }
        using(var unprivileged=await Actor("RESIDENT"))
            foreach(var endpoint in new[]{"/api/v1/residents?","/api/v1/residents/excel/export?"})Assert.Equal(HttpStatusCode.Forbidden,(await unprivileged.GetAsync(endpoint+"statuses=ACTIVE&statuses=INACTIVE")).StatusCode);
        var beforeMatrix=await Facts();
        var relationSets=new Dictionary<string,string[]>{{"",["QA-1","QA-2","QA-3","QA-4"]},{"OWNER_ONLY",["QA-2"]},{"RESIDENT_ONLY",["QA-3"]},{"OWNER_AND_RESIDENT",["QA-1"]},{"ANY_OWNER",["QA-1","QA-2"]},{"ANY_RESIDENT",["QA-1","QA-3"]},{"NONE",["QA-4"]}};
        var residencySets=new Dictionary<string,string[]>{{"",["QA-1","QA-2","QA-3","QA-4"]},{"OWNER_OCCUPIED",["QA-1"]},{"TENANT",["QA-3"]},{"AUTHORIZED_OCCUPANT",[]}};
        var roleSets=new Dictionary<string,string[]>{{"",["QA-1","QA-2","QA-3","QA-4"]},{"HOUSEHOLD_HEAD",["QA-1","QA-3"]},{"HOUSEHOLD_MEMBER",[]}};
        int matrixCase=0,apartmentCase=0,exportComparisons=0,invalidChecks=0;
        foreach(var relation in relationSets)foreach(var residency in residencySets)foreach(var role in roleSets)
        {
            var floor=new[]{"","2","3","9"}[matrixCase%4];var account=new[]{"","true","false"}[matrixCase%3];var state=new[]{"","ACTIVE","INACTIVE","MOVED_OUT"}[(matrixCase/3)%4];
            var expected=relation.Value.Intersect(residency.Value).Intersect(role.Value);
            if(floor!="")expected=expected.Intersect(floor=="2"?["QA-1","QA-2"]:floor=="3"?["QA-3"]:Array.Empty<string>());
            if(account!="")expected=expected.Intersect(account=="true"?["QA-1"]:["QA-2","QA-3","QA-4"]);
            if(state!="")expected=expected.Intersect(state=="ACTIVE"?["QA-1","QA-2","QA-3"]:state=="INACTIVE"?["QA-4"]:Array.Empty<string>());
            var query=$"?search=QA%20Excel&relationshipKind={relation.Key}&residencyType={residency.Key}&householdRole={role.Key}&floorNumber={floor}&hasAccount={account}&status={state}";
            var list=(await manager.GetFromJsonAsync<PagedResidentsResponse>("/api/v1/residents"+query))!;
            Assert.Equal(expected.Order(),list.Items.Select(x=>x.ResidentCode).Order());
            if(matrixCase%7==0)
            {
                using var file=await manager.GetAsync("/api/v1/residents/excel/export"+query);Assert.Equal(HttpStatusCode.OK,file.StatusCode);
                Assert.Equal(list.Items.Select(x=>x.ResidentCode),ReadReport(await file.Content.ReadAsByteArrayAsync(),headers,"Danh sách cư dân").Select(x=>x.Cells[0]));
                exportComparisons++;
            }
            matrixCase++;
        }
        var unitFacts=new[]{(Code:"0000-A",Floor:2,Area:75.5m,Owner:true,Occupied:true,State:"ACTIVE"),(Code:"0000-B",Floor:3,Area:90m,Owner:false,Occupied:true,State:"ACTIVE"),(Code:"MATRIX-C",Floor:2,Area:65m,Owner:false,Occupied:false,State:"INACTIVE")};
        foreach(var owner in new[]{"","true","false"})foreach(var occupied in new[]{"","OCCUPIED","VACANT"})foreach(var state in new[]{"","ACTIVE","INACTIVE"})
        {
            var query=$"?hasOwner={owner}&occupancy={occupied}&status={state}&minArea=65&maxArea=90";
            using var list=JsonDocument.Parse(await manager.GetStringAsync("/api/v1/apartments"+query));
            var expected=unitFacts.Where(x=>(owner==""||x.Owner==bool.Parse(owner))&&(occupied==""||x.Occupied==(occupied=="OCCUPIED"))&&(state==""||x.State==state)).OrderBy(x=>x.Floor).ThenBy(x=>x.Code).Select(x=>x.Code);
            var actual=list.RootElement.GetProperty("items").EnumerateArray().Select(x=>x.GetProperty("unitNumber").GetString()).ToArray();Assert.Equal(expected,actual);
            using var file=await manager.GetAsync("/api/v1/apartments/excel/export"+query);Assert.Equal(HttpStatusCode.OK,file.StatusCode);
            Assert.Equal(actual,ReadReport(await file.Content.ReadAsByteArrayAsync(),["Mã căn hộ","Tầng","Loại căn hộ","Diện tích (m²)","Chủ sở hữu","Cư dân đang cư trú","Trạng thái","Tình trạng cư trú"],"Danh sách căn hộ").Select(x=>x.Cells[0]));
            apartmentCase++;exportComparisons++;
        }
        foreach(var invalid in new[]{"floorNumber=-1","floorNumber=1.5","status=99","hasAccount=maybe","relationshipKind=99","residencyType=99","householdRole=99"})
            foreach(var endpoint in new[]{"/api/v1/residents?","/api/v1/residents/excel/export?"}){Assert.Equal(HttpStatusCode.BadRequest,(await manager.GetAsync(endpoint+invalid)).StatusCode);invalidChecks++;}
        foreach(var invalid in new[]{"floorNumber=-1","floorNumber=1.5","status=99","occupancy=99","hasOwner=maybe","minArea=-1","minArea=90&maxArea=65","minArea=NaN"})
            foreach(var endpoint in new[]{"/api/v1/apartments?","/api/v1/apartments/excel/export?"}){Assert.Equal(HttpStatusCode.BadRequest,(await manager.GetAsync(endpoint+invalid)).StatusCode);invalidChecks++;}
        foreach(var badSearch in new[]{"QA%00",new string('a',201),Uri.EscapeDataString(string.Join(' ',Enumerable.Repeat("a",13)))})
            foreach(var endpoint in new[]{"/api/v1/apartments?","/api/v1/apartments/excel/export?","/api/v1/residents?","/api/v1/residents/excel/export?"}){Assert.Equal(HttpStatusCode.BadRequest,(await manager.GetAsync(endpoint+"search="+badSearch)).StatusCode);invalidChecks++;}
        Assert.Equal(beforeMatrix,await Facts());
        using(var mixed=JsonDocument.Parse(await manager.GetStringAsync("/api/v1/apartments?search="+Uri.EscapeDataString("  0000-A  BÌNH "))))Assert.Equal("0000-A",Assert.Single(mixed.RootElement.GetProperty("items").EnumerateArray()).GetProperty("unitNumber").GetString());
        Assert.Equal("QA-1",Assert.Single((await manager.GetFromJsonAsync<PagedResidentsResponse>("/api/v1/residents?search="+Uri.EscapeDataString("QA-1   An")))!.Items).ResidentCode);
        using(var accountant=await Actor("ACCOUNTANT"))Assert.Empty((await accountant.GetFromJsonAsync<PagedResidentsResponse>("/api/v1/residents?search=qa1%40example.invalid"))!.Items);
        using(var last=JsonDocument.Parse(await manager.GetStringAsync("/api/v1/apartments?floorNumber=2&pageIndex=999&pageSize=1"))){Assert.Equal(2,last.RootElement.GetProperty("pageIndex").GetInt32());Assert.Equal("MATRIX-C",Assert.Single(last.RootElement.GetProperty("items").EnumerateArray()).GetProperty("unitNumber").GetString());}
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"PropFlow.sln")))root=root.Parent;Assert.NotNull(root);
        var evidence=Path.Combine(root!.FullName,"artifacts","excel-qa");Directory.CreateDirectory(evidence);
        foreach(var (query,expected) in new (string,string[])[]{("status=INACTIVE",["QA-4"]),("hasAccount=true",["QA-1"]),("hasAccount=false",["QA-2","QA-3","QA-4"]),("relationshipKind=NONE",["QA-4"])})
            Assert.Equal(expected,((await manager.GetFromJsonAsync<PagedResidentsResponse>("/api/v1/residents?search=QA%20Excel&"+query))!).Items.Select(x=>x.ResidentCode));
        foreach(var (query,expected) in new (string,string[])[]{("maxArea=75.5",["0000-A","MATRIX-C"]),("floorNumber=2&minArea=75.5",["0000-A"]),("minArea=90&maxArea=90",["0000-B"]),("search=%25",[])})
        {
            using var list=JsonDocument.Parse(await manager.GetStringAsync("/api/v1/apartments?"+query));
            Assert.Equal(expected,list.RootElement.GetProperty("items").EnumerateArray().Select(x=>x.GetProperty("unitNumber").GetString()));
        }
        using(var scope=database.Factory.Services.CreateScope())
        {
            var people=scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();var an=await people.Residents.SingleAsync(x=>x.ResidentCode=="QA-1");
            (await people.ResidentApartments.SingleAsync(x=>x.ResidentId==an.Id&&x.ApartmentUnitId==unitA&&x.Status==ResidencyStatus.ACTIVE)).EndResidency(today.AddDays(-1),null,DateTimeOffset.UtcNow);
            var head=await people.ResidentApartments.SingleAsync(x=>x.ApartmentUnitId==unitB&&x.HouseholdRole==HouseholdRole.HOUSEHOLD_HEAD&&x.Status==ResidencyStatus.ACTIVE);
            people.ResidentApartments.Add(new(an.Id,unitB,HouseholdRole.HOUSEHOLD_MEMBER,ResidencyType.AUTHORIZED_OCCUPANT,today,DateTimeOffset.UtcNow,head.Id,HouseholdRelationship.OTHER_RELATIVE));await people.SaveChangesAsync();
        }
        foreach(var (query,expected) in new (string,string[])[]{("floorNumber=2&relationshipKind=OWNER_ONLY",["QA-1","QA-2"]),("floorNumber=3&relationshipKind=RESIDENT_ONLY",["QA-1","QA-3"]),("relationshipKind=OWNER_AND_RESIDENT",["QA-1"])})
            Assert.Equal(expected,((await manager.GetFromJsonAsync<PagedResidentsResponse>("/api/v1/residents?search=QA%20Excel&"+query))!).Items.Select(x=>x.ResidentCode));
        foreach(var (query,expected) in new (string,string[])[]{
            ($"apartmentUnitIds={unitA}&apartmentUnitIds={unitB}&relationshipKinds=OWNER_AND_RESIDENT",["QA-1"]),
            ($"apartmentUnitIds={unitA}&apartmentUnitIds={unitB}&floorNumbers=3&relationshipKinds=OWNER_AND_RESIDENT",[]),
            ($"apartmentUnitIds={unitA}&floorNumbers=2&floorNumbers=3&relationshipKinds=OWNER_ONLY",["QA-1","QA-2"])})
            Assert.Equal(expected,((await manager.GetFromJsonAsync<PagedResidentsResponse>("/api/v1/residents?search=QA%20Excel&"+query))!).Items.Select(x=>x.ResidentCode));
        using(var scope=database.Factory.Services.CreateScope())
        {
            var units=scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>();
            var people=scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
            var an=await people.Residents.SingleAsync(x=>x.ResidentCode=="QA-1");
            units.ApartmentOwnerships.Add(new(unitB,an.Id,today,DateTimeOffset.UtcNow,null));await units.SaveChangesAsync();
        }
        var multiOwner=(await manager.GetFromJsonAsync<PagedResidentsResponse>($"/api/v1/residents?apartmentUnitIds={unitA}&apartmentUnitIds={unitB}&relationshipKinds=ANY_OWNER"))!;
        Assert.Equal(2,multiOwner.TotalCount);Assert.Equal(new[]{"QA-1","QA-2"},multiOwner.Items.Select(x=>x.ResidentCode));
        using var empty=await manager.GetAsync("/api/v1/residents/excel/export?search=not-found-in-fixture");Assert.Equal(HttpStatusCode.OK,empty.StatusCode);
        using var emptyZip=new ZipArchive(new MemoryStream(await empty.Content.ReadAsByteArrayAsync()));using var input=emptyZip.GetEntry("xl/worksheets/sheet1.xml")!.Open();var sheet=XDocument.Load(input);Assert.DoesNotContain(sheet.Descendants(SheetNs+"row"),x=>(int)x.Attribute("r")!>5);
        await File.WriteAllTextAsync(Path.Combine(evidence,"directory-filter-matrix-evidence.json"),JsonSerializer.Serialize(new{observedAtUtc=DateTimeOffset.UtcNow,status="PASS",residentCombinationCases=matrixCase,apartmentCombinationCases=apartmentCase,exportComparisons,invalidChecks,fixtureOnly=true,queryBusinessWrites=0}));
    }

    [Fact]
    public async Task Resident_exports_respect_filters_full_pagination_and_accountant_contact_redaction()
    {
        using var manager=await Actor("MANAGER");using var accountant=await Actor("ACCOUNTANT");
        using(var scope=database.Factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
            // Synthetic historical profile fixtures exercise read scope; no new resident creation workflow is introduced.
            for(var i=0;i<101;i++)db.Residents.Add(new Resident("XLS-"+i.ToString("D4"),"Excel Nguyễn "+i.ToString("D4"),DateTimeOffset.UtcNow,phoneNumber:"0900000"+i.ToString("D3"),email:$"excel{i:D3}@example.invalid"));
            db.Residents.Add(new Resident("OTHER","Không thuộc bộ lọc",DateTimeOffset.UtcNow));await db.SaveChangesAsync();
        }
        var path="/api/v1/residents/excel/export?search="+Uri.EscapeDataString("Excel Nguyễn")+"&status=ACTIVE";
        using var full=await manager.GetAsync(path);using var redacted=await accountant.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK,full.StatusCode);Assert.Equal(HttpStatusCode.OK,redacted.StatusCode);
        var columns=new[]{"Mã cư dân","Họ và tên","Trạng thái","Tài khoản","Căn hộ sở hữu","Căn hộ đang cư trú"};
        var managerRows=XlsxTable.Read(await full.Content.ReadAsByteArrayAsync(),[..columns,"Số điện thoại","Email liên hệ"],sheetName:"Danh sách cư dân",headerRow:5);
        Assert.Equal(101,managerRows.Rows.Count);Assert.Equal("0900000000",managerRows.Rows[0].Cells[6]);Assert.Contains("Nguyễn",managerRows.Rows[0].Cells[1]);
        var accountantBytes=await redacted.Content.ReadAsByteArrayAsync();Assert.Equal(101,XlsxTable.Read(accountantBytes,columns,sheetName:"Danh sách cư dân",headerRow:5).Rows.Count);
        using var zip=new ZipArchive(new MemoryStream(accountantBytes));using var reader=new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());var xml=await reader.ReadToEndAsync();Assert.DoesNotContain("@example.invalid",xml);Assert.DoesNotContain("0900000",xml);
        using var hiddenSearch=await accountant.GetAsync("/api/v1/residents/excel/export?search=excel000%40example.invalid");
        using var hiddenZip=new ZipArchive(new MemoryStream(await hiddenSearch.Content.ReadAsByteArrayAsync()));using var hiddenReader=new StreamReader(hiddenZip.GetEntry("xl/worksheets/sheet1.xml")!.Open());Assert.DoesNotContain("XLS-0000",await hiddenReader.ReadToEndAsync());
    }
    [Fact]
    public async Task Read_and_manage_permissions_are_identical_to_the_existing_directory_endpoints()
    {
        using var anonymous=database.Factory.CreateClient(new(){BaseAddress=new Uri("https://localhost")});
        Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/v1/residents/excel/export")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await Upload(anonymous,[1,2])).StatusCode);
        foreach(var role in new[]{"STAFF","ACCOUNTANT","ADMIN","RESIDENT"})
        {
            using var actor=await Actor(role);
            Assert.Equal(HttpStatusCode.Forbidden,(await actor.GetAsync("/api/v1/apartments/excel/template")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await Upload(actor,[1,2])).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await actor.PostAsJsonAsync(Commit,new CommitApartmentExcel{Ticket="no-ticket"})).StatusCode);
            var expected=role is "STAFF" or "ACCOUNTANT"?HttpStatusCode.OK:HttpStatusCode.Forbidden;
            Assert.Equal(expected,(await actor.GetAsync("/api/v1/apartments/excel/export")).StatusCode);
            Assert.Equal(expected,(await actor.GetAsync("/api/v1/residents/excel/export")).StatusCode);
        }
        using var manager=await Actor("MANAGER");using var template=await manager.GetAsync("/api/v1/apartments/excel/template");Assert.Equal(HttpStatusCode.OK,template.StatusCode);
        using var swagger=await manager.GetAsync("/swagger/v1/swagger.json");Assert.Equal(HttpStatusCode.OK,swagger.StatusCode);Assert.Contains("/api/v1/apartments/excel/preview",await swagger.Content.ReadAsStringAsync());
        using var package=new ZipArchive(new MemoryStream(await template.Content.ReadAsByteArrayAsync()));Assert.Equal(2,package.Entries.Count(x=>x.FullName.StartsWith("xl/worksheets/")&&x.FullName.EndsWith(".xml")));
        Assert.Equal(HttpStatusCode.NotFound,(await manager.PostAsync("/api/v1/residents/excel/preview",null)).StatusCode);
    }
    private sealed class FailAudit(ISystemAuditWriter inner,int failOn):ISystemAuditWriter
    {
        private int count;
        public Task AppendAsync(SystemAuditEvent entry,CancellationToken ct)=>inner.AppendAsync(entry,ct);
        public Task AppendInTransactionAsync(SystemAuditEvent entry,DbConnection connection,DbTransaction transaction,CancellationToken ct)=>Interlocked.Increment(ref count)==failOn?throw new InvalidOperationException("Injected fixture-only audit failure on last entry"):inner.AppendInTransactionAsync(entry,connection,transaction,ct);
    }
}
