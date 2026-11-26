using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Administration.Domain.Permissions;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;
using PropFlow.Modules.Administration.Infrastructure;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

[Trait("UseCase", "FE-15.8")]
public sealed class SupportContactsHttpTests : IAsyncLifetime
{
    private readonly AuthDatabaseFixture database = new();
    private const string Admin = "/api/v1/administration/system-configuration/support-contacts";
    private const string Public = "/api/v1/public/support-contacts";
    public Task InitializeAsync() => database.InitializeAsync();
    public Task DisposeAsync() => database.DisposeAsync();

    private async Task<(HttpClient Client, Guid Id)> Actor(string role, WebApplicationFactory<Program>? factory = null)
    {
        factory ??= database.Factory;
        using var scope = factory.Services.CreateScope();
        var now = database.Factory.Clock.GetUtcNow();
        var auth = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var access = scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
        var user = new UserAccount("contact_" + Guid.NewGuid().ToString("N")[..12], "unused-test-hash", "Contact test", now, status: AccountStatus.ACTIVE, emailVerified: true);
        auth.UserAccounts.Add(user); await auth.SaveChangesAsync();
        var assigned = await access.Roles.Include(x => x.RolePermissions).ThenInclude(x => x.Permission).SingleAsync(x => x.Code == role);
        access.UserRoleAssignments.Add(new UserRoleAssignment(user.Id, assigned.Id, now)); await access.SaveChangesAsync();
        var account = new AccountResponse(user.Id,user.Username,user.DisplayName,null,null,"ACTIVE",true,role,assigned.RolePermissions.Select(x => x.Permission.Code).ToArray());
        var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await AuthenticationTestSessions.IssueAsync(scope.ServiceProvider,account,now)).AccessToken);
        return (client,user.Id);
    }

    [Fact]
    public async Task Admin_updates_public_consumers_persist_across_host_restart_and_other_roles_cannot_write()
    {
        using var anonymous = database.Factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var empty = await anonymous.GetFromJsonAsync<PublicSupportContacts>(Public);
        Assert.Equal(new PublicSupportContacts(null,null), empty);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync(Admin,new UpdateSupportContacts())).StatusCode);
        foreach (var role in new[] { "MANAGER", "STAFF", "ACCOUNTANT", "RESIDENT" })
        {
            var actor = await Actor(role); using var forbidden = actor.Client;
            Assert.Equal(HttpStatusCode.Forbidden,(await forbidden.GetAsync(Admin)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await forbidden.PutAsJsonAsync(Admin,new UpdateSupportContacts())).StatusCode);
        }
        var owner = await Actor("ADMIN"); using var admin = owner.Client;
        var before = (await admin.GetFromJsonAsync<SupportContactsState>(Admin))!;
        var request = new UpdateSupportContacts { Email=" support@example.invalid ",Phone="+84 (24) 1234-5678",ExpectedVersion=before.Version };
        var saved = await admin.PutAsJsonAsync(Admin,request);
        Assert.Equal(HttpStatusCode.OK,saved.StatusCode);
        var after = (await saved.Content.ReadFromJsonAsync<SupportContactsState>())!;
        Assert.NotEqual(before.Version,after.Version);
        Assert.Equal("support@example.invalid",after.Email); Assert.Equal("+842412345678",after.Phone);
        var publicResponse = await anonymous.GetAsync(Public);
        Assert.True(publicResponse.Headers.CacheControl!.NoStore);
        var json = await publicResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] {"email","phone"},json.EnumerateObject().Select(x=>x.Name).OrderBy(x=>x).ToArray());
        Assert.Equal(after.Email,json.GetProperty("email").GetString());
        // A new application host/service provider against the owned fixture DB proves persistence beyond process memory.
        await using(var restarted = database.Factory.WithWebHostBuilder(_=>{}))
        using(var publicAfterRestart = restarted.CreateClient(new() {BaseAddress=new Uri("https://localhost")}))
            Assert.Equal(new PublicSupportContacts(after.Email,after.Phone),await publicAfterRestart.GetFromJsonAsync<PublicSupportContacts>(Public));
        using (var scope=database.Factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
            var logs=await db.AuditLogs.Where(x=>x.EntityType=="SystemConfiguration").ToArrayAsync();
            Assert.Equal(2,logs.Length);
            Assert.All(logs,x=>Assert.Equal(owner.Id,x.ActorUserId));
            Assert.Contains(logs,x=>x.NewValues!.Contains("support.contact_email")&&x.NewValues.Contains(after.Email!));
            Assert.All(logs,x=>Assert.False(string.IsNullOrEmpty(x.CorrelationId)));
        }
        request.ExpectedVersion=after.Version;
        var noOp=(await (await admin.PutAsJsonAsync(Admin,request)).Content.ReadFromJsonAsync<SupportContactsState>())!;
        Assert.Equal(after.Version,noOp.Version);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync(Admin,new {email="bad",phone="123",expectedVersion=after.Version})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync(Admin,new {email=after.Email,phone=after.Phone,expectedVersion=after.Version,smtpHost="evil.invalid"})).StatusCode);
        using(var scope=database.Factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
            Assert.Equal(2,await db.AuditLogs.CountAsync(x=>x.EntityType=="SystemConfiguration"));
        }
        using(var revoked = database.Factory.CreateClient(new() {BaseAddress=new Uri("https://localhost")}))
        {
            revoked.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-session.invalid-signature.invalid");
            Assert.Equal(HttpStatusCode.Unauthorized,(await revoked.PutAsJsonAsync(Admin,request)).StatusCode);
        }
        var cleared=await admin.PutAsJsonAsync(Admin,new UpdateSupportContacts { ExpectedVersion=after.Version });
        Assert.Equal(HttpStatusCode.OK,cleared.StatusCode);
        Assert.Equal(new PublicSupportContacts(null,null),await anonymous.GetFromJsonAsync<PublicSupportContacts>(Public));

        // ADMIN role without the fixed permission must not be sufficient.
        using(var scope=database.Factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
            db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(x=>x.Permission.Code==SystemPermissionCodes.ManageSystemConfiguration));
            await db.SaveChangesAsync();
        }
        var unprivileged=await Actor("ADMIN"); using var noPermission=unprivileged.Client;
        Assert.Equal(HttpStatusCode.Forbidden,(await noPermission.GetAsync(Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await noPermission.PutAsJsonAsync(Admin,request)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_updates_are_atomic_and_stale_version_is_rejected_including_first_creation()
    {
        var one=await Actor("ADMIN"); using var a=one.Client;
        var two=await Actor("ADMIN"); using var b=two.Client;
        var initial=(await a.GetFromJsonAsync<SupportContactsState>(Admin))!;
        var responses=await Task.WhenAll(a.PutAsJsonAsync(Admin,new UpdateSupportContacts { Email="first@example.invalid",Phone="19001111",ExpectedVersion=initial.Version }),
            b.PutAsJsonAsync(Admin,new UpdateSupportContacts { Email="second@example.invalid",Phone="19002222",ExpectedVersion=initial.Version }));
        Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.OK);
        Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.Conflict);
        var current=(await a.GetFromJsonAsync<SupportContactsState>(Admin))!;
        Assert.True((current.Email,current.Phone) is ("first@example.invalid","19001111") or ("second@example.invalid","19002222"));
        // A stale client cannot clear a newer value.
        Assert.Equal(HttpStatusCode.Conflict,(await a.PutAsJsonAsync(Admin,new UpdateSupportContacts {ExpectedVersion=initial.Version})).StatusCode);
        var updates = await Task.WhenAll(
            a.PutAsJsonAsync(Admin, new UpdateSupportContacts { Email="updated-one@example.invalid", Phone="19003333", ExpectedVersion=current.Version }),
            b.PutAsJsonAsync(Admin, new UpdateSupportContacts { Email="updated-two@example.invalid", Phone="19004444", ExpectedVersion=current.Version }));
        Assert.Single(updates, x=>x.StatusCode==HttpStatusCode.OK);
        Assert.Single(updates, x=>x.StatusCode==HttpStatusCode.Conflict);
        var updated = (await a.GetFromJsonAsync<SupportContactsState>(Admin))!;
        Assert.True((updated.Email,updated.Phone) is ("updated-one@example.invalid","19003333") or ("updated-two@example.invalid","19004444"));
        foreach(var response in updates) response.Dispose();
        using var scope=database.Factory.Services.CreateScope();
        Assert.Equal(2,await scope.ServiceProvider.GetRequiredService<AdministrationDbContext>().SystemConfigurations.CountAsync());
        foreach(var r in responses)r.Dispose();
    }

    [Fact]
    public async Task Audit_failure_rolls_back_both_contact_values_and_version()
    {
        var actor=await Actor("ADMIN"); using var admin=actor.Client;
        var initial=(await admin.GetFromJsonAsync<SupportContactsState>(Admin))!;
        await admin.PutAsJsonAsync(Admin,new UpdateSupportContacts {Email="before@example.invalid",Phone="19001111",ExpectedVersion=initial.Version});
        var before=(await admin.GetFromJsonAsync<SupportContactsState>(Admin))!;
        await using var broken=database.Factory.WithWebHostBuilder(builder=>builder.ConfigureTestServices(services=>
        {
            services.AddScoped<SystemAuditWriter>();services.RemoveAll<ISystemAuditWriter>();
            services.AddScoped<ISystemAuditWriter>(sp=>new FailAudit(sp.GetRequiredService<SystemAuditWriter>()));
        }));
        var badActor=await Actor("ADMIN",broken); using var client=badActor.Client;
        Assert.Equal(HttpStatusCode.InternalServerError,(await client.PutAsJsonAsync(Admin,new UpdateSupportContacts {Email="after@example.invalid",Phone="19002222",ExpectedVersion=before.Version})).StatusCode);
        Assert.Equal(before,await admin.GetFromJsonAsync<SupportContactsState>(Admin));
    }
    [Fact]
    public async Task Migration_preserves_existing_configuration_and_other_module_data_on_owned_fixture()
    {
        using var scope = database.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
        var connectionString = db.Database.GetConnectionString()!;
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.StartsWith("propflow_fe01_test_", target.Database);
        await TestDatabaseSafety.VerifyFixtureAsync(connectionString, target.Database!, TestDatabaseSafety.ProcessOwner,
            TestDatabaseConfiguration.GetConnectionString());
        const string migration = "20261124082235_AddSupportContactConfiguration";
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(migration, applied[^1]);
        var migrator = db.GetService<IMigrator>();
        // This fresh, guarded fixture contains synthetic data only. No application/demo DB is migrated.
        await migrator.MigrateAsync(applied[^2]);
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO administration.system_configurations (id,config_key,config_value) VALUES ({id},'unrelated.fixture.setting','preserved synthetic fixture value')");
        async Task<string> Snapshot()
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT jsonb_build_object('configurations',(SELECT jsonb_agg(to_jsonb(t)-'version' ORDER BY t.id) FROM administration.system_configurations t),'buildings',(SELECT jsonb_agg(to_jsonb(t) ORDER BY t.id) FROM property_assets.buildings t))::text", connection);
            return (string)(await command.ExecuteScalarAsync())!;
        }
        // A logical snapshot backup is sufficient for these disposable, synthetic fixture rows.
        var backup = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(backup, await Snapshot());
            Assert.NotEmpty(await File.ReadAllTextAsync(backup));
            await migrator.MigrateAsync(migration);
            Assert.Equal(await File.ReadAllTextAsync(backup), await Snapshot());
            db.ChangeTracker.Clear();
            var legacy = await db.SystemConfigurations.SingleAsync(x=>x.Id==id);
            Assert.NotEqual(Guid.Empty, legacy.Version);
            Assert.Equal("preserved synthetic fixture value", legacy.ConfigValue);
            Assert.Equal(1, await db.RolePermissions.CountAsync(x=>x.Permission.Code==SystemPermissionCodes.ManageSystemConfiguration));
        }
        finally { File.Delete(backup); }
    }

    private sealed class FailAudit(ISystemAuditWriter inner):ISystemAuditWriter
    {
        public Task AppendAsync(SystemAuditEvent entry,CancellationToken ct)=>inner.AppendAsync(entry,ct);
        public Task AppendInTransactionAsync(SystemAuditEvent entry,DbConnection connection,DbTransaction tx,CancellationToken ct)=>throw new InvalidOperationException("Injected test-only audit failure");
    }
}
