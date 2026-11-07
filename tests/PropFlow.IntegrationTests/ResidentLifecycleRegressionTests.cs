using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Application;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.Residents.Domain.ResidentApartments;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Presentation;

namespace PropFlow.IntegrationTests;

// Regression coverage for the FE-01 rule confirmed on 2026-11-03 and FE-02 residency boundaries.
// All writes live in AuthDatabaseFixture's disposable propflow_fe01_test_<guid> database.
[Trait("UseCase", "FE-01.1/FE-01.3/FE-02.5/FE-02.6")]
public sealed class ResidentLifecycleRegressionTests(AuthDatabaseFixture database) : IClassFixture<AuthDatabaseFixture>
{
    private DateOnly Today => DateOnly.FromDateTime(database.Factory.Clock.GetLocalNow().DateTime);
    private static string Email() => $"completion-{Guid.NewGuid():N}@example.invalid";

    private async Task<(RegistrationResidentSeed Head, RegistrationResidentSeed Member)> Household(bool finiteHead = false, bool finiteMember = false)
    {
        var head = await database.SeedRegistrationResidentAsync(Email(), activeResidency: true);
        var member = await database.SeedRegistrationResidentAsync(Email(), apartmentId: head.ApartmentId);
        using var scope = database.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var headRow = await db.ResidentApartments.SingleAsync(x => x.Id == head.ResidencyId);
        headRow.UpdateHousehold(HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT, null, null, null, database.Factory.Clock.GetUtcNow());
        if (finiteHead)
            await db.ResidentApartments.Where(x => x.Id == head.ResidencyId).ExecuteUpdateAsync(s => s.SetProperty(x => x.EndDate, Today.AddDays(30)));
        if (!finiteHead)
            db.ResidentApartments.Add(new ResidentApartment(member.ResidentId, head.ApartmentId,
                HouseholdRole.HOUSEHOLD_MEMBER, ResidencyType.TENANT, Today.AddDays(-2), database.Factory.Clock.GetUtcNow(),
                head.ResidencyId, HouseholdRelationship.CHILD, finiteMember ? Today.AddDays(30) : null));
        await db.SaveChangesAsync();
        return (head, member);
    }

    [Theory]
    [InlineData(true, ResidencyType.TENANT, false)]
    [InlineData(false, ResidencyType.TENANT, true)]
    [InlineData(false, ResidencyType.AUTHORIZED_OCCUPANT, false)]
    public async Task FE01_non_owner_head_must_rent_an_ownerless_apartment(bool hasOwner, ResidencyType type, bool eligible)
    {
        var owner = await database.SeedRegistrationResidentAsync(Email(), currentOwnership: hasOwner);
        var head = await database.SeedRegistrationResidentAsync(Email(), activeResidency: true, apartmentId: owner.ApartmentId);
        using var scope = database.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var residency = await db.ResidentApartments.SingleAsync(x => x.Id == head.ResidencyId);
        residency.UpdateHousehold(HouseholdRole.HOUSEHOLD_HEAD, type, null, null, null, database.Factory.Clock.GetUtcNow());
        await db.SaveChangesAsync();
        var candidate = await scope.ServiceProvider.GetRequiredService<IResidentOnboarding>()
            .FindRegistrationCandidateAsync(head.Email.ToLowerInvariant(), head.PhoneNumber,
                head.IdentityType, head.IdentityNumber, Today, default);
        Assert.Equal(eligible, candidate is not null);
    }

    [Fact]
    public async Task FE01_household_member_alone_must_not_qualify_for_registration()
    {
        var (_, member) = await Household();
        using var scope = database.Factory.Services.CreateScope();
        var candidate = await scope.ServiceProvider.GetRequiredService<IResidentOnboarding>()
            .FindRegistrationCandidateAsync(member.Email.ToLowerInvariant(), member.PhoneNumber,
                member.IdentityType, member.IdentityNumber, Today, default);
        Assert.Null(candidate);
    }

    [Fact]
    public async Task FE02_list_should_include_both_current_residencies()
    {
        var resident = await database.SeedRegistrationResidentAsync(Email(), activeResidency: true);
        var second = await database.SeedRegistrationResidentAsync(Email());
        using (var scope = database.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
            db.ResidentApartments.Add(new ResidentApartment(resident.ResidentId, second.ApartmentId,
                HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT, Today.AddDays(-2), database.Factory.Clock.GetUtcNow()));
            await db.SaveChangesAsync();
        }
        using var client = await ManagerClient();
        var list = await client.GetFromJsonAsync<PagedResidentsResponse>($"api/v1/residents?search={Uri.EscapeDataString(resident.Email)}");
        var item = Assert.Single(list!.Items);
        Assert.Equal(2, item.ApartmentRelationships.Count(x => x.IsCurrentResident));
    }

    [Fact]
    public async Task FE02_head_should_not_end_while_finite_current_member_remains()
    {
        var (head, _) = await Household(finiteMember: true);
        using var scope = database.Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ResidentResidencyService>();
        var error = await Assert.ThrowsAsync<ResidentResidencyException>(() => service.EndAsync(head.ResidentId, head.ResidencyId!.Value, Today, null, default));
        Assert.Equal("household_members_remain", error.Code);
    }

    [Fact]
    public async Task FE02_eligible_finite_head_should_accept_a_current_member()
    {
        var (head, member) = await Household(finiteHead: true);
        using var scope = database.Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ResidentResidencyService>();
        Assert.Contains(await service.EligibleHeadsAsync(head.ApartmentId, Today, default), x => x.ResidencyId == head.ResidencyId);
        var row = await service.CreateAsync(member.ResidentId,
            new(head.ApartmentId, HouseholdRole.HOUSEHOLD_MEMBER, ResidencyType.TENANT, head.ResidencyId,
                HouseholdRelationship.CHILD, Today, Today.AddDays(10), null), null, default);
        Assert.Equal(head.ResidencyId, row.HouseholdHeadResidencyId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FE01_future_or_expired_head_does_not_qualify(bool future)
    {
        var seed = await database.SeedRegistrationResidentAsync(Email(), activeResidency: true);
        using var scope = database.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        if (future)
            await db.ResidentApartments.Where(x => x.Id == seed.ResidencyId).ExecuteUpdateAsync(s => s.SetProperty(x => x.StartDate, Today.AddDays(1)));
        else
            await db.ResidentApartments.Where(x => x.Id == seed.ResidencyId).ExecuteUpdateAsync(s => s.SetProperty(x => x.EndDate, Today.AddDays(-1)));
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IResidentOnboarding>()
            .FindRegistrationCandidateAsync(seed.Email.ToLowerInvariant(), seed.PhoneNumber, seed.IdentityType, seed.IdentityNumber, Today, default));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(31)]
    public async Task FE02_member_cannot_outlive_the_head(int? endOffset)
    {
        var (head, member) = await Household(finiteHead: true);
        using var scope = database.Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ResidentResidencyService>();
        var error = await Assert.ThrowsAsync<ResidentResidencyException>(() => service.CreateAsync(member.ResidentId,
            new(head.ApartmentId, HouseholdRole.HOUSEHOLD_MEMBER, ResidencyType.TENANT, head.ResidencyId,
                HouseholdRelationship.CHILD, Today, endOffset.HasValue ? Today.AddDays(endOffset.Value) : null, null), null, default));
        Assert.Equal("invalid_household_head", error.Code);
    }

    [Fact]
    public async Task FE02_move_cannot_orphan_a_finite_current_member()
    {
        var (head, _) = await Household(finiteMember: true);
        var target = await database.SeedRegistrationResidentAsync(Email());
        using var scope = database.Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ResidentResidencyService>();
        var error = await Assert.ThrowsAsync<ResidentResidencyException>(() => service.MoveAsync(head.ResidentId, head.ResidencyId!.Value,
            new(target.ApartmentId, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT, null, null, Today, null, null), null, default));
        Assert.Equal("household_members_remain", error.Code);
        var db = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        Assert.Equal(ResidencyStatus.ACTIVE, (await db.ResidentApartments.AsNoTracking().SingleAsync(x => x.Id == head.ResidencyId)).Status);
    }

    [Fact]
    public async Task FE02_ending_twice_does_not_rewrite_history()
    {
        var seed = await database.SeedRegistrationResidentAsync(Email(), activeResidency: true);
        using var scope = database.Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ResidentResidencyService>();
        await service.EndAsync(seed.ResidentId, seed.ResidencyId!.Value, Today, null, default);
        var error = await Assert.ThrowsAsync<ResidentResidencyException>(() => service.EndAsync(seed.ResidentId, seed.ResidencyId.Value, Today.AddDays(1), null, default));
        Assert.Equal("residency_not_active", error.Code);
        Assert.Equal(Today, (await scope.ServiceProvider.GetRequiredService<ResidentsDbContext>().ResidentApartments.AsNoTracking().SingleAsync(x => x.Id == seed.ResidencyId)).EndDate);
    }

    [Fact]
    public async Task FE02_concurrent_member_creation_and_head_end_cannot_leave_an_orphan()
    {
        var head = await database.SeedRegistrationResidentAsync(Email(), activeResidency: true);
        var member = await database.SeedRegistrationResidentAsync(Email(), apartmentId: head.ApartmentId);
        async Task<bool> AddMember()
        {
            using var scope = database.Factory.Services.CreateScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<ResidentResidencyService>().CreateAsync(member.ResidentId,
                    new(head.ApartmentId, HouseholdRole.HOUSEHOLD_MEMBER, ResidencyType.TENANT, head.ResidencyId,
                        HouseholdRelationship.CHILD, Today, null, null), null, default);
                return true;
            }
            catch (ResidentResidencyException ex) when (ex.Code == "invalid_household_head") { return false; }
        }
        async Task<bool> EndHead()
        {
            using var scope = database.Factory.Services.CreateScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<ResidentResidencyService>().EndAsync(head.ResidentId, head.ResidencyId!.Value, Today, null, default);
                return true;
            }
            catch (ResidentResidencyException ex) when (ex.Code == "household_members_remain") { return false; }
        }
        var outcomes = await Task.WhenAll(AddMember(), EndHead());
        Assert.NotEqual(outcomes[0], outcomes[1]);
        using var check = database.Factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var headRow = await db.ResidentApartments.AsNoTracking().SingleAsync(x => x.Id == head.ResidencyId);
        var memberExists = await db.ResidentApartments.AnyAsync(x => x.ResidentId == member.ResidentId && x.Status == ResidencyStatus.ACTIVE);
        Assert.Equal(memberExists, headRow.Status == ResidencyStatus.ACTIVE);
    }

    private async Task<HttpClient> ManagerClient()
    {
        using var scope = database.Factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var access = scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
        var user = new UserAccount("audit_" + Guid.NewGuid().ToString("N")[..12], "test-only-hash", "Audit", database.Factory.Clock.GetUtcNow(),
            status: AccountStatus.ACTIVE, emailVerified: true);
        auth.UserAccounts.Add(user); await auth.SaveChangesAsync();
        var role = await access.Roles.Include(x => x.RolePermissions).ThenInclude(x => x.Permission).SingleAsync(x => x.Code == "MANAGER");
        access.UserRoleAssignments.Add(new UserRoleAssignment(user.Id, role.Id, database.Factory.Clock.GetUtcNow()));
        await access.SaveChangesAsync();
        var account = new AccountResponse(user.Id, user.Username, user.DisplayName, null, null, "ACTIVE", true, "MANAGER", role.RolePermissions.Select(x => x.Permission.Code).ToArray());
        var client = database.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scope.ServiceProvider.GetRequiredService<IAuthSecrets>().Issue(account, database.Factory.Clock.GetUtcNow()).AccessToken);
        return client;
    }
}

