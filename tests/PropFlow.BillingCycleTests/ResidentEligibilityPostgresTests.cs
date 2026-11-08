using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;
using PropFlow.Modules.Apartments.Infrastructure;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Domain.ResidentApartments;
using PropFlow.Modules.Residents.Infrastructure;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Domain.Users;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PropFlow.BillingCycleTests;
public sealed class ResidentEligibilityPostgresTests : IAsyncLifetime
{
    private readonly string name = "propflow_cycle_test_" + Guid.NewGuid().ToString("N");
    private string connection = "", admin = "";
    public async Task InitializeAsync() {
        var b = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PROPFLOW_TEST_DATABASE") ?? throw new InvalidOperationException("Missing test connection")) { Database = "postgres" }; admin = b.ConnectionString;
        await using var c = new NpgsqlConnection(admin); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE {name}", c); await cmd.ExecuteNonQueryAsync(); b.Database = name; connection = b.ConnectionString;
        await using var a = Apartments(); await a.Database.MigrateAsync();
        await using var auth = Accounts();
        await auth.GetService<IMigrator>().MigrateAsync("20260914101250_CorrectAuthenticationEmailOtpVerification");
        await using var r = Residents();
        await r.GetService<IMigrator>().MigrateAsync("20260914063807_InitialResidents");
        await auth.Database.MigrateAsync(); await r.Database.MigrateAsync();
    }
    public async Task DisposeAsync() { if(admin.Length == 0) return; NpgsqlConnection.ClearAllPools(); await using var c = new NpgsqlConnection(admin); await c.OpenAsync(); await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {name} WITH (FORCE)", c); await cmd.ExecuteNonQueryAsync(); }
    private ApartmentsDbContext Apartments() => new(new DbContextOptionsBuilder<ApartmentsDbContext>().UseNpgsql(connection, x => x.MigrationsHistoryTable("__EFMigrationsHistory", "apartments")).Options);
    private ResidentsDbContext Residents() => new(new DbContextOptionsBuilder<ResidentsDbContext>().UseNpgsql(connection, x => x.MigrationsHistoryTable("__EFMigrationsHistory", "residents")).Options);
    private AuthenticationDbContext Accounts() => new(new DbContextOptionsBuilder<AuthenticationDbContext>().UseNpgsql(connection, x => x.MigrationsHistoryTable("__EFMigrationsHistory", "auth")).Options);
    [Fact]
    public async Task Resident_search_and_relationship_filters_share_total_and_pages_without_changing_relationships()
    {
        await using var a=Apartments();await using var r=Residents();var now=DateTimeOffset.UtcNow;var date=DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var type=new ApartmentUnitType("Paging test",now);a.ApartmentUnitTypes.Add(type);var unit=new ApartmentUnit("FILTER-A",1,now,type.Id,70);a.ApartmentUnits.Add(unit);await a.SaveChangesAsync();
        var head=new Resident("HEAD","Head",now);r.Residents.Add(head);
        var people=Enumerable.Range(1,23).Select(i=>new Resident($"MATCH-{i:00}","Same matching name",now,email:$"m{i}@example.invalid")).ToArray();
        r.Residents.AddRange(people);r.Residents.Add(new Resident("OTHER","Unrelated",now));await r.SaveChangesAsync();
        var headResidency=new ResidentApartment(head.Id,unit.Id,HouseholdRole.HOUSEHOLD_HEAD,ResidencyType.TENANT,date.AddDays(-1),now);r.ResidentApartments.Add(headResidency);await r.SaveChangesAsync();
        r.ResidentApartments.AddRange(people.Select(person=>new ResidentApartment(person.Id,unit.Id,HouseholdRole.HOUSEHOLD_MEMBER,ResidencyType.TENANT,date.AddDays(-1),now,headResidency.Id,HouseholdRelationship.OTHER_RELATIVE)));await r.SaveChangesAsync();
        var controller=new PropFlow.Modules.Residents.Presentation.ResidentsController(r,null!,new ApartmentResidentRelationshipSource(a),null!,null!,null!,TimeProvider.System);
        controller.ControllerContext=new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext=new Microsoft.AspNetCore.Http.DefaultHttpContext { User=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role,"MANAGER")],"test")) } };
        async Task<PropFlow.Modules.Residents.Presentation.PagedResidentsResponse> List(int page,string term="matching",HouseholdRole role=HouseholdRole.HOUSEHOLD_MEMBER) {
            var result=await controller.List(term,ResidentStatus.ACTIVE,unit.Id,PropFlow.Modules.Residents.Presentation.ResidentApartmentRelationshipKind.RESIDENT_ONLY,ResidencyType.TENANT,role,page,20,default);
            return Assert.IsType<PropFlow.Modules.Residents.Presentation.PagedResidentsResponse>(Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result.Result).Value);
        }
        var first=await List(1);var second=await List(2);Assert.Equal(23,first.TotalCount);Assert.Equal(23,second.TotalCount);Assert.Equal(20,first.Items.Count);Assert.Equal(3,second.Items.Count);
        Assert.Empty(first.Items.Select(x=>x.Id).Intersect(second.Items.Select(x=>x.Id)));
        Assert.Equal(2,(await List(999)).PageIndex);
        var empty=await List(2,"missing");Assert.Equal(0,empty.TotalCount);Assert.Empty(empty.Items);Assert.Equal(1,empty.PageIndex);
        Assert.Empty((await List(1,role:HouseholdRole.HOUSEHOLD_HEAD)).Items);
        Assert.Equal(24,await r.ResidentApartments.CountAsync());Assert.Equal(25,await r.Residents.CountAsync());
        var otherUnit=new ApartmentUnit("FILTER-B",1,now,type.Id,70);a.ApartmentUnits.Add(otherUnit);await a.SaveChangesAsync();
        var cross=new Resident("CROSS","Cross relationships",now);r.Residents.Add(cross);await r.SaveChangesAsync();
        r.ResidentApartments.AddRange(new ResidentApartment(cross.Id,unit.Id,HouseholdRole.HOUSEHOLD_MEMBER,ResidencyType.TENANT,date.AddDays(-1),now,headResidency.Id,HouseholdRelationship.OTHER_RELATIVE),
            new ResidentApartment(cross.Id,otherUnit.Id,HouseholdRole.HOUSEHOLD_HEAD,ResidencyType.AUTHORIZED_OCCUPANT,date.AddDays(-1),now));await r.SaveChangesAsync();
        var combined=await controller.List("Cross",ResidentStatus.ACTIVE,null,null,ResidencyType.TENANT,HouseholdRole.HOUSEHOLD_HEAD,1,20,default);
        var combinedData=Assert.IsType<PropFlow.Modules.Residents.Presentation.PagedResidentsResponse>(Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(combined.Result).Value);
        Assert.Empty(combinedData.Items);Assert.Equal(0,combinedData.TotalCount);

    }
    [Fact]
    public async Task Water_counts_residents_without_accounts_at_day_one_and_excludes_late_entries_and_prior_departures()
    {
        await using var a = Apartments(); await using var r = Residents(); await using var auth = Accounts();
        var now = DateTimeOffset.UtcNow;
        var date = new DateOnly(2026, 10, 1);
        var type = new ApartmentUnitType("Water count test", now); a.ApartmentUnitTypes.Add(type);
        var unit = new ApartmentUnit("W-COUNT", 1, now, type.Id, 70); a.ApartmentUnits.Add(unit); await a.SaveChangesAsync();
        var unitId = unit.Id;
        var people = Enumerable.Range(0, 5).Select(x => new Resident("R-COUNT-" + x, "Count " + x, now)).ToArray();
        r.Residents.AddRange(people); await r.SaveChangesAsync();
        var head = new ResidentApartment(people[0].Id, unitId, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT, date.AddDays(-20), now);
        r.ResidentApartments.Add(head); await r.SaveChangesAsync();
        ResidentApartment Member(int index, DateOnly start) => new(people[index].Id, unitId, HouseholdRole.HOUSEHOLD_MEMBER, ResidencyType.TENANT, start, now, head.Id, HouseholdRelationship.CHILD);
        var leavingLater = Member(1, date.AddDays(-10)); leavingLater.EndResidency(date.AddDays(10), null, now);
        var firstDay = Member(2, date);
        var late = Member(3, date.AddDays(1));
        var gone = Member(4, date.AddDays(-10)); gone.EndResidency(date.AddDays(-1), null, now);
        r.ResidentApartments.AddRange(leavingLater, firstDay, late, gone); await r.SaveChangesAsync();
        var directory = new PropFlow.Api.Composition.BillingHouseholdDirectory(r, a, auth);
        var counts = await directory.ResidentCountsAsync([unitId, Guid.NewGuid()], date, default);
        Assert.Equal(3, counts[unitId]); Assert.Single(counts);
        Assert.All(people, person => Assert.Null(person.UserId));
        Assert.Equal(1, (await directory.ResidentCountsAsync([unitId], date.AddDays(-15), default))[unitId]);
    }
    [Theory]
    [InlineData(1, false, false, false, true)]
    [InlineData(2, false, false, false, true)]
    [InlineData(0, true, false, false, true)]
    [InlineData(0, true, true, false, false)]
    [InlineData(1, true, true, false, true)]
    [InlineData(0, false, true, true, false)]
    [InlineData(0, false, true, false, false)]
    [InlineData(0, false, false, false, false)]
    public async Task Registration_and_linking_use_person_level_ownership_or_ownerless_head(int ownedCount, bool head, bool otherOwner, bool tenantMember, bool eligible)
    {
        await using var a = Apartments(); await using var r = Residents(); var now = DateTimeOffset.UtcNow; var date = DateOnly.FromDateTime(now.UtcDateTime);
        var person = new Resident("R-1", "Candidate", null, null, null, "CCCD", "123456789012", null, null, now, phoneNumber:"0901234567", email:"candidate@example.invalid");
        var other = new Resident("R-2", "Other head", now); r.Residents.AddRange(person, other); await r.SaveChangesAsync();
        var type = new ApartmentUnitType("Test", now); a.ApartmentUnitTypes.Add(type);
        var unit = new ApartmentUnit("A-1",1,now,type.Id,70); a.ApartmentUnits.Add(unit);
        for(var n = 0; n < ownedCount; n++) { var owned = new ApartmentUnit("OWN-" + n,1,now,type.Id,70); a.ApartmentUnits.Add(owned); a.ApartmentOwnerships.Add(new(owned.Id,person.Id,date.AddDays(-1),now,null)); }
        if(otherOwner) a.ApartmentOwnerships.Add(new(unit.Id,other.Id,date.AddDays(-1),now,null));
        await a.SaveChangesAsync();
        if(head) r.ResidentApartments.Add(new(person.Id,unit.Id,HouseholdRole.HOUSEHOLD_HEAD,ResidencyType.TENANT,date.AddDays(-1),now));
        else if(otherOwner || tenantMember) { var h = new ResidentApartment(other.Id,unit.Id,HouseholdRole.HOUSEHOLD_HEAD,ResidencyType.AUTHORIZED_OCCUPANT,date.AddDays(-1),now); r.ResidentApartments.Add(h);
            r.ResidentApartments.Add(new(person.Id,unit.Id,HouseholdRole.HOUSEHOLD_MEMBER,tenantMember ? ResidencyType.TENANT : ResidencyType.AUTHORIZED_OCCUPANT,date.AddDays(-1),now,h.Id,HouseholdRelationship.OTHER_RELATIVE)); }
        await r.SaveChangesAsync();
        var onboarding = new ResidentOnboarding(r,new ApartmentResidentRelationshipSource(a));
        var candidate = await onboarding.FindRegistrationCandidateAsync("candidate@example.invalid","0901234567","CCCD","123456789012",date,default);
        Assert.Equal(eligible,candidate is not null);
        Assert.Equal(eligible,(await onboarding.RevalidateRegistrationCandidateAsync(person.Id,date,default)) is not null);
        if(candidate is not null) {
            await using var accounts = Accounts(); var account = new UserAccount("candidate", "test-only", "Candidate",now); accounts.UserAccounts.Add(account); await accounts.SaveChangesAsync();
            Assert.True(await onboarding.LinkEligibleAsync(candidate,account.Id,date,now,default));
            Assert.Null(await onboarding.FindRegistrationCandidateAsync("candidate@example.invalid","0901234567","CCCD","123456789012",date,default));
        } else {
            var forged = new PropFlow.Modules.Residents.Contracts.RegistrationResidentCandidate(person.Id,unit.Id,"candidate@example.invalid","0901234567","CCCD","123456789012");
            Assert.False(await onboarding.LinkEligibleAsync(forged,Guid.NewGuid(),date,now,default));
            await using var factory=new RegistrationFactory(connection);
            using var client=factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress=new Uri("https://localhost") });
            var response=await client.PostAsJsonAsync("api/v1/auth/register",new PropFlow.Modules.Authentication.Contracts.RegisterRequest("candidate","Candidate","candidate@example.invalid","Secure test password 123!","0901234567","CCCD","123456789012"));
            Assert.Equal(System.Net.HttpStatusCode.OK,response.StatusCode);
            var challenge=await response.Content.ReadFromJsonAsync<PropFlow.Modules.Authentication.Contracts.ChallengeResponse>();
            Assert.Equal(Guid.Empty,challenge!.ChallengeId);
            await using var accounts=Accounts(); Assert.Equal(0,await accounts.UserAccounts.CountAsync());
        }
        Assert.Equal(2,await r.Residents.CountAsync());
    }
    private sealed class RegistrationFactory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_,config)=> { using var rsa=RSA.Create(2048); config.AddInMemoryCollection(new Dictionary<string,string?> {
                ["ConnectionStrings:PropFlowDatabase"]=connection,["Authentication:Issuer"]="eligibility-tests",["Authentication:Audience"]="eligibility-tests",
                ["Authentication:PrivateKeyPem"]=rsa.ExportRSAPrivateKeyPem(),["Authentication:OtpHashKey"]=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ["Authentication:Mail:Host"]="invalid.test",["Authentication:Mail:From"]="test@example.invalid",["Logging:LogLevel:Default"]="None"
            }); });
            builder.ConfigureTestServices(services=> {
                foreach(var service in services.Where(x=>x.ServiceType==typeof(IHostedService) && x.ImplementationType is not null
                    && typeof(BackgroundService).IsAssignableFrom(x.ImplementationType)).ToArray()) services.Remove(service);
            });
        }
    }
    [Fact]
    public async Task Billing_directory_keeps_ownership_and_rental_capacity_separate_for_the_same_person()
    {
        await using var a=Apartments(); await using var r=Residents(); await using var accounts=Accounts();
        var now=DateTimeOffset.UtcNow; var date=DateOnly.FromDateTime(now.UtcDateTime); var start=new DateOnly(date.Year,date.Month,1);
        var account=new UserAccount("qualified","test-only","Qualified",now,"qualified@example.invalid",status:AccountStatus.ACTIVE); accounts.UserAccounts.Add(account); await accounts.SaveChangesAsync();
        var person=new Resident("R-1","Owner and renter",now,account.Id,email:"qualified@example.invalid");
        var other=new Resident("R-2","Other owner",now,email:"other@example.invalid"); r.Residents.AddRange(person,other); await r.SaveChangesAsync();
        var type=new ApartmentUnitType("Test",now); a.ApartmentUnitTypes.Add(type);
        var owned=new ApartmentUnit("A-1",1,now,type.Id,70); var rental=new ApartmentUnit("B-1",1,now,type.Id,80); var unrelated=new ApartmentUnit("C-1",1,now,type.Id,90);
        a.ApartmentUnits.AddRange(owned,rental,unrelated); await a.SaveChangesAsync();
        a.ApartmentOwnerships.AddRange(new(owned.Id,person.Id,start.AddDays(-1),now,null),new(unrelated.Id,other.Id,start.AddDays(-1),now,null)); await a.SaveChangesAsync();
        r.ResidentApartments.AddRange(new(person.Id,rental.Id,HouseholdRole.HOUSEHOLD_HEAD,ResidencyType.TENANT,start,now),
            new(person.Id,unrelated.Id,HouseholdRole.HOUSEHOLD_HEAD,ResidencyType.TENANT,start,now)); await r.SaveChangesAsync();
        var directory=new PropFlow.Api.Composition.BillingHouseholdDirectory(r,a,accounts);
        var contexts=await directory.ListChargeableAsync(date,default);
        Assert.Equal("OWNER",Assert.Single(contexts.Where(x=>x.ApartmentId==owned.Id)).Capacity);
        Assert.Equal("RENTAL",Assert.Single(contexts.Where(x=>x.ApartmentId==rental.Id)).Capacity);
        Assert.Equal(other.Id,Assert.Single(contexts.Where(x=>x.ApartmentId==unrelated.Id)).ResidentId);
        Assert.DoesNotContain(contexts,x=>x.ApartmentId==unrelated.Id && x.UserId==account.Id);
        Assert.Equal(2,contexts.Count(x=>x.UserId==account.Id));
        Assert.Equal(1,await accounts.UserAccounts.CountAsync());
    }
    [Fact]
    public async Task Ownership_change_rechecks_pending_registration_without_deleting_existing_person_or_account()
    {
        await using var a = Apartments(); await using var r = Residents(); var now = DateTimeOffset.UtcNow; var date = DateOnly.FromDateTime(now.UtcDateTime);
        var person = new Resident("R-1","Head",null,null,null,"CCCD","123456789012",null,null,now,phoneNumber:"0901234567",email:"head@example.invalid");
        var owner = new Resident("R-2","New owner",now); r.Residents.AddRange(person,owner); await r.SaveChangesAsync();
        var type = new ApartmentUnitType("Test",now); a.ApartmentUnitTypes.Add(type); var unit = new ApartmentUnit("A-1",1,now,type.Id,70); a.ApartmentUnits.Add(unit); await a.SaveChangesAsync();
        r.ResidentApartments.Add(new(person.Id,unit.Id,HouseholdRole.HOUSEHOLD_HEAD,ResidencyType.AUTHORIZED_OCCUPANT,date.AddDays(-1),now)); await r.SaveChangesAsync();
        var onboarding = new ResidentOnboarding(r,new ApartmentResidentRelationshipSource(a)); Assert.NotNull(await onboarding.RevalidateRegistrationCandidateAsync(person.Id,date,default));
        a.ApartmentOwnerships.Add(new(unit.Id,owner.Id,date,now,null)); await a.SaveChangesAsync();
        Assert.Null(await onboarding.RevalidateRegistrationCandidateAsync(person.Id,date,default)); Assert.Equal(2,await r.Residents.CountAsync());
        Assert.Equal(ResidentStatus.ACTIVE,(await r.Residents.SingleAsync(x=>x.Id==person.Id)).Status);
        await using var accounts=Accounts(); var existing=new UserAccount("existing","test-only","Existing",now,status:AccountStatus.ACTIVE); accounts.UserAccounts.Add(existing); await accounts.SaveChangesAsync();
        person.LinkUserAccount(existing.Id,null,now); await r.SaveChangesAsync();
        Assert.Null(await new ResidentAccountEligibility(r,new ApartmentResidentRelationshipSource(a)).EligibleApartmentAsync(person.Id,date,default));
        Assert.Equal(AccountStatus.ACTIVE,(await accounts.UserAccounts.SingleAsync()).Status);
        Assert.Equal(existing.Id,(await r.Residents.SingleAsync(x=>x.Id==person.Id)).UserId);
    }
}
