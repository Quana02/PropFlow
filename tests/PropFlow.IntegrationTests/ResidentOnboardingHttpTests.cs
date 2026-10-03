using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.Administration.Domain.Permissions;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;
using PropFlow.Modules.Apartments.Presentation;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Domain.ResidentApartments;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Presentation;

namespace PropFlow.IntegrationTests;

[Trait("Feature", "FE-02")]
public sealed class ResidentOnboardingHttpTests(AuthDatabaseFixture database) : IClassFixture<AuthDatabaseFixture>
{
    [Fact]
    public async Task Owner_only_creates_owner_without_residency()
    {
        var apartmentId = await CreateApartmentAsync();
        var response = await PostAsync(apartmentId, ResidentApartmentRelationshipKind.OWNER_ONLY, ResidencyType.OWNER_OCCUPIED);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = Assert.IsType<ResidentDetailResponse>(await response.Content.ReadFromJsonAsync<ResidentDetailResponse>());
        var ownership = Assert.Single(body!.Ownerships);
        Assert.Equal(apartmentId, ownership.ApartmentUnitId);
        Assert.StartsWith("HTTP-", ownership.UnitNumber);
        Assert.Empty(body.Residencies);
        await using var scope = database.Factory.Services.CreateAsyncScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>().ApartmentOwnerships.AnyAsync(x => x.ApartmentUnitId == apartmentId && x.OwnerResidentId == body!.Id && x.EndDate == null));
        Assert.False(await scope.ServiceProvider.GetRequiredService<ResidentsDbContext>().ResidentApartments.AnyAsync(x => x.ResidentId == body.Id));
        var apartment = await GetApartmentAsync(apartmentId);
        Assert.Contains(apartment.Owners, x => x.ResidentId == body.Id);
        Assert.DoesNotContain(apartment.Occupants, x => x.ResidentId == body.Id);
    }

    [Fact]
    public async Task Resident_only_creates_residency_without_ownership()
    {
        var apartmentId = await CreateApartmentAsync();
        var response = await PostAsync(apartmentId, ResidentApartmentRelationshipKind.RESIDENT_ONLY, ResidencyType.TENANT);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = Assert.IsType<ResidentDetailResponse>(await response.Content.ReadFromJsonAsync<ResidentDetailResponse>());
        Assert.Empty(body!.Ownerships);
        var residency = Assert.Single(body.Residencies);
        Assert.Equal(apartmentId, residency.ApartmentUnitId);
        Assert.Equal(ResidencyType.TENANT.ToString(), residency.ResidencyType);
        Assert.StartsWith("HTTP-", residency.UnitNumber);
        await using var scope = database.Factory.Services.CreateAsyncScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<ResidentsDbContext>().ResidentApartments.AnyAsync(x => x.ResidentId == body!.Id && x.EndDate == null));
        Assert.False(await scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>().ApartmentOwnerships.AnyAsync(x => x.OwnerResidentId == body.Id && x.EndDate == null));
        var apartment = await GetApartmentAsync(apartmentId);
        Assert.DoesNotContain(apartment.Owners, x => x.ResidentId == body.Id);
        Assert.Contains(apartment.Occupants, x => x.ResidentId == body.Id);
    }

    [Fact]
    public async Task Owner_and_resident_creates_owner_occupied_residency()
    {
        var apartmentId = await CreateApartmentAsync();
        var response = await PostAsync(apartmentId, ResidentApartmentRelationshipKind.OWNER_AND_RESIDENT, ResidencyType.TENANT);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = Assert.IsType<ResidentDetailResponse>(await response.Content.ReadFromJsonAsync<ResidentDetailResponse>());
        Assert.Single(body!.Ownerships);
        var residency = Assert.Single(body.Residencies);
        Assert.Equal(ResidencyType.OWNER_OCCUPIED.ToString(), residency.ResidencyType);
        Assert.Equal(HouseholdRole.HOUSEHOLD_HEAD.ToString(), residency.HouseholdRole);
        await using var scope = database.Factory.Services.CreateAsyncScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>().ApartmentOwnerships.AnyAsync(x => x.ApartmentUnitId == apartmentId && x.OwnerResidentId == body!.Id && x.EndDate == null));
        Assert.Equal(ResidencyType.OWNER_OCCUPIED, await scope.ServiceProvider.GetRequiredService<ResidentsDbContext>().ResidentApartments.Where(x => x.ResidentId == body.Id && x.EndDate == null).Select(x => x.ResidencyType).SingleAsync());
        var apartment = await GetApartmentAsync(apartmentId);
        Assert.Contains(apartment.Owners, x => x.ResidentId == body.Id);
        Assert.Contains(apartment.Occupants, x => x.ResidentId == body.Id);
    }

    [Fact]
    public async Task Owner_only_allows_an_existing_co_owner()
    {
        var apartmentId = await CreateApartmentAsync();
        var ownerA = await AddExistingOwnerAsync(apartmentId);
        var response = await PostAsync(apartmentId, ResidentApartmentRelationshipKind.OWNER_ONLY, ResidencyType.OWNER_OCCUPIED);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = Assert.IsType<ResidentDetailResponse>(await response.Content.ReadFromJsonAsync<ResidentDetailResponse>());
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var owners = await scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>().ApartmentOwnerships.Where(x => x.ApartmentUnitId == apartmentId && x.EndDate == null).Select(x => x.OwnerResidentId).ToListAsync();
        Assert.Contains(ownerA, owners); Assert.Contains(body!.Id, owners); Assert.Equal(2, owners.Count);
    }

    [Fact]
    public async Task Typed_business_errors_are_mapped_without_database_details()
    {
        var ownershipResponse = await PostAsync(Guid.NewGuid(), ResidentApartmentRelationshipKind.OWNER_ONLY, ResidencyType.OWNER_OCCUPIED);
        Assert.Equal(HttpStatusCode.NotFound, ownershipResponse.StatusCode);
        await AssertSafeProblemAsync(ownershipResponse);
        var residencyResponse = await PostAsync(Guid.NewGuid(), ResidentApartmentRelationshipKind.RESIDENT_ONLY, ResidencyType.TENANT);
        Assert.Equal(HttpStatusCode.BadRequest, residencyResponse.StatusCode);
        await AssertSafeProblemAsync(residencyResponse);
    }

    [Fact]
    public async Task Contract_rejects_missing_apartment_and_missing_required_residency()
    {
        var missingApartment = await SendAsync(new CreateResidentRequest("Invalid owner", new DateOnly(1990, 1, 1), null, "VN", "CCCD", "012345678901", null, null, "0363602027", null, null, Guid.Empty, ResidentApartmentRelationshipKind.OWNER_ONLY, null));
        Assert.Equal(HttpStatusCode.BadRequest, missingApartment.StatusCode);
        var apartmentId = await CreateApartmentAsync();
        var missingResidency = await SendAsync(new CreateResidentRequest("Invalid resident", new DateOnly(1990, 1, 1), null, "VN", "CCCD", "012345678901", null, null, "0363602027", null, null, apartmentId, ResidentApartmentRelationshipKind.RESIDENT_ONLY, null));
        Assert.Equal(HttpStatusCode.BadRequest, missingResidency.StatusCode);
    }

    [Fact]
    public async Task Contract_rejects_owner_only_with_a_contradictory_residency_payload()
    {
        var apartmentId = await CreateApartmentAsync();
        var request = new CreateResidentRequest("Contradictory owner", new DateOnly(1990, 1, 1), null, "VN", "CCCD", "012345678901", null, null, "0363602027", null, null,
            apartmentId, ResidentApartmentRelationshipKind.OWNER_ONLY,
            new CreateResidencyRequest(apartmentId, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.OWNER_OCCUPIED, null, null, DateOnly.FromDateTime(DateTime.UtcNow), null, null));
        var response = await SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertSafeProblemAsync(response);
    }

    [Theory]
    [InlineData(ResidentApartmentRelationshipKind.OWNER_ONLY, ResidencyType.OWNER_OCCUPIED, false)]
    [InlineData(ResidentApartmentRelationshipKind.RESIDENT_ONLY, ResidencyType.TENANT, false)]
    [InlineData(ResidentApartmentRelationshipKind.RESIDENT_ONLY, ResidencyType.AUTHORIZED_OCCUPANT, false)]
    [InlineData(ResidentApartmentRelationshipKind.RESIDENT_ONLY, ResidencyType.TENANT, true)]
    [InlineData(ResidentApartmentRelationshipKind.RESIDENT_ONLY, ResidencyType.AUTHORIZED_OCCUPANT, true)]
    [InlineData(ResidentApartmentRelationshipKind.OWNER_AND_RESIDENT, ResidencyType.OWNER_OCCUPIED, false)]
    [InlineData(ResidentApartmentRelationshipKind.OWNER_AND_RESIDENT, ResidencyType.OWNER_OCCUPIED, true)]
    public async Task PostgreSql_valid_relationship_matrix_persists_expected_state(
        ResidentApartmentRelationshipKind kind, ResidencyType residencyType, bool member)
    {
        var apartmentId = await CreateApartmentAsync();
        Guid? headId = member ? await AddHouseholdHeadAsync(apartmentId, residencyType) : null;
        var residency = kind == ResidentApartmentRelationshipKind.OWNER_ONLY
            ? null
            : new CreateResidencyRequest(apartmentId, member ? HouseholdRole.HOUSEHOLD_MEMBER : HouseholdRole.HOUSEHOLD_HEAD,
                residencyType, headId, member ? HouseholdRelationship.CHILD : null, DateOnly.FromDateTime(DateTime.UtcNow), null, null);
        var request = new CreateResidentRequest("Matrix " + Guid.NewGuid().ToString("N")[..8], new DateOnly(1990, 1, 1), "Nam", "VN", "CCCD",
            "0" + Random.Shared.NextInt64(10000000000, 99999999999), null, null, "0363602027", null, null, apartmentId, kind, residency);

        using var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = Assert.IsType<ResidentDetailResponse>(await response.Content.ReadFromJsonAsync<ResidentDetailResponse>());
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var apartments = scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>();
        Assert.True(await residents.Residents.AnyAsync(x => x.Id == created.Id));
        Assert.Equal(kind is ResidentApartmentRelationshipKind.OWNER_ONLY or ResidentApartmentRelationshipKind.OWNER_AND_RESIDENT,
            await apartments.ApartmentOwnerships.AnyAsync(x => x.ApartmentUnitId == apartmentId && x.OwnerResidentId == created.Id && x.EndDate == null));
        var persistedResidency = await residents.ResidentApartments.SingleOrDefaultAsync(x => x.ApartmentUnitId == apartmentId && x.ResidentId == created.Id && x.EndDate == null);
        if (kind == ResidentApartmentRelationshipKind.OWNER_ONLY) Assert.Null(persistedResidency);
        else
        {
            Assert.NotNull(persistedResidency);
            Assert.Equal(kind == ResidentApartmentRelationshipKind.OWNER_AND_RESIDENT ? ResidencyType.OWNER_OCCUPIED : residencyType, persistedResidency.ResidencyType);
            Assert.Equal(member ? HouseholdRole.HOUSEHOLD_MEMBER : HouseholdRole.HOUSEHOLD_HEAD, persistedResidency.HouseholdRole);
            Assert.Equal(headId, persistedResidency.HouseholdHeadResidencyId);
            Assert.Equal(member ? HouseholdRelationship.CHILD : null, persistedResidency.RelationshipToHead);
        }
    }

    [Fact]
    public async Task Contract_rejects_invalid_relationship_combinations_with_field_keys()
    {
        var apartmentId = await CreateApartmentAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await AssertValidationFieldAsync(new CreateResidentRequest("Resident owner type", new DateOnly(1990, 1, 1), null, "VN", "CCCD", "012345678901", null, null, "0363602027", null, null,
            apartmentId, ResidentApartmentRelationshipKind.RESIDENT_ONLY,
            new CreateResidencyRequest(apartmentId, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.OWNER_OCCUPIED, null, null, today, null, null)), "residency.residencyType");
        await AssertValidationFieldAsync(new CreateResidentRequest("Member no head", new DateOnly(1990, 1, 1), null, "VN", "CCCD", "012345678902", null, null, "0363602027", null, null,
            apartmentId, ResidentApartmentRelationshipKind.RESIDENT_ONLY,
            new CreateResidencyRequest(apartmentId, HouseholdRole.HOUSEHOLD_MEMBER, ResidencyType.TENANT, null, HouseholdRelationship.CHILD, today, null, null)), "residency.householdHeadResidencyId");
        await AssertValidationFieldAsync(new CreateResidentRequest("Member no relation", new DateOnly(1990, 1, 1), null, "VN", "CCCD", "012345678903", null, null, "0363602027", null, null,
            apartmentId, ResidentApartmentRelationshipKind.RESIDENT_ONLY,
            new CreateResidencyRequest(apartmentId, HouseholdRole.HOUSEHOLD_MEMBER, ResidencyType.TENANT, Guid.NewGuid(), null, today, null, null)), "residency.relationshipToHead");
        await AssertValidationFieldAsync(new CreateResidentRequest("Head stale fields", new DateOnly(1990, 1, 1), null, "VN", "CCCD", "012345678904", null, null, "0363602027", null, null,
            apartmentId, ResidentApartmentRelationshipKind.RESIDENT_ONLY,
            new CreateResidencyRequest(apartmentId, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT, Guid.NewGuid(), HouseholdRelationship.CHILD, today, null, null)),
            "residency.householdHeadResidencyId", "residency.relationshipToHead");
    }

    [Theory]
    [InlineData(ResidencyType.AUTHORIZED_OCCUPANT)]
    [InlineData(ResidencyType.TENANT)]
    public async Task Household_member_allows_a_residency_type_different_from_the_owner_occupied_head(ResidencyType memberType)
    {
        var apartmentId = await CreateApartmentAsync();
        var headId = await AddOwnerOccupiedHouseholdHeadAsync(apartmentId);
        var request = new CreateResidentRequest("Different member type " + Guid.NewGuid().ToString("N")[..6], new DateOnly(1990, 1, 1), null, "VN", "CCCD",
            "0" + Random.Shared.NextInt64(10000000000, 99999999999), null, null, "0363602027", null, null, apartmentId,
            ResidentApartmentRelationshipKind.RESIDENT_ONLY,
            new CreateResidencyRequest(apartmentId, HouseholdRole.HOUSEHOLD_MEMBER, memberType, headId,
                HouseholdRelationship.CHILD, DateOnly.FromDateTime(DateTime.UtcNow), null, null));

        using var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = Assert.IsType<ResidentDetailResponse>(await response.Content.ReadFromJsonAsync<ResidentDetailResponse>());
        var residency = Assert.Single(created.Residencies);
        Assert.Equal(memberType.ToString(), residency.ResidencyType);
        Assert.Equal(headId, residency.HouseholdHeadResidencyId);
    }

    [Fact]
    public async Task Owner_occupied_residency_rejects_a_resident_without_current_ownership()
    {
        var apartmentId = await CreateApartmentAsync();
        var residentId = await AddResidentAsync();
        var request = new CreateResidencyRequest(apartmentId, HouseholdRole.HOUSEHOLD_HEAD,
            ResidencyType.OWNER_OCCUPIED, null, null, DateOnly.FromDateTime(DateTime.UtcNow), null, null);

        using var response = await AddResidencyAsync(residentId, request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("owner_occupied_requires_current_ownership", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Owner_occupied_residency_accepts_the_current_owner_of_the_same_apartment()
    {
        var apartmentId = await CreateApartmentAsync();
        var residentId = await AddResidentAsync();
        await using (var scope = database.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IApartmentOwnershipCommand>()
                .AddOwnerAsync(apartmentId, residentId, DateOnly.FromDateTime(DateTime.UtcNow), null, CancellationToken.None);
        }
        var request = new CreateResidencyRequest(apartmentId, HouseholdRole.HOUSEHOLD_HEAD,
            ResidencyType.OWNER_OCCUPIED, null, null, DateOnly.FromDateTime(DateTime.UtcNow), null, null);

        using var response = await AddResidencyAsync(residentId, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Member_rejects_head_from_another_apartment_and_ended_head()
    {
        var apartmentId = await CreateApartmentAsync();
        var otherApartmentId = await CreateApartmentAsync();
        var otherHead = await AddHouseholdHeadAsync(otherApartmentId, ResidencyType.TENANT);
        var endedHead = await AddHouseholdHeadAsync(apartmentId, ResidencyType.TENANT, ended: true);

        foreach (var headId in new[] { otherHead, endedHead })
        {
            var request = new CreateResidentRequest("Invalid head " + Guid.NewGuid().ToString("N")[..6], new DateOnly(1990, 1, 1), null, "VN", "CCCD",
                "0" + Random.Shared.NextInt64(10000000000, 99999999999), null, null, "0363602027", null, null, apartmentId,
                ResidentApartmentRelationshipKind.RESIDENT_ONLY,
                new CreateResidencyRequest(apartmentId, HouseholdRole.HOUSEHOLD_MEMBER, ResidencyType.TENANT, headId, HouseholdRelationship.CHILD, DateOnly.FromDateTime(DateTime.UtcNow), null, null));
            using var response = await SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var payload = await response.Content.ReadAsStringAsync();
            Assert.Contains("invalid_household_head", payload, StringComparison.Ordinal);
            await AssertSafeProblemAsync(response);
        }
    }

    [Fact]
    public async Task Eligible_heads_returns_only_active_heads_from_the_selected_apartment()
    {
        var apartmentId = await CreateApartmentAsync();
        var otherApartmentId = await CreateApartmentAsync();
        var ended = await AddHouseholdHeadAsync(apartmentId, ResidencyType.TENANT, ended: true);
        var eligible = await AddHouseholdHeadAsync(apartmentId, ResidencyType.TENANT);
        var other = await AddHouseholdHeadAsync(otherApartmentId, ResidencyType.TENANT);
        await using (var scope = database.Factory.Services.CreateAsyncScope())
        {
            var direct = await scope.ServiceProvider.GetRequiredService<PropFlow.Modules.Residents.Application.ResidentResidencyService>()
                .EligibleHeadsAsync(apartmentId, DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None);
            Assert.Contains(direct, x => x.ResidencyId == eligible);
        }
        using var client = database.Factory.CreateClient();
        var account = await CreateManagerAccountAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", database.Factory.Services.GetRequiredService<IAuthSecrets>().Issue(account, DateTimeOffset.UtcNow).AccessToken);

        using var response = await client.GetAsync($"api/v1/residents/apartments/{apartmentId}/eligible-household-heads");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Expected eligible-heads success but received {(int)response.StatusCode}: {body}");
        var heads = System.Text.Json.JsonSerializer.Deserialize<List<PropFlow.Modules.Residents.Application.EligibleHouseholdHead>>(body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.NotNull(heads);
        Assert.Contains(heads, x => x.ResidencyId == eligible);
        Assert.DoesNotContain(heads, x => x.ResidencyId == ended || x.ResidencyId == other);
    }

    [Fact]
    public async Task Frontend_shaped_owner_and_resident_member_payload_accepts_nested_enum_names()
    {
        var apartmentId = await CreateApartmentAsync();
        var headId = await AddHouseholdHeadAsync(apartmentId, ResidencyType.OWNER_OCCUPIED);
        var payload = new
        {
            fullName = "Frontend member",
            dateOfBirth = "2004-02-02",
            nationality = "Việt Nam",
            identityType = "CCCD",
            identityNumber = "064204010555",
            phoneNumber = "0363602027",
            apartmentUnitId = apartmentId,
            relationshipKind = "OWNER_AND_RESIDENT",
            residency = new
            {
                apartmentUnitId = apartmentId,
                householdRole = "HOUSEHOLD_MEMBER",
                residencyType = "OWNER_OCCUPIED",
                householdHeadResidencyId = headId,
                relationshipToHead = "CHILD",
                startDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd")
            }
        };
        using var client = database.Factory.CreateClient();
        var account = await CreateManagerAccountAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", database.Factory.Services.GetRequiredService<IAuthSecrets>().Issue(account, DateTimeOffset.UtcNow).AccessToken);

        using var response = await client.PostAsJsonAsync("api/v1/residents", payload);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = Assert.IsType<ResidentDetailResponse>(await response.Content.ReadFromJsonAsync<ResidentDetailResponse>());
        var residency = Assert.Single(created.Residencies);
        Assert.Equal(HouseholdRole.HOUSEHOLD_MEMBER.ToString(), residency.HouseholdRole);
        Assert.Equal(ResidencyType.OWNER_OCCUPIED.ToString(), residency.ResidencyType);
        Assert.Equal(headId, residency.HouseholdHeadResidencyId);
        Assert.Equal(HouseholdRelationship.CHILD.ToString(), residency.RelationshipToHead);
    }

    private async Task<Guid> CreateApartmentAsync()
    {
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var apartments = scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>();
        var apartment = new ApartmentUnit("HTTP-" + Guid.NewGuid().ToString("N")[..12], 1, DateTimeOffset.UtcNow, await ApartmentTypeTestData.CreateAsync(apartments));
        apartments.ApartmentUnits.Add(apartment); await apartments.SaveChangesAsync();
        return apartment.Id;
    }

    private async Task<Guid> AddExistingOwnerAsync(Guid apartmentId)
    {
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var resident = new Resident("HTTP-" + Guid.NewGuid().ToString("N")[..12], "Existing owner", DateTimeOffset.UtcNow);
        var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        residents.Residents.Add(resident); await residents.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IApartmentOwnershipCommand>().AddOwnerAsync(apartmentId, resident.Id, DateOnly.FromDateTime(DateTime.UtcNow), null, CancellationToken.None);
        return resident.Id;
    }

    private async Task<Guid> AddHouseholdHeadAsync(Guid apartmentId, ResidencyType type, bool ended = false)
    {
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var resident = new Resident("HEAD-" + Guid.NewGuid().ToString("N")[..12], "Household head", DateTimeOffset.UtcNow);
        residents.Residents.Add(resident);
        await residents.SaveChangesAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var relation = new ResidentApartment(resident.Id, apartmentId, HouseholdRole.HOUSEHOLD_HEAD, type, today.AddDays(-5), DateTimeOffset.UtcNow,
            endDate: ended ? today.AddDays(-1) : null);
        residents.ResidentApartments.Add(relation);
        await residents.SaveChangesAsync();
        return relation.Id;
    }

    private async Task<Guid> AddOwnerOccupiedHouseholdHeadAsync(Guid apartmentId)
    {
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var resident = new Resident("HEAD-" + Guid.NewGuid().ToString("N")[..12], "Owner household head", DateTimeOffset.UtcNow);
        residents.Residents.Add(resident);
        await residents.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IApartmentOwnershipCommand>()
            .AddOwnerAsync(apartmentId, resident.Id, DateOnly.FromDateTime(DateTime.UtcNow), null, CancellationToken.None);
        var relation = new ResidentApartment(resident.Id, apartmentId, HouseholdRole.HOUSEHOLD_HEAD,
            ResidencyType.OWNER_OCCUPIED, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5), DateTimeOffset.UtcNow);
        residents.ResidentApartments.Add(relation);
        await residents.SaveChangesAsync();
        return relation.Id;
    }

    private async Task<Guid> AddResidentAsync()
    {
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var resident = new Resident("RES-" + Guid.NewGuid().ToString("N")[..12], "Residency test resident", DateTimeOffset.UtcNow);
        residents.Residents.Add(resident);
        await residents.SaveChangesAsync();
        return resident.Id;
    }

    private async Task<HttpResponseMessage> AddResidencyAsync(Guid residentId, CreateResidencyRequest request)
    {
        using var client = database.Factory.CreateClient();
        var account = await CreateManagerAccountAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            database.Factory.Services.GetRequiredService<IAuthSecrets>().Issue(account, DateTimeOffset.UtcNow).AccessToken);
        return await client.PostAsJsonAsync($"api/v1/residents/{residentId}/residencies", request);
    }

    private async Task<HttpResponseMessage> PostAsync(Guid apartmentId, ResidentApartmentRelationshipKind kind, ResidencyType residencyType)
    {
        var residency = kind == ResidentApartmentRelationshipKind.OWNER_ONLY ? null : new CreateResidencyRequest(apartmentId, HouseholdRole.HOUSEHOLD_HEAD, residencyType, null, null, DateOnly.FromDateTime(DateTime.UtcNow), null, null);
        var request = new CreateResidentRequest("HTTP onboarding " + Guid.NewGuid().ToString("N")[..8], new DateOnly(1990, 1, 1), null, "VN", "CCCD", "0" + Random.Shared.NextInt64(10000000000, 99999999999), null, null,
            "0363602027", null, null, apartmentId, kind, residency);
        return await SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendAsync(CreateResidentRequest request)
    {
        using var client = database.Factory.CreateClient();
        var account = await CreateManagerAccountAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", database.Factory.Services.GetRequiredService<IAuthSecrets>().Issue(account, DateTimeOffset.UtcNow).AccessToken);
        return await client.PostAsJsonAsync("api/v1/residents", request);
    }

    private async Task<ApartmentDetailResponse> GetApartmentAsync(Guid apartmentId)
    {
        using var client = database.Factory.CreateClient();
        var account = await CreateManagerAccountAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", database.Factory.Services.GetRequiredService<IAuthSecrets>().Issue(account, DateTimeOffset.UtcNow).AccessToken);
        return Assert.IsType<ApartmentDetailResponse>(await client.GetFromJsonAsync<ApartmentDetailResponse>($"api/v1/apartments/{apartmentId}"));
    }

    private async Task<AccountResponse> CreateManagerAccountAsync()
    {
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var now = DateTimeOffset.UtcNow;
        var user = new UserAccount("http_" + Guid.NewGuid().ToString("N")[..12], "test-password-hash", "HTTP manager", now,
            email: "http-" + Guid.NewGuid().ToString("N")[..12] + "@example.invalid", status: AccountStatus.ACTIVE, emailVerified: true);
        var auth = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var administration = scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
        auth.UserAccounts.Add(user); await auth.SaveChangesAsync();
        var managerRoleId = await administration.Roles.Where(role => role.Code == "MANAGER").Select(role => role.Id).SingleAsync();
        administration.UserRoleAssignments.Add(new UserRoleAssignment(user.Id, managerRoleId, now));
        await administration.SaveChangesAsync();
        return new AccountResponse(user.Id, user.Username, user.DisplayName, user.Email, null, "ACTIVE", true, "MANAGER", [SystemPermissionCodes.ManageOperations]);
    }

    private static async Task AssertSafeProblemAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("PostgresException", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DbUpdateException", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("constraint", payload, StringComparison.OrdinalIgnoreCase);
    }

    private async Task AssertValidationFieldAsync(CreateResidentRequest request, params string[] expectedFields)
    {
        using var response = await SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>();
        Assert.NotNull(problem);
        foreach (var field in expectedFields) Assert.Contains(field, problem.Errors.Keys, StringComparer.OrdinalIgnoreCase);
    }
}
