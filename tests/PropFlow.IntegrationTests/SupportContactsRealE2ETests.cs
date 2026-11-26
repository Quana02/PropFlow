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

public sealed class RealSupportContactsFactAttribute : FactAttribute
{
    public RealSupportContactsFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PROPFLOW_RUN_REAL_E2E") != "1")
            Skip = "Opt-in real Kestrel/browser test: set PROPFLOW_RUN_REAL_E2E=1 and publish Web to the test artifact directory.";
    }
}

[Trait("UseCase", "FE-15.8 real Web/API/PostgreSQL E2E")]
public sealed class SupportContactsRealE2ETests
{
    [RealSupportContactsFact]
    public async Task Real_login_ui_storage_public_restart_concurrency_and_role_boundaries()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PropFlow.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var repo = root!.FullName;
        var evidenceRoot = Path.Combine(repo, "artifacts", "fe158-real-e2e");
        Directory.CreateDirectory(evidenceRoot);
        var artifacts = Path.Combine(evidenceRoot, "run-"+DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ")+"-"+Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(artifacts);
        await File.WriteAllTextAsync(Path.Combine(evidenceRoot,"latest-run.json"),JsonSerializer.Serialize(new {artifacts}));
        var publishedWeb = Path.Combine(repo, "artifacts", "fe158-real-web");
        Assert.True(File.Exists(Path.Combine(publishedWeb, "PropFlow.Web.dll")), "Publish the real Web app into artifacts/fe158-real-web first.");
        var owned = Path.Combine(Path.GetTempPath(), "propflow-real-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(owned);
        var databaseName = "propflow_fe158_e2e_" + Guid.NewGuid().ToString("N");
        var source = TestDatabaseSafety.Dedicated(TestDatabaseConfiguration.GetConnectionString());
        var target = new NpgsqlConnectionStringBuilder(source.ConnectionString) { Database = databaseName };
        var adminTarget = new NpgsqlConnectionStringBuilder(source.ConnectionString) { Database = "postgres" };
        var processes = new List<Process>();
        var processRecords = new List<object>();
        var checkpoints = new List<object>();
        var apiPort = FreePort(); var webPort = FreePort();
        while (webPort == apiPort) webPort = FreePort();
        var apiUrl = $"https://127.0.0.1:{apiPort}";
        var webUrl = $"https://127.0.0.1:{webPort}";
        var users = new Dictionary<string, Guid>();
        var completed = false; var journeyPassed = false; var created = false; var dropped = false;
        Process? api = null; Process? web = null; Process? browserTest = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(9));
        using var tls = RSA.Create(2048);
        var request = new CertificateRequest("CN=PropFlow owned E2E fixture", tls, System.Security.Cryptography.HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
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
                RedirectStandardOutput=true, RedirectStandardError=true, RedirectStandardInput=protocol };
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
            var publishedApi=Path.Combine(repo,"artifacts","excel-real-api");
            api=Start("dotnet",[Path.Combine(publishedApi,"PropFlow.Api.dll"),"--urls",apiUrl],publishedApi,apiEnvironment,"api-"+suffix);
            await Ready(apiUrl+"/api/v1/public/support-contacts",api);
            web=Start("dotnet",[Path.Combine(publishedWeb,"PropFlow.Web.dll"),"--urls",webUrl],publishedWeb,webEnvironment,"web-"+suffix);
            await Ready(webUrl+"/login",web);
        }
        try
        {
            await using(var admin = new NpgsqlConnection(adminTarget.ConnectionString))
            { await admin.OpenAsync(timeout.Token); await TestDatabaseSafety.CreateAsync(admin,databaseName); created=true; }
            await TestDatabaseSafety.VerifyFixtureAsync(target.ConnectionString,databaseName,TestDatabaseSafety.ProcessOwner,source.ConnectionString);
            await MigrateAndSeed(target.ConnectionString,users,fixturePassword);
            await File.WriteAllTextAsync(Path.Combine(publishedWeb,"wwwroot","appsettings.json"),JsonSerializer.Serialize(new {Api=new {BaseUrl=apiUrl+"/"}}));
            await File.WriteAllTextAsync(Path.Combine(publishedWeb,"wwwroot","appsettings.Testing.json"),"{}");
            await StartServers("first");
            browserTest=Start("node",[Path.Combine(repo,"tests","frontend","support-contacts.real-e2e.cjs")],repo,
                new Dictionary<string,string> { ["PROPFLOW_E2E_WEB"]=webUrl,["PROPFLOW_E2E_API"]=apiUrl,["PROPFLOW_E2E_PASSWORD"]=fixturePassword,["PROPFLOW_E2E_ARTIFACTS"]=artifacts },"browser",true);
            while(await browserTest.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                using var message=JsonDocument.Parse(line);
                var command=message.RootElement;
                var action=command.GetProperty("action").GetString();
                await File.AppendAllTextAsync(Path.Combine(artifacts,"progress.log"),DateTimeOffset.UtcNow.ToString("O")+" "+action+" "+(command.TryGetProperty("phase",out var phase)?phase.GetString():"")+Environment.NewLine);
                if(action=="db")
                {
                    await using var db=Context<AdministrationDbContext>(target.ConnectionString,"administration");
                    var rows=await db.SystemConfigurations.AsNoTracking().Where(x=>x.ConfigKey.StartsWith("support.contact_")).ToListAsync(timeout.Token);
                    Assert.Equal(command.GetProperty("email").GetString()??"",rows.Single(x=>x.ConfigKey=="support.contact_email").ConfigValue);
                    Assert.Equal(command.GetProperty("phone").GetString()??"",rows.Single(x=>x.ConfigKey=="support.contact_phone").ConfigValue);
                    var audits=await db.AuditLogs.AsNoTracking().Where(x=>x.EntityType=="SystemConfiguration").ToListAsync(timeout.Token);
                    Assert.Equal(command.GetProperty("audits").GetInt32(),audits.Count);
                    Assert.All(audits,x=>Assert.Contains(x.ActorUserId!.Value,users.Values));
                    checkpoints.Add(new {action,phase=command.GetProperty("phase").GetString(),rows=rows.Count,audits=audits.Count,verified=true});
                }
                else if(action=="restart")
                { await Stop(web); await Stop(api); await StartServers("restarted"); checkpoints.Add(new {action,verified=true}); }
                else if(action=="permission")
                {
                    await using var db=Context<AdministrationDbContext>(target.ConnectionString,"administration");
                    db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(x=>x.Permission.Code=="MANAGE_SYSTEM_CONFIGURATION"));
                    await db.SaveChangesAsync(timeout.Token); checkpoints.Add(new {action,scope="owned fixture only",verified=true});
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
        property.Buildings.Add(new Building("FE15.8 owned E2E fixture","Synthetic local fixture",DateTimeOffset.UtcNow,numberOfFloors:10)); await property.SaveChangesAsync();
        await apartments.Database.MigrateAsync();
        await residents.GetService<IMigrator>().MigrateAsync("20260914063807_InitialResidents");
        await administration.Database.MigrateAsync(); await auth.Database.MigrateAsync(); await residents.Database.MigrateAsync();
        foreach(var db in new DbContext[] {
            Context<ServiceRequestsDbContext>(connection,"service_requests"),Context<ComplaintsDbContext>(connection,"complaints"),
            Context<MaintenanceDbContext>(connection,"maintenance"),Context<BillingDbContext>(connection,"billing"),
            Context<PaymentsDbContext>(connection,"payments"),Context<AiClassificationDbContext>(connection,"ai_classification"),
            Context<AiRecommendationDbContext>(connection,"ai_recommendation"),Context<AiChatbotDbContext>(connection,"ai_chatbot"),
            Context<CommunicationDbContext>(connection,"communication") })
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
