using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;
using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Application;
using PropFlow.Modules.Residents.Domain.ResidentApartments;

namespace PropFlow.IntegrationTests;

public sealed class ResidentOnboardingAtomicTests(PropFlowApiFactory factory) : IClassFixture<PropFlowApiFactory>
{
    [Fact]
    public async Task Coordinator_uses_the_same_scoped_connection_for_residents_and_apartments()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var apartments = scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>();

        Assert.Same(residents.Database.GetDbConnection(), apartments.Database.GetDbConnection());
    }

    [Fact]
    public async Task Coordinator_rolls_back_saved_resident_when_callback_throws()
    {
        var residentId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
            var coordinator = scope.ServiceProvider.GetRequiredService<IAtomicTransactionCoordinator>();
            await Assert.ThrowsAsync<AtomicTestException>(() => coordinator.ExecuteAsync<object?>(async ct =>
            {
                db.Residents.Add(new Resident($"AT{Guid.NewGuid():N}"[..18], "Atomic rollback", DateTimeOffset.UtcNow));
                await db.SaveChangesAsync(ct);
                residentId = db.Residents.Local.Single().Id;
                throw new AtomicTestException();
            }, CancellationToken.None));
        }

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        Assert.False(await verify.Residents.AnyAsync(x => x.Id == residentId));
    }

    [Fact]
    public async Task Coordinator_rolls_back_both_schemas_after_resident_and_ownership_save()
    {
        Guid apartmentId;
        await using (var setup = factory.Services.CreateAsyncScope())
        {
            var apartments = setup.ServiceProvider.GetRequiredService<ApartmentsDbContext>();
            var apartment = new ApartmentUnit($"AT-{Guid.NewGuid():N}"[..18], 1, DateTimeOffset.UtcNow, await ApartmentTypeTestData.CreateAsync(apartments));
            apartments.ApartmentUnits.Add(apartment); await apartments.SaveChangesAsync(); apartmentId = apartment.Id;
        }
        Guid residentId = Guid.Empty;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
            var coordinator = scope.ServiceProvider.GetRequiredService<IAtomicTransactionCoordinator>();
            var ownership = scope.ServiceProvider.GetRequiredService<IApartmentOwnershipCommand>();
            await Assert.ThrowsAsync<AtomicTestException>(() => coordinator.ExecuteAsync<object?>(async ct =>
            {
                var resident = new Resident($"AT{Guid.NewGuid():N}"[..18], "Atomic cross schema", DateTimeOffset.UtcNow);
                residents.Residents.Add(resident); await residents.SaveChangesAsync(ct); residentId = resident.Id;
                await ownership.AddOwnerAsync(apartmentId, resident.Id, DateOnly.FromDateTime(DateTime.UtcNow), null, ct);
                throw new AtomicTestException();
            }, CancellationToken.None));
        }
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyResidents = verifyScope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var verifyApartments = verifyScope.ServiceProvider.GetRequiredService<ApartmentsDbContext>();
        Assert.False(await verifyResidents.Residents.AnyAsync(x => x.Id == residentId));
        Assert.False(await verifyApartments.ApartmentOwnerships.AnyAsync(x => x.ApartmentUnitId == apartmentId && x.OwnerResidentId == residentId));
    }

    [Fact]
    public async Task Owner_only_onboarding_commits_resident_and_owner_without_residency()
    {
        Guid apartmentId;
        await using (var setup = factory.Services.CreateAsyncScope())
        {
            var apartments = setup.ServiceProvider.GetRequiredService<ApartmentsDbContext>();
            var apartment = new ApartmentUnit($"ON-{Guid.NewGuid():N}"[..18], 1, DateTimeOffset.UtcNow, await ApartmentTypeTestData.CreateAsync(apartments));
            apartments.ApartmentUnits.Add(apartment); await apartments.SaveChangesAsync(); apartmentId = apartment.Id;
        }
        ResidentOnboardingResult result;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var onboarding = scope.ServiceProvider.GetRequiredService<ResidentOnboardingService>();
            result = await onboarding.OnboardAsync(new("Owner only", new DateOnly(1990, 1, 1), null, "VN", "CCCD", Guid.NewGuid().ToString("N"), null, null, null, null, null, OnboardingRelationshipKind.OWNER_ONLY, new(apartmentId, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.OWNER_OCCUPIED, null, null, DateOnly.FromDateTime(DateTime.UtcNow), null, null)), null, CancellationToken.None);
        }
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var residents = verifyScope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var apartmentsVerify = verifyScope.ServiceProvider.GetRequiredService<ApartmentsDbContext>();
        Assert.True(await residents.Residents.AnyAsync(x => x.Id == result.Resident.Id));
        Assert.True(await apartmentsVerify.ApartmentOwnerships.AnyAsync(x => x.ApartmentUnitId == apartmentId && x.OwnerResidentId == result.Resident.Id && x.EndDate == null));
        Assert.False(await residents.ResidentApartments.AnyAsync(x => x.ResidentId == result.Resident.Id));
    }

    [Fact]
    public async Task Coordinator_rolls_back_saved_resident_when_typed_business_exception_propagates()
    {
        Guid residentId = Guid.Empty;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
            var coordinator = scope.ServiceProvider.GetRequiredService<IAtomicTransactionCoordinator>();
            var ownership = scope.ServiceProvider.GetRequiredService<IApartmentOwnershipCommand>();
            await Assert.ThrowsAsync<ApartmentOwnershipException>(() => coordinator.ExecuteAsync<object?>(async ct =>
            {
                var resident = new Resident($"BE{Guid.NewGuid():N}"[..18], "Business exception", DateTimeOffset.UtcNow);
                residents.Residents.Add(resident); await residents.SaveChangesAsync(ct); residentId = resident.Id;
                await ownership.AddOwnerAsync(Guid.NewGuid(), resident.Id, DateOnly.FromDateTime(DateTime.UtcNow), null, ct);
                return null;
            }, CancellationToken.None));
        }
        await using var verifyScope = factory.Services.CreateAsyncScope();
        Assert.False(await verifyScope.ServiceProvider.GetRequiredService<ResidentsDbContext>().Residents.AnyAsync(x => x.Id == residentId));
    }

    [Fact]
    public async Task Owner_only_onboarding_adds_a_co_owner_without_ending_the_existing_owner()
    {
        var apartmentId = await CreateApartmentAsync();
        var ownerA = await AddExistingOwnerAsync(apartmentId);
        var result = await OnboardAsync(apartmentId, OnboardingRelationshipKind.OWNER_ONLY);

        await using var scope = factory.Services.CreateAsyncScope();
        var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var apartments = scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>();
        var owners = await apartments.ApartmentOwnerships.Where(x => x.ApartmentUnitId == apartmentId && x.EndDate == null).Select(x => x.OwnerResidentId).ToListAsync();
        Assert.Contains(ownerA, owners); Assert.Contains(result.Resident.Id, owners); Assert.Equal(2, owners.Count);
        Assert.False(await residents.ResidentApartments.AnyAsync(x => x.ResidentId == result.Resident.Id));
    }

    [Fact]
    public async Task Resident_only_onboarding_preserves_the_existing_residency_only_behavior()
    {
        var apartmentId = await CreateApartmentAsync();
        var result = await OnboardAsync(apartmentId, OnboardingRelationshipKind.RESIDENT_ONLY, ResidencyType.TENANT);

        await using var scope = factory.Services.CreateAsyncScope();
        var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var apartments = scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>();
        var residency = await residents.ResidentApartments.SingleAsync(x => x.ResidentId == result.Resident.Id && x.EndDate == null);
        Assert.Equal(ResidencyType.TENANT, residency.ResidencyType);
        Assert.False(await apartments.ApartmentOwnerships.AnyAsync(x => x.OwnerResidentId == result.Resident.Id && x.EndDate == null));
    }

    [Fact]
    public async Task Owner_and_resident_onboarding_creates_both_relationships_and_preserves_household_role()
    {
        var apartmentId = await CreateApartmentAsync();
        var head = await OnboardAsync(apartmentId, OnboardingRelationshipKind.OWNER_AND_RESIDENT, ResidencyType.OWNER_OCCUPIED);
        var result = await OnboardMemberAsOwnerAsync(apartmentId, head.Residency!.Id);

        await using var scope = factory.Services.CreateAsyncScope();
        var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var apartments = scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>();
        var residency = await residents.ResidentApartments.SingleAsync(x => x.ResidentId == result.Resident.Id && x.EndDate == null);
        Assert.Equal(HouseholdRole.HOUSEHOLD_MEMBER, residency.HouseholdRole);
        Assert.Equal(ResidencyType.OWNER_OCCUPIED, residency.ResidencyType);
        Assert.True(await apartments.ApartmentOwnerships.AnyAsync(x => x.ApartmentUnitId == apartmentId && x.OwnerResidentId == result.Resident.Id && x.EndDate == null));
    }

    [Fact]
    public async Task Post_ownership_residency_failure_rolls_back_every_schema()
    {
        var apartmentId = await CreateApartmentAsync();
        await OnboardAsync(apartmentId, OnboardingRelationshipKind.RESIDENT_ONLY, ResidencyType.TENANT);
        var identityNumber = Guid.NewGuid().ToString("N");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var onboarding = scope.ServiceProvider.GetRequiredService<ResidentOnboardingService>();
            var command = new ResidentOnboardingCommand("Post ownership failure", new DateOnly(1990, 1, 1), null, "VN", "CCCD", identityNumber, null, null, null, null, null,
                OnboardingRelationshipKind.OWNER_AND_RESIDENT, new(apartmentId, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT, null, null, DateOnly.FromDateTime(DateTime.UtcNow), null, null));
            await Assert.ThrowsAsync<ResidentResidencyException>(() => onboarding.OnboardAsync(command, null, CancellationToken.None));
        }
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var residents = verifyScope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var apartments = verifyScope.ServiceProvider.GetRequiredService<ApartmentsDbContext>();
        Assert.False(await residents.Residents.AnyAsync(x => x.IdentityNumber == identityNumber));
        Assert.False(await apartments.ApartmentOwnerships.AnyAsync(x => x.ApartmentUnitId == apartmentId && x.EndDate == null));
    }

    [Fact]
    public async Task Owner_and_resident_onboarding_allows_an_existing_co_owner()
    {
        var apartmentId = await CreateApartmentAsync();
        var ownerA = await AddExistingOwnerAsync(apartmentId);
        var result = await OnboardAsync(apartmentId, OnboardingRelationshipKind.OWNER_AND_RESIDENT, ResidencyType.TENANT);

        await using var scope = factory.Services.CreateAsyncScope();
        var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var apartments = scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>();
        var owners = await apartments.ApartmentOwnerships.Where(x => x.ApartmentUnitId == apartmentId && x.EndDate == null).Select(x => x.OwnerResidentId).ToListAsync();
        Assert.Contains(ownerA, owners); Assert.Contains(result.Resident.Id, owners); Assert.Equal(2, owners.Count);
        Assert.Equal(ResidencyType.OWNER_OCCUPIED, await residents.ResidentApartments.Where(x => x.ResidentId == result.Resident.Id && x.EndDate == null).Select(x => x.ResidencyType).SingleAsync());
    }

    [Fact]
    public async Task Coordinator_cleanup_allows_a_fresh_scope_to_onboard_after_a_rollback()
    {
        var apartmentId = await CreateApartmentAsync();
        await using (var failedScope = factory.Services.CreateAsyncScope())
        {
            var residents = failedScope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
            var ownership = failedScope.ServiceProvider.GetRequiredService<IApartmentOwnershipCommand>();
            var coordinator = failedScope.ServiceProvider.GetRequiredService<IAtomicTransactionCoordinator>();
            await Assert.ThrowsAsync<AtomicTestException>(() => coordinator.ExecuteAsync<object?>(async ct =>
            {
                var resident = new Resident($"CL{Guid.NewGuid():N}"[..18], "Cleanup rollback", DateTimeOffset.UtcNow);
                residents.Residents.Add(resident); await residents.SaveChangesAsync(ct);
                await ownership.AddOwnerAsync(apartmentId, resident.Id, DateOnly.FromDateTime(DateTime.UtcNow), null, ct);
                throw new AtomicTestException();
            }, CancellationToken.None));
        }

        var result = await OnboardAsync(apartmentId, OnboardingRelationshipKind.OWNER_ONLY);
        await using var verifyScope = factory.Services.CreateAsyncScope();
        Assert.True(await verifyScope.ServiceProvider.GetRequiredService<ResidentsDbContext>().Residents.AnyAsync(x => x.Id == result.Resident.Id));
        Assert.True(await verifyScope.ServiceProvider.GetRequiredService<ApartmentsDbContext>().ApartmentOwnerships.AnyAsync(x => x.OwnerResidentId == result.Resident.Id && x.EndDate == null));
    }

    [Fact]
    public async Task Concurrent_owner_only_onboarding_allows_two_distinct_owners_and_unique_codes()
    {
        var apartmentId = await CreateApartmentAsync();
        using var gate = new Barrier(2);
        var first = Task.Run(() => OnboardConcurrentlyAsync(apartmentId, gate));
        var second = Task.Run(() => OnboardConcurrentlyAsync(apartmentId, gate));
        var results = await Task.WhenAll(first, second);

        await using var scope = factory.Services.CreateAsyncScope();
        var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var apartments = scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>();
        var ownerIds = await apartments.ApartmentOwnerships.Where(x => x.ApartmentUnitId == apartmentId && x.EndDate == null).Select(x => x.OwnerResidentId).ToListAsync();
        var codes = await residents.Residents.Where(x => results.Select(result => result.Resident.Id).Contains(x.Id)).Select(x => x.ResidentCode).ToListAsync();
        Assert.Equal(2, ownerIds.Count); Assert.All(results, result => Assert.Contains(result.Resident.Id, ownerIds));
        Assert.Equal(2, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.False(await residents.ResidentApartments.AnyAsync(x => ownerIds.Contains(x.ResidentId) && x.EndDate == null));
    }

    [Theory]
    [InlineData(true, "RESIDENT_IDENTITY_ALREADY_EXISTS")]
    [InlineData(false, "RESIDENT_EMAIL_ALREADY_EXISTS")]
    public async Task Concurrent_duplicate_onboarding_commits_exactly_one_resident_and_owner(bool collideOnIdentity, string expectedCode)
    {
        var apartmentId = await CreateApartmentAsync();
        var token = Guid.NewGuid().ToString("N")[..8];
        var sharedIdentity = Random.Shared.NextInt64(100_000_000_000, 1_000_000_000_000).ToString();
        var sharedEmail = $"concurrent-{token}@example.test";
        using var gate = new Barrier(2);
        var first = Task.Run(() => CaptureDuplicateAsync(apartmentId, collideOnIdentity, sharedIdentity, sharedEmail, gate));
        var second = Task.Run(() => CaptureDuplicateAsync(apartmentId, collideOnIdentity, sharedIdentity, sharedEmail, gate));

        var outcomes = await Task.WhenAll(first, second);

        Assert.Single(outcomes, outcome => outcome is null);
        var conflict = Assert.Single(outcomes.OfType<ResidentDuplicateException>());
        Assert.Equal(expectedCode, conflict.Code);
        await using var scope = factory.Services.CreateAsyncScope();
        var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var apartments = scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>();
        var matchingResidents = collideOnIdentity
            ? await residents.Residents.CountAsync(x => x.IdentityType == "CCCD" && x.IdentityNumber == sharedIdentity)
            : await residents.Residents.CountAsync(x => x.Email == sharedEmail);
        Assert.Equal(1, matchingResidents);
        Assert.Equal(1, await apartments.ApartmentOwnerships.CountAsync(x => x.ApartmentUnitId == apartmentId && x.EndDate == null));
    }

    [Fact]
    public async Task Duplicate_owner_and_resident_onboarding_rolls_back_resident_ownership_and_residency()
    {
        var apartmentId = await CreateApartmentAsync();
        var identity = Random.Shared.NextInt64(100_000_000_000, 1_000_000_000_000).ToString();
        var email = $"atomic-duplicate-{Guid.NewGuid():N}@example.test";
        var originalCommand = new ResidentOnboardingCommand("Original person", new DateOnly(1990, 1, 1), null, "VN",
            "CCCD", identity, null, null, "0363602027", email, null, OnboardingRelationshipKind.OWNER_ONLY,
            new(apartmentId, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.OWNER_OCCUPIED, null, null,
                DateOnly.FromDateTime(DateTime.UtcNow), null, null));
        await using (var originalScope = factory.Services.CreateAsyncScope())
            await originalScope.ServiceProvider.GetRequiredService<ResidentOnboardingService>()
                .OnboardAsync(originalCommand, null, CancellationToken.None);

        var duplicateCommand = originalCommand with
        {
            FullName = "Duplicate person",
            RelationshipKind = OnboardingRelationshipKind.OWNER_AND_RESIDENT
        };
        await using (var duplicateScope = factory.Services.CreateAsyncScope())
        {
            var exception = await Assert.ThrowsAsync<ResidentDuplicateException>(() => duplicateScope.ServiceProvider
                .GetRequiredService<ResidentOnboardingService>().OnboardAsync(duplicateCommand, null, CancellationToken.None));
            Assert.Equal("RESIDENT_IDENTITY_ALREADY_EXISTS", exception.Code);
        }

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var residents = verifyScope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var apartments = verifyScope.ServiceProvider.GetRequiredService<ApartmentsDbContext>();
        Assert.Equal(1, await residents.Residents.CountAsync(x => x.IdentityType == "CCCD" && x.IdentityNumber == identity));
        Assert.Equal(1, await apartments.ApartmentOwnerships.CountAsync(x => x.ApartmentUnitId == apartmentId && x.EndDate == null));
        Assert.False(await residents.ResidentApartments.AnyAsync(x => x.ApartmentUnitId == apartmentId));
    }

    private async Task<Guid> CreateApartmentAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var apartments = scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>();
        var apartment = new ApartmentUnit($"OA-{Guid.NewGuid():N}"[..18], 1, DateTimeOffset.UtcNow, await ApartmentTypeTestData.CreateAsync(apartments));
        apartments.ApartmentUnits.Add(apartment); await apartments.SaveChangesAsync();
        return apartment.Id;
    }

    private async Task<Guid> AddExistingOwnerAsync(Guid apartmentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var ownership = scope.ServiceProvider.GetRequiredService<IApartmentOwnershipCommand>();
        var resident = new Resident($"EO{Guid.NewGuid():N}"[..18], "Existing owner", DateTimeOffset.UtcNow);
        residents.Residents.Add(resident); await residents.SaveChangesAsync();
        await ownership.AddOwnerAsync(apartmentId, resident.Id, DateOnly.FromDateTime(DateTime.UtcNow), null, CancellationToken.None);
        return resident.Id;
    }

    private async Task<ResidentOnboardingResult> OnboardAsync(Guid apartmentId, OnboardingRelationshipKind kind, ResidencyType residencyType = ResidencyType.OWNER_OCCUPIED)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var onboarding = scope.ServiceProvider.GetRequiredService<ResidentOnboardingService>();
        return await onboarding.OnboardAsync(NewCommand(apartmentId, kind, residencyType), null, CancellationToken.None);
    }

    private async Task<ResidentOnboardingResult> OnboardConcurrentlyAsync(Guid apartmentId, Barrier gate)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        gate.SignalAndWait(TimeSpan.FromSeconds(10));
        var onboarding = scope.ServiceProvider.GetRequiredService<ResidentOnboardingService>();
        return await onboarding.OnboardAsync(NewCommand(apartmentId, OnboardingRelationshipKind.OWNER_ONLY, ResidencyType.OWNER_OCCUPIED), null, CancellationToken.None);
    }

    private async Task<ResidentOnboardingResult> OnboardMemberAsOwnerAsync(Guid apartmentId, Guid householdHeadResidencyId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var onboarding = scope.ServiceProvider.GetRequiredService<ResidentOnboardingService>();
        var command = new ResidentOnboardingCommand("Owner household member", new DateOnly(1990, 1, 1), null, "VN", "CCCD", Guid.NewGuid().ToString("N"), null, null, null, null, null,
            OnboardingRelationshipKind.OWNER_AND_RESIDENT, new(apartmentId, HouseholdRole.HOUSEHOLD_MEMBER, ResidencyType.TENANT, householdHeadResidencyId, HouseholdRelationship.SPOUSE, DateOnly.FromDateTime(DateTime.UtcNow), null, null));
        return await onboarding.OnboardAsync(command, null, CancellationToken.None);
    }

    private async Task<Exception?> CaptureDuplicateAsync(Guid apartmentId, bool collideOnIdentity, string sharedIdentity, string sharedEmail, Barrier gate)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        gate.SignalAndWait(TimeSpan.FromSeconds(10));
        var onboarding = scope.ServiceProvider.GetRequiredService<ResidentOnboardingService>();
        var identity = collideOnIdentity
            ? sharedIdentity
            : Random.Shared.NextInt64(100_000_000_000, 1_000_000_000_000).ToString();
        var email = collideOnIdentity ? $"distinct-{Guid.NewGuid():N}@example.test" : sharedEmail;
        var command = new ResidentOnboardingCommand("Concurrent duplicate", new DateOnly(1990, 1, 1), null, "VN",
            "CCCD", identity, null, null, "0363602027", email, null, OnboardingRelationshipKind.OWNER_ONLY,
            new(apartmentId, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.OWNER_OCCUPIED, null, null,
                DateOnly.FromDateTime(DateTime.UtcNow), null, null));
        return await Record.ExceptionAsync(() => onboarding.OnboardAsync(command, null, CancellationToken.None));
    }

    private static ResidentOnboardingCommand NewCommand(Guid apartmentId, OnboardingRelationshipKind kind, ResidencyType residencyType) => new(
        "Onboarding " + Guid.NewGuid().ToString("N")[..8], new DateOnly(1990, 1, 1), null, "VN", "CCCD", Guid.NewGuid().ToString("N"), null, null, null, null, null,
        kind, new(apartmentId, HouseholdRole.HOUSEHOLD_HEAD, residencyType, null, null, DateOnly.FromDateTime(DateTime.UtcNow), null, null));


    private sealed class AtomicTestException : Exception;
}
