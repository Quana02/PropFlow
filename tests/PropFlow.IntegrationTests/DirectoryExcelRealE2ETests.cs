using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;
using PropFlow.Modules.Complaints.Infrastructure.Persistence;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.Billing.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using PropFlow.Modules.AiClassification.Infrastructure.Persistence;
using PropFlow.Modules.AiRecommendation.Infrastructure.Persistence;
using PropFlow.Modules.AiChatbot.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed class RealDirectoryExcelFactAttribute : FactAttribute
{
    public RealDirectoryExcelFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PROPFLOW_RUN_EXCEL_E2E") != "1")
            Skip = "Opt-in real Kestrel/browser test: set PROPFLOW_RUN_EXCEL_E2E=1 and publish Web to the test artifact directory.";
    }
}

[Trait("UseCase", "FE-02/FE-03 real Excel E2E")]
public sealed class DirectoryExcelRealE2ETests
{
    [RealDirectoryExcelFact]
    public async Task Real_excel_download_upload_preview_commit_postgresql_export_and_independent_read()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PropFlow.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var repo = root!.FullName;
        var evidenceRoot = Path.Combine(repo, "artifacts", "excel-real-e2e");
        Directory.CreateDirectory(evidenceRoot);
        // Published wwwroot/appsettings is shared by this harness. A second
        // process must not redirect an already-running browser to its fixture.
        using var publishedHostLease = new FileStream(Path.Combine(evidenceRoot,"published-host.lock"),
            FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var artifacts = Path.Combine(evidenceRoot, "run-"+DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ")+"-"+Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(artifacts);
        await File.WriteAllTextAsync(Path.Combine(evidenceRoot,"latest-run.json"),JsonSerializer.Serialize(new {artifacts}));
        var publishedWeb = Path.Combine(repo, "artifacts", "excel-real-web");
        Assert.True(File.Exists(Path.Combine(publishedWeb, "PropFlow.Web.dll")), "Publish the real Web app into artifacts/excel-real-web first.");
        var owned = Path.Combine(Path.GetTempPath(), "propflow-excel-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(owned);
        var databaseName = "propflow_excel_e2e_" + Guid.NewGuid().ToString("N");
        var source = TestDatabaseSafety.Dedicated(TestDatabaseConfiguration.GetConnectionString());
        var target = new NpgsqlConnectionStringBuilder(source.ConnectionString) { Database = databaseName };
        var adminTarget = new NpgsqlConnectionStringBuilder(source.ConnectionString) { Database = "postgres" };
        var processes = new List<Process>();
        var processRecords = new List<object>();
        var checkpoints = new List<object>();
        Dictionary<string,string> protectedBaseline=[];
        var apiPort = FreePort(); var webPort = FreePort();
        while (webPort == apiPort) webPort = FreePort();
        var apiUrl = $"https://127.0.0.1:{apiPort}";
        var webUrl = $"https://127.0.0.1:{webPort}";
        await File.WriteAllTextAsync(Path.Combine(artifacts,"fixture-ownership.json"),JsonSerializer.Serialize(new{databaseName,owned,apiPort,webPort,createdBy="DirectoryExcelRealE2ETests",observedAtUtc=DateTimeOffset.UtcNow}));
        var users = new Dictionary<string, Guid>();
        var completed = false; var journeyPassed = false; var created = false; var dropped = false;
        Process? api = null; Process? web = null; Process? browserTest = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        using var tls = RSA.Create(2048);
        var request = new CertificateRequest("CN=PropFlow owned Excel fixture", tls, System.Security.Cryptography.HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddIpAddress(IPAddress.Loopback); names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        var certificatePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var certificatePath = Path.Combine(owned, "localhost.pfx");
        await File.WriteAllBytesAsync(certificatePath, certificate.Export(X509ContentType.Pfx, certificatePassword));
        using var signing = RSA.Create(2048);
        var fixturePassword = "Aa9!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var common = new Dictionary<string,string>
        {
            ["ASPNETCORE_ENVIRONMENT"]="Testing", ["DOTNET_ENVIRONMENT"]="Testing",
            ["ASPNETCORE_Kestrel__Certificates__Default__Path"]=certificatePath,
            ["ASPNETCORE_Kestrel__Certificates__Default__Password"]=certificatePassword,
            ["Logging__LogLevel__Default"]="Warning", ["Logging__LogLevel__Microsoft"]="Warning"
        };
        var apiEnvironment = new Dictionary<string,string>(common)
        {
            ["ConnectionStrings__PropFlowDatabase"]=target.ConnectionString,
            ["Authentication__Issuer"]="propflow-real-e2e", ["Authentication__Audience"]="propflow-real-e2e",
            ["Authentication__PrivateKeyPem"]=signing.ExportRSAPrivateKeyPem(),
            ["Authentication__OtpHashKey"]=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Authentication__Mail__Host"]="127.0.0.1", ["Authentication__Mail__Port"]="9",
            ["Authentication__Mail__From"]="fixture@example.invalid",
            ["Authentication__Mail__Username"]="", ["Authentication__Mail__Password"]="",
            ["Cors__AllowedOrigins__0"]=webUrl, ["DataProtection__KeyPath"]=Path.Combine(owned,"api-dp"),
            ["AiChatbot__Gemini__Enabled"]="false", ["AiChatbot__Gemini__ApiKey"]="",
            ["Payments__SePay__Enabled"]="false", ["Payments__SePay__OAuth__Enabled"]="false",
            ["FacilityRecommendations__Live__Enabled"]="false"
            , ["Cloudinary__CloudName"]="fixture-not-connected", ["Cloudinary__ApiKey"]="fixture-not-connected", ["Cloudinary__ApiSecret"]="fixture-not-connected"
        };
        var webEnvironment = new Dictionary<string,string>(common) { ["DataProtection__KeyPath"]=Path.Combine(owned,"web-dp") };

        Process Start(string executable, string[] args, string cwd, IReadOnlyDictionary<string,string> environment, string label, bool protocol=false)
        {
            var info = new ProcessStartInfo(executable) { WorkingDirectory=cwd, UseShellExecute=false, CreateNoWindow=true,
                RedirectStandardOutput=true, RedirectStandardError=true, RedirectStandardInput=protocol,
                StandardOutputEncoding=System.Text.Encoding.UTF8, StandardErrorEncoding=System.Text.Encoding.UTF8 };
            foreach (var argument in args) info.ArgumentList.Add(argument);
            // No credentials from the developer's shell are inherited by a fixture server.
            foreach(var name in info.Environment.Keys.Where(x=>new[]{"Authentication__","ConnectionStrings__","Cloudinary__","AiChatbot__","Payments__","DataProtection__","ASPNETCORE_","DOTNET_ENVIRONMENT"}.Any(p=>x.StartsWith(p,StringComparison.OrdinalIgnoreCase))).ToArray()) info.Environment.Remove(name);
            foreach(var entry in environment) info.Environment[entry.Key]=entry.Value;
            var process = Process.Start(info)!;
            processes.Add(process); processRecords.Add(new {label,pid=process.Id});
            File.WriteAllText(Path.Combine(artifacts,"owned-processes.json"),JsonSerializer.Serialize(processRecords));
            process.ErrorDataReceived += (_, e) => { if(e.Data is not null) lock(processRecords) File.AppendAllText(Path.Combine(artifacts,label+".stderr.log"),e.Data+Environment.NewLine); };
            process.BeginErrorReadLine();
            if(!protocol)
            {
                process.OutputDataReceived += (_, e) => { if(e.Data is not null) lock(processRecords) File.AppendAllText(Path.Combine(artifacts,label+".stdout.log"),e.Data+Environment.NewLine); };
                process.BeginOutputReadLine();
            }
            return process;
        }
        async Task Ready(string url, Process process)
        {
            using var http = new HttpClient(new HttpClientHandler { UseProxy=false, ServerCertificateCustomValidationCallback=(_,_,_,_)=>true }) {Timeout=TimeSpan.FromSeconds(2)};
            for(var n=0;n<150;n++)
            {
                if(process.HasExited) throw new InvalidOperationException("Fixture server exited; inspect owned server logs.");
                try { using var response = await http.GetAsync(url,timeout.Token); if(response.IsSuccessStatusCode) return; }
                catch(HttpRequestException) { }
                catch(TaskCanceledException) when(!timeout.IsCancellationRequested) { }
                await Task.Delay(200,timeout.Token);
            }
            throw new TimeoutException("Fixture HTTPS server did not become ready.");
        }
        async Task StartServers(string suffix)
        {
            api=Start("dotnet",[Path.Combine(repo,"artifacts","excel-real-api","PropFlow.Api.dll"),"--urls",apiUrl],Path.Combine(repo,"artifacts","excel-real-api"),apiEnvironment,"api-"+suffix);
            await Ready(apiUrl+"/swagger/v1/swagger.json",api);
            web=Start("dotnet",[Path.Combine(publishedWeb,"PropFlow.Web.dll"),"--urls",webUrl],publishedWeb,webEnvironment,"web-"+suffix);
            await Ready(webUrl+"/login",web);
        }
        try
        {
            await using(var admin = new NpgsqlConnection(adminTarget.ConnectionString))
            { await admin.OpenAsync(timeout.Token); await TestDatabaseSafety.CreateAsync(admin,databaseName); created=true; }
            await TestDatabaseSafety.VerifyFixtureAsync(target.ConnectionString,databaseName,TestDatabaseSafety.ProcessOwner,source.ConnectionString);
            await MigrateAndSeed(target.ConnectionString,users,fixturePassword);
            protectedBaseline=await ProtectedFacts(target.ConnectionString,timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(artifacts,"protected-baseline.json"),JsonSerializer.Serialize(protectedBaseline));
            await using(var oracle=Context<ResidentsDbContext>(target.ConnectionString,"residents"))
            {
                var expected=await oracle.Residents.AsNoTracking().Where(x=>x.FullName.StartsWith("Excel Nguyễn")).OrderBy(x=>x.FullName).ThenBy(x=>x.Id).ToArrayAsync(timeout.Token);
                await File.WriteAllTextAsync(Path.Combine(artifacts,"expected-residents.json"),JsonSerializer.Serialize(expected.Select(x=>new object?[]{x.ResidentCode,x.FullName,"Đang hoạt động","Chưa có tài khoản","","",x.PhoneNumber,x.Email})));
            }
            await File.WriteAllBytesAsync(Path.Combine(artifacts,"text-format-probe.xlsx"),PropFlow.Shared.Spreadsheets.XlsxTable.Write(new PropFlow.Shared.Spreadsheets.XlsxSheet("Định dạng kiểm thử",
                [new("Mã",24),new("Điện thoại",28),new("Ngày",20,"date"),new("Diện tích",20,"decimal"),new("Chuỗi an toàn",42)],
                Enumerable.Range(0,4).Select(i=>new object?[]{"00001234567890123456","00123456789012345678",new DateOnly(2026,11,24),75.5m,new[]{"=1+1","+SUM(1,2)","-1+2","@SUM(A1)"}[i]}).ToArray(),"PropFlow — KIỂM THỬ ĐỊNH DẠNG","Dữ liệu giả","Bộ lọc: Kiểm thử")));
            await File.WriteAllTextAsync(Path.Combine(publishedWeb,"wwwroot","appsettings.json"),JsonSerializer.Serialize(new {Api=new {BaseUrl=apiUrl+"/"}}));
            await File.WriteAllTextAsync(Path.Combine(publishedWeb,"wwwroot","appsettings.Testing.json"),"{}");
            await StartServers("first");
            browserTest=Start("node",[Path.Combine(repo,"tests","frontend","directory-excel.real-e2e.cjs")],repo,
                new Dictionary<string,string> { ["PROPFLOW_E2E_WEB"]=webUrl,["PROPFLOW_E2E_API"]=apiUrl,["PROPFLOW_E2E_PASSWORD"]=fixturePassword,["PROPFLOW_E2E_ARTIFACTS"]=artifacts },"browser",true);
            while(await browserTest.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                using var message=JsonDocument.Parse(line);
                var command=message.RootElement;
                var action=command.GetProperty("action").GetString();
                await File.AppendAllTextAsync(Path.Combine(artifacts,"progress.log"),DateTimeOffset.UtcNow.ToString("O")+" "+action+" "+(command.TryGetProperty("phase",out var phase)?phase.GetString():"")+Environment.NewLine);
                if(action=="prepare"||action=="inspect")
                {
                    var file=command.GetProperty("file").GetString()!;
                    var kind=command.TryGetProperty("kind",out var k)?k.GetString()!:"apartments";
                    var reader=Start(Environment.GetEnvironmentVariable("PROPFLOW_EXCEL_PYTHON")??"python",[Path.Combine(repo,"tests","frontend","excel-fixture-workbooks.py"),action,file,artifacts,kind],repo,new Dictionary<string,string>(),"xlsx-library-"+action+"-"+kind);
                    await reader.WaitForExitAsync(timeout.Token);
                    Assert.Equal(0,reader.ExitCode);
                    checkpoints.Add(new {action,kind,reader="openpyxl",nativeExcel=false,verified=true});
                }
                else if(action=="db")
                {
                    await using var db=Context<ApartmentsDbContext>(target.ConnectionString,"apartments");
                    var count=command.GetProperty("count").GetInt32();
                    var rows=await db.ApartmentUnits.AsNoTracking().Where(x=>x.UnitNumber.StartsWith("XLS-E2E-")).ToArrayAsync(timeout.Token);
                    Assert.Equal(count,rows.Length);
                    Assert.All(rows,x=>{Assert.Equal(2,x.FloorNumber);Assert.Equal(75.5m,x.UsableAreaM2);Assert.Equal(new DateOnly(2026,11,24),x.HandoverDate);Assert.Equal(users["MANAGER"],x.CreatedBy);});
                    if(count>0)
                    {
                        var expected=await db.ApartmentUnits.AsNoTracking().Where(x=>x.UnitNumber.StartsWith("XLS-E2E-")).OrderBy(x=>x.FloorNumber).ThenBy(x=>x.UnitNumber).Select(x=>new{x.UnitNumber,x.FloorNumber,Type=x.ApartmentUnitType.Name,x.UsableAreaM2}).ToArrayAsync(timeout.Token);
                        await File.WriteAllTextAsync(Path.Combine(artifacts,"expected-apartments.json"),JsonSerializer.Serialize(expected.Select(x=>new object?[]{x.UnitNumber,x.FloorNumber,x.Type,x.UsableAreaM2,"Chưa xác định","Căn hộ trống","Đang quản lý","Căn hộ trống"})));
                    }
                    Assert.Equal(1,await db.ApartmentUnits.CountAsync(x=>x.UnitNumber=="OTHER-E2E"));
                    Assert.Equal(1,await db.ApartmentOwnerships.CountAsync());
                    await using var residentDb=Context<ResidentsDbContext>(target.ConnectionString,"residents");
                    Assert.Equal(2,await residentDb.ResidentApartments.CountAsync());
                    await using var authDb=Context<AuthenticationDbContext>(target.ConnectionString,"auth");
                    Assert.Equal(users.Count,await authDb.UserAccounts.CountAsync());
                    await using var auditDb=Context<AdministrationDbContext>(target.ConnectionString,"administration");
                    var audits=await auditDb.AuditLogs.Where(x=>x.EntityType=="ApartmentUnit").ToArrayAsync();
                    Assert.Equal(count,audits.Length);Assert.All(audits,x=>Assert.Equal(users["MANAGER"],x.ActorUserId));
                    var protectedAfter=await ProtectedFacts(target.ConnectionString,timeout.Token);
                    foreach(var entry in protectedBaseline)Assert.Equal(entry.Value,protectedAfter[entry.Key]);
                    checkpoints.Add(new {action,phase=command.GetProperty("phase").GetString(),rows=rows.Length,audits=audits.Length,accountOrRelationshipSideEffects=0,protectedTablesPreserved=protectedAfter.Count,verified=true});
                }
                else if(action=="api-offline")
                {
                    await Stop(api);checkpoints.Add(new{action,ownedFixtureOnly=true,verified=true});
                }
                else if(action=="api-online")
                {
                    api=Start("dotnet",[Path.Combine(repo,"artifacts","excel-real-api","PropFlow.Api.dll"),"--urls",apiUrl],Path.Combine(repo,"artifacts","excel-real-api"),apiEnvironment,"api-recovery");
                    await Ready(apiUrl+"/swagger/v1/swagger.json",api);checkpoints.Add(new{action,ownedFixtureOnly=true,verified=true});
                }
                else if(action=="complete")
                {
                    Assert.True(command.GetProperty("passed").GetBoolean());
                    await File.WriteAllTextAsync(Path.Combine(artifacts,"browser-result.json"),line);
                    completed=true;
                }
                else if(action!="progress") throw new InvalidOperationException("Unknown fixture protocol action.");
                await browserTest.StandardInput.WriteLineAsync("{\"ok\":true}");
            }
            await browserTest.WaitForExitAsync(timeout.Token);
            Assert.Equal(0,browserTest.ExitCode); Assert.True(completed,"Real browser journey did not complete.");
            journeyPassed=true;
        }
        finally
        {
            for(var i=processes.Count-1;i>=0;i--) await Stop(processes[i]);
            NpgsqlConnection.ClearAllPools();
            if(created)
            {
                await using var admin=new NpgsqlConnection(adminTarget.ConnectionString); await admin.OpenAsync();
                await TestDatabaseSafety.DropAsync(admin,databaseName); dropped=true;
            }
            var portsReleased=!Listening(apiPort)&&!Listening(webPort);
            await File.WriteAllTextAsync(Path.Combine(artifacts,"receipt.json"),JsonSerializer.Serialize(new
            {
                observedAtUtc=DateTimeOffset.UtcNow,passed=journeyPassed&&dropped&&portsReleased&&processes.All(x=>x.HasExited),
                actualWeb=true,actualKestrelApi=true,actualPostgreSql=true,apiDoubles=false,
                databaseName,fixtureOwned=true,fixtureDropped=dropped,processRecords,checkpoints,
                processesStopped=processes.All(x=>x.HasExited),portsReleased,apiPort,webPort,
                awsChanged=false,demoDatabaseChanged=false,packagesChanged=false
            },new JsonSerializerOptions {WriteIndented=true}));
            foreach(var process in processes) process.Dispose();
            if(Directory.Exists(owned)) Directory.Delete(owned,true);
            Assert.True(portsReleased,"Owned HTTPS ports not released.");
        }
    }

    private static T Context<T>(string connection,string schema) where T:DbContext =>
        (T)Activator.CreateInstance(typeof(T),new DbContextOptionsBuilder<T>().UseNpgsql(connection,n=>n.MigrationsHistoryTable("__EFMigrationsHistory",schema)).Options)!;
    private static async Task MigrateAndSeed(string connection,Dictionary<string,Guid> users,string password)
    {
        await using var auth=Context<AuthenticationDbContext>(connection,"auth");
        await using var property=Context<PropertyAssetsDbContext>(connection,"property_assets");
        await using var apartments=Context<ApartmentsDbContext>(connection,"apartments");
        await using var residents=Context<ResidentsDbContext>(connection,"residents");
        await using var administration=Context<AdministrationDbContext>(connection,"administration");
        await auth.GetService<IMigrator>().MigrateAsync("20260914101250_CorrectAuthenticationEmailOtpVerification");
        await property.Database.MigrateAsync();
        property.Buildings.Add(new Building("Excel owned E2E fixture","Synthetic local fixture",DateTimeOffset.UtcNow,numberOfFloors:10)); await property.SaveChangesAsync();
        await apartments.Database.MigrateAsync();
        await residents.GetService<IMigrator>().MigrateAsync("20260914063807_InitialResidents");
        await administration.Database.MigrateAsync(); await auth.Database.MigrateAsync(); await residents.Database.MigrateAsync();
        foreach(var db in new DbContext[] {
            Context<ServiceRequestsDbContext>(connection,"service_requests"),Context<ComplaintsDbContext>(connection,"complaints"),
            Context<MaintenanceDbContext>(connection,"maintenance"),Context<BillingDbContext>(connection,"billing"),
            Context<PaymentsDbContext>(connection,"payments"),Context<AiClassificationDbContext>(connection,"ai_classification"),
            Context<AiRecommendationDbContext>(connection,"ai_recommendation"),Context<AiChatbotDbContext>(connection,"ai_chatbot"),
            Context<CommunicationDbContext>(connection,"communication"),
            Context<PropFlow.Modules.RemoteOnlineSign.Infrastructure.Persistence.RemoteOnlineSignDbContext>(connection,"remote_online_sign") })
            await using(db) await db.Database.MigrateAsync();
        foreach(var role in new[]{"ADMIN","ADMIN2","MANAGER","STAFF","ACCOUNTANT","RESIDENT"})
        {
            var user=new UserAccount("e2e_"+role.ToLowerInvariant(),"pending","Fixture "+role,DateTimeOffset.UtcNow,email:role.ToLowerInvariant()+"@example.invalid",status:AccountStatus.ACTIVE,emailVerified:true);
            user.ChangePasswordHash(new PasswordHasher<UserAccount>().HashPassword(user,password),null,DateTimeOffset.UtcNow);
            auth.UserAccounts.Add(user); await auth.SaveChangesAsync();
            var assigned=await administration.Roles.SingleAsync(x=>x.Code==(role=="ADMIN2"?"ADMIN":role));
            administration.UserRoleAssignments.Add(new UserRoleAssignment(user.Id,assigned.Id,DateTimeOffset.UtcNow)); await administration.SaveChangesAsync();
            users.Add(role,user.Id);
        }
        var excelType=new PropFlow.Modules.Apartments.Domain.ApartmentUnits.ApartmentUnitType("Loại Excel E2E",DateTimeOffset.UtcNow,75.5m,2,2);
        apartments.ApartmentUnitTypes.AddRange(excelType,new PropFlow.Modules.Apartments.Domain.ApartmentUnits.ApartmentUnitType("Loại Excel E2E phụ",DateTimeOffset.UtcNow,60m,1,1));await apartments.SaveChangesAsync();
        apartments.ApartmentUnits.Add(new PropFlow.Modules.Apartments.Domain.ApartmentUnits.ApartmentUnit("OTHER-E2E",1,DateTimeOffset.UtcNow,excelType.Id));
        await apartments.SaveChangesAsync();
        for(var i=0;i<101;i++)residents.Residents.Add(new PropFlow.Modules.Residents.Domain.Residents.Resident("EXCEL-"+i.ToString("D4"),"Excel Nguyễn "+i.ToString("D4"),DateTimeOffset.UtcNow,phoneNumber:"0900000"+i.ToString("D3"),email:$"fixture{i:D3}@example.invalid"));
        residents.Residents.Add(new PropFlow.Modules.Residents.Domain.Residents.Resident("OTHER-PERSON","Không thuộc bộ lọc",DateTimeOffset.UtcNow));await residents.SaveChangesAsync();
        // Nonempty, synthetic protected records make preservation checks stronger than zero row counts.
        var existingUnit=await apartments.ApartmentUnits.SingleAsync(x=>x.UnitNumber=="OTHER-E2E");
        var existingPerson=await residents.Residents.SingleAsync(x=>x.ResidentCode=="OTHER-PERSON");
        if ((Environment.GetEnvironmentVariable("PROPFLOW_FILTER_QA_FEATURES")??"").Split(',').Contains("remote"))
            residents.Residents.Add(new PropFlow.Modules.Residents.Domain.Residents.Resident("REMOTE-LINKED","Z Cư dân ký hợp đồng",DateTimeOffset.UtcNow,userId:users["RESIDENT"],email:"remote@example.invalid"));
        await residents.SaveChangesAsync();
        var now=DateTimeOffset.UtcNow;var today=DateOnly.FromDateTime(now.UtcDateTime);
        var ft1=new PropFlow.Modules.PropertyAssets.Domain.Facilities.FacilityType("QA Tiện ích A",users["MANAGER"],now);
        var ft2=new PropFlow.Modules.PropertyAssets.Domain.Facilities.FacilityType("QA Tiện ích B",users["MANAGER"],now);
        var et1=new PropFlow.Modules.PropertyAssets.Domain.Equipment.EquipmentType("QA Thiết bị A",users["MANAGER"],now);
        var et2=new PropFlow.Modules.PropertyAssets.Domain.Equipment.EquipmentType("QA Thiết bị B",users["MANAGER"],now);
        property.FacilityTypes.AddRange(ft1,ft2);property.EquipmentTypes.AddRange(et1,et2);
        property.FacilityTypes.AddRange(Enumerable.Range(1,10).Select(i=>new PropFlow.Modules.PropertyAssets.Domain.Facilities.FacilityType($"QA Danh mục tiện ích {i:D2}",users["MANAGER"],now)));
        property.EquipmentTypes.AddRange(Enumerable.Range(1,10).Select(i=>new PropFlow.Modules.PropertyAssets.Domain.Equipment.EquipmentType($"QA Danh mục thiết bị {i:D2}",users["MANAGER"],now)));
        var facilities=Enumerable.Range(1,24).Select(i=>new PropFlow.Modules.PropertyAssets.Domain.Facilities.Facility($"QA-F-{i:D2}",$"QA Tiện ích {i:D2}",now.AddMinutes(-i),i%2==0?ft1.Id:ft2.Id,locationDescription:"QA vị trí "+i,initialStatus:i>18?MasterDataStatus.INACTIVE:MasterDataStatus.ACTIVE)).ToArray();
        property.Facilities.AddRange(facilities);
        for(var i=1;i<=24;i++)
        {
            var item=new PropFlow.Modules.PropertyAssets.Domain.Equipment.Equipment($"QA-E-{i:D2}",$"QA Thiết bị {i:D2}",now.AddMinutes(-i),facilityId:i<=12?facilities[0].Id:i<=18?facilities[1].Id:null,equipmentTypeId:i%2==0?et1.Id:et2.Id,serialNumber:$"QA-SERIAL-{i:D2}");
            if(i>18)item.Deactivate(users["MANAGER"],now);property.Equipment.Add(item);
        }
        await property.SaveChangesAsync();
        // Fixture setup only: discard synthetic availability events from seeded assets before
        // taking the read-only baseline; these are not delivery records or business emails.
        await property.ResidentEvents.ExecuteDeleteAsync();
        apartments.ApartmentOwnerships.Add(new PropFlow.Modules.Apartments.Domain.ApartmentUnits.ApartmentOwnership(existingUnit.Id,existingPerson.Id,today.AddDays(-90),now,null));await apartments.SaveChangesAsync();
        var ended=new PropFlow.Modules.Residents.Domain.ResidentApartments.ResidentApartment(existingPerson.Id,existingUnit.Id,PropFlow.Modules.Residents.Domain.ResidentApartments.HouseholdRole.HOUSEHOLD_HEAD,PropFlow.Modules.Residents.Domain.ResidentApartments.ResidencyType.OWNER_OCCUPIED,today.AddDays(-60),now);
        ended.EndResidency(today.AddDays(-30),null,now);residents.ResidentApartments.Add(ended);await residents.SaveChangesAsync();
        residents.ResidentApartments.Add(new(existingPerson.Id,existingUnit.Id,PropFlow.Modules.Residents.Domain.ResidentApartments.HouseholdRole.HOUSEHOLD_HEAD,PropFlow.Modules.Residents.Domain.ResidentApartments.ResidencyType.OWNER_OCCUPIED,today.AddDays(-10),now));await residents.SaveChangesAsync();
        await using(var requests=Context<ServiceRequestsDbContext>(connection,"service_requests"))
        {
            var catA=new PropFlow.Modules.ServiceRequests.Domain.ServiceRequestCategories.ServiceRequestCategory("QA-A","QA Yêu cầu A",now);
            var catB=new PropFlow.Modules.ServiceRequests.Domain.ServiceRequestCategories.ServiceRequestCategory("QA-B","QA Yêu cầu B",now);
            requests.ServiceRequestCategories.AddRange(catA,catB);
            var residency=await residents.ResidentApartments.SingleAsync(x=>x.ResidentId==existingPerson.Id&&x.Status==PropFlow.Modules.Residents.Domain.ResidentApartments.ResidencyStatus.ACTIVE);
            for(var i=1;i<=24;i++)
            {
                var item=new PropFlow.Modules.ServiceRequests.Domain.ServiceRequests.ServiceRequest($"QA-REQ-{i:D2}",existingPerson.Id,residency.Id,$"QA Yêu cầu {i:D2}","Mô tả tổng hợp",now.AddMinutes(-i),apartmentUnitId:existingUnit.Id,categoryId:i%2==0?catA.Id:catB.Id,finalPriorityCode:i%2==0?"HIGH":"LOW");
                if(i>12)item.MarkUnderReview(now);
                requests.ServiceRequests.Add(item);
                if(i>12)requests.ServiceRequestAssignments.Add(new(item.Id,users["STAFF"],users["MANAGER"],now));
            }
            await requests.SaveChangesAsync();
        }
        await using var billing=Context<BillingDbContext>(connection,"billing");await using var payments=Context<PaymentsDbContext>(connection,"payments");
        var currentResidency=await residents.ResidentApartments.SingleAsync(x=>x.ResidentId==existingPerson.Id&&x.Status==PropFlow.Modules.Residents.Domain.ResidentApartments.ResidencyStatus.ACTIVE);
        await using(var complaints=Context<ComplaintsDbContext>(connection,"complaints"))
        {
            for(var i=1;i<=24;i++)
            {
                var item=new PropFlow.Modules.Complaints.Domain.Complaints.Complaint($"QA-C-{i:D2}",existingPerson.Id,currentResidency.Id,$"QA Phản ánh {i:D2}","Mô tả tổng hợp",now.AddMinutes(-i));
                if(i>12){item.MarkUnderReview(now);complaints.ComplaintFollowups.Add(new(item.Id,users["STAFF"],users["MANAGER"],"QA Xử lý",now));}
                complaints.Complaints.Add(item);
            }
            await complaints.SaveChangesAsync();
        }
        await using(var maintenance=Context<MaintenanceDbContext>(connection,"maintenance"))
        {
            for(var i=1;i<=125;i++)
            {
                var item=new PropFlow.Modules.Maintenance.Domain.MaintenanceSchedules.MaintenanceSchedule($"QA-S-{i:D3}",$"QA Lịch {i:D3}",now.AddHours(1),users["MANAGER"],now,facilities[0].Id,property.Equipment.Local.First().Id,description:"Lịch tổng hợp");
                if(i>100)item.Cancel(users["MANAGER"],now);maintenance.MaintenanceSchedules.Add(item);
            }
            for(var i=1;i<=24;i++)
            {
                var item=new PropFlow.Modules.Maintenance.Domain.MaintenanceTasks.MaintenanceTask($"QA-M-{i:D2}",$"QA Công việc {i:D2}",users["MANAGER"],now.AddMinutes(-i),facilityId:facilities[i%2].Id,description:"Tổng hợp",priorityCode:i%2==0?"HIGH":"LOW",plannedStartAt:now.AddHours(1),dueAt:now.AddDays(1));
                if(i>12){item.MarkAssigned(now);maintenance.MaintenanceAssignments.Add(new(item.Id,users["STAFF"],users["MANAGER"],now));}
                maintenance.MaintenanceTasks.Add(item);
            }
            await maintenance.SaveChangesAsync();await maintenance.ResidentEvents.ExecuteDeleteAsync();
        }
        var invoice=new PropFlow.Modules.Billing.Domain.Invoices.Invoice("QA-PROTECTED-INV",existingUnit.Id,today.AddDays(-10),today,users["MANAGER"],now);
        invoice.AddItem("QA_FIXED","Phí fixture tổng hợp","FIXED",1,1000,users["MANAGER"],now);
        invoice.Issue(users["MANAGER"],today,now,today.AddDays(10));billing.Invoices.Add(invoice);
        billing.InvoiceRecipients.Add(new(invoice.Id,null,existingPerson.FullName,null,existingUnit.UnitNumber,existingUnit.UsableAreaM2,existingPerson.Id,"OWNER"));
        billing.InvoiceStatusHistory.Add(new(invoice.Id,PropFlow.Modules.Billing.Domain.Invoices.InvoiceStatus.ISSUED,users["MANAGER"],now,PropFlow.Modules.Billing.Domain.Invoices.InvoiceStatus.DRAFT,"Synthetic fixture"));await billing.SaveChangesAsync();
        var payment=new PropFlow.Modules.Payments.Domain.Payments.Payment("QA-PROTECTED-PAY",invoice.Id,1000,"CASH",now,now,users["ACCOUNTANT"]);payments.Payments.Add(payment);
        payments.PaymentStatusHistory.Add(new(payment.Id,PropFlow.Modules.Payments.Domain.Payments.PaymentStatus.PENDING,users["ACCOUNTANT"],now));await payments.SaveChangesAsync();
        for(var monthIndex=0;monthIndex<12;monthIndex++)for(var typeIndex=0;typeIndex<2;typeIndex++)
        {
            var period=new DateOnly(today.Year,today.Month,1).AddMonths(-monthIndex);
            var sample=new PropFlow.Modules.Billing.Domain.Invoices.Invoice($"QA-INV-{monthIndex:D2}-{typeIndex}",existingUnit.Id,period,period.AddMonths(1).AddDays(-1),users["MANAGER"],now,invoiceType:typeIndex==0?PropFlow.Modules.Billing.Domain.Invoices.InvoiceType.SERVICE_FEE:PropFlow.Modules.Billing.Domain.Invoices.InvoiceType.MONTHLY_RENTAL);
            sample.AddItem("QA_DEMO","Hóa đơn tổng hợp","FIXED",1,1000,users["MANAGER"],now);sample.Issue(users["MANAGER"],today.AddDays(-10),now,today.AddDays(typeIndex==0?10:-1));
            billing.Invoices.Add(sample);billing.InvoiceRecipients.Add(new(sample.Id,null,"QA Hộ thử",null,existingUnit.UnitNumber,existingUnit.UsableAreaM2,existingPerson.Id,"OWNER"));await billing.SaveChangesAsync();
            if(typeIndex==1){var paid=new PropFlow.Modules.Payments.Domain.Payments.Payment($"QA-PAY-{monthIndex:D2}",sample.Id,monthIndex%2==0?500:1000,"CASH",now,now,users["ACCOUNTANT"]);paid.Confirm(users["ACCOUNTANT"],1000,now);payments.Payments.Add(paid);await payments.SaveChangesAsync();}
        }
        await using(var communication=Context<CommunicationDbContext>(connection,"communication"))
        {
            for(var i=1;i<=24;i++){var at=now.AddMinutes(-i);var announcement=new PropFlow.Modules.Communication.Domain.Announcements.Announcement($"QA Thông báo {i:D2}","Nội dung tổng hợp",users["MANAGER"],at);announcement.AddAllResidentsAudience(at);announcement.Publish(users["MANAGER"],at,"{}");communication.Announcements.Add(announcement);}await communication.SaveChangesAsync();
        }
    }
    private static async Task<Dictionary<string,string>> ProtectedFacts(string connection,CancellationToken ct)
    {
        await using var db=new NpgsqlConnection(connection);await db.OpenAsync(ct);var result=new Dictionary<string,string>();
        foreach(var table in new[]{"residents.residents","residents.resident_apartments","apartments.apartment_ownerships","billing.invoices","billing.invoice_items","billing.invoice_recipients","billing.invoice_status_history","payments.payments","payments.payment_status_history","communication.finance_emails","communication.notifications","communication.announcements","communication.announcement_versions","communication.announcement_audiences","property_assets.facilities","property_assets.equipment","property_assets.facility_types","property_assets.equipment_types","complaints.complaints","complaints.complaint_followups","maintenance.maintenance_schedules","maintenance.maintenance_tasks","maintenance.maintenance_assignments","service_requests.service_requests","service_requests.service_request_assignments"})
        {
            await using var query=new NpgsqlCommand($"SELECT count(*)::text || ':' || md5(coalesce(jsonb_agg(to_jsonb(t) ORDER BY t.id)::text,'[]')) FROM {table} t",db);
            result[table]=(string)(await query.ExecuteScalarAsync(ct))!;
        }
        return result;
    }
    private static int FreePort() { var socket=new TcpListener(IPAddress.Loopback,0); socket.Start(); var port=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();return port; }
    private static bool Listening(int port)=>System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(x=>x.Port==port);
    private static async Task Stop(Process? process)
    {
        if(process is null||process.HasExited)return;
        process.Kill(entireProcessTree:true);
        using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(15)); await process.WaitForExitAsync(stop.Token);
    }
}

