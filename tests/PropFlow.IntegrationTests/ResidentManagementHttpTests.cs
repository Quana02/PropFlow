using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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

[Trait("Feature", "FE-02-Management")]
public sealed class ResidentManagementHttpTests(AuthDatabaseFixture database) : IClassFixture<AuthDatabaseFixture>
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Theory]
    [InlineData("MANAGER", HttpStatusCode.Created)]
    [InlineData("STAFF", HttpStatusCode.Forbidden)]
    [InlineData("ACCOUNTANT", HttpStatusCode.Forbidden)]
    [InlineData("RESIDENT", HttpStatusCode.Forbidden)]
    [InlineData("ADMIN", HttpStatusCode.Forbidden)]
    public async Task Create_obeys_actor_matrix(string role, HttpStatusCode expected)
    {
        var apartmentId = await SeedApartmentAsync();
        using var response = await (await ClientAsync(role)).PostAsJsonAsync("api/v1/residents", ValidCreate(apartmentId));
        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("MANAGER", HttpStatusCode.OK)]
    [InlineData("STAFF", HttpStatusCode.OK)]
    [InlineData("ACCOUNTANT", HttpStatusCode.OK)]
    [InlineData("RESIDENT", HttpStatusCode.Forbidden)]
    [InlineData("ADMIN", HttpStatusCode.Forbidden)]
    public async Task Detail_obeys_actor_matrix_and_shapes_pii(string role, HttpStatusCode expected)
    {
        var identity = UniqueIdentity();
        var email = $"matrix-{Guid.NewGuid():N}@example.test";
        var resident = await SeedResidentAsync("Matrix person", phone: "+84901234567", email: email, fullPii: true, identityNumber: identity);
        using var response = await (await ClientAsync(role)).GetAsync($"api/v1/residents/{resident.Id}");
        Assert.Equal(expected, response.StatusCode);
        if (expected != HttpStatusCode.OK) return;

        var body = Assert.IsType<ResidentDetailResponse>(await response.Content.ReadFromJsonAsync<ResidentDetailResponse>());
        Assert.Equal(resident.Id, body.Id);
        if (role == "MANAGER")
        {
            Assert.Equal(identity, body.IdentityNumber);
            Assert.Equal("private-note", body.Note);
            Assert.NotNull(body.DateOfBirth);
        }
        else
        {
            Assert.Null(body.IdentityNumber);
            Assert.Null(body.IdentityType);
            Assert.Null(body.Note);
            Assert.Null(body.DateOfBirth);
            if (role == "STAFF") { Assert.Equal("+84901234567", body.PhoneNumber); Assert.Equal(email, body.Email); }
            else { Assert.Null(body.PhoneNumber); Assert.Null(body.Email); }
        }
    }

    [Fact]
    public async Task Manager_update_is_persisted_and_reloaded()
    {
        var resident = await SeedResidentAsync("Before update", fullPii: true);
        var request = new UpdateResidentRequest("After update", new DateOnly(1992, 3, 4), "FEMALE", "Việt Nam", "CCCD", "987654321000", new DateOnly(2020, 1, 1), null, "+84987654321", "after@example.test", "updated note");
        using var response = await (await ClientAsync("MANAGER")).PutAsJsonAsync($"api/v1/residents/{resident.Id}", request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var reloaded = await (await ClientAsync("MANAGER")).GetFromJsonAsync<ResidentDetailResponse>($"api/v1/residents/{resident.Id}");
        Assert.NotNull(reloaded);
        Assert.Equal("After update", reloaded.FullName);
        Assert.Equal("987654321000", reloaded.IdentityNumber);
        Assert.Equal("after@example.test", reloaded.Email);
        Assert.Equal("updated note", reloaded.Note);
    }

    [Theory]
    [MemberData(nameof(InvalidUpdates))]
    public async Task Invalid_update_is_rejected_without_raw_database_failure(UpdateResidentRequest request)
    {
        var resident = await SeedResidentAsync("Validation target", fullPii: true);
        using var response = await (await ClientAsync("MANAGER")).PutAsJsonAsync($"api/v1/residents/{resident.Id}", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("DbUpdateException", payload, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<UpdateResidentRequest> InvalidUpdates => new()
    {
        new("Valid", new DateOnly(1990, 1, 1), null, "VN", "CCCD", "123456789012", null, null, null, "not-an-email", null),
        new("Valid", new DateOnly(1990, 1, 1), null, "VN", "CCCD", "123456789012", null, null, "12x", null, null),
        new("Valid", new DateOnly(2099, 1, 1), null, "VN", "CCCD", "123456789012", null, null, null, null, null),
        new("Valid", new DateOnly(1990, 1, 1), null, "VN", "CCCD", "bad identity!", null, null, null, null, null),
        new(new string('A', 151), new DateOnly(1990, 1, 1), null, "VN", "CCCD", "123456789012", null, null, null, null, null)
    };

    [Theory]
    [InlineData(null, "CCCD", "012345678901", "phoneNumber")]
    [InlineData("0363602027", null, "012345678901", "identityType")]
    [InlineData("0363602027", "CCCD", null, "identityNumber")]
    [InlineData("0363602027", "CCCD", "123456789", "identityNumber")]
    [InlineData("0363602027", "CCCD", "01234567890A", "identityNumber")]
    [InlineData("0363602027", "CCCD", "A012345678901", "identityNumber")]
    [InlineData("0363602027", "CMND", "1234567890", "identityNumber")]
    [InlineData("phone", "CCCD", "012345678901", "phoneNumber")]
    public async Task Create_returns_field_specific_validation_errors(string? phone, string? identityType, string? identityNumber, string expectedField)
    {
        var apartmentId = await SeedApartmentAsync("VALIDATE-");
        var request = ValidCreate(apartmentId) with { PhoneNumber = phone, IdentityType = identityType, IdentityNumber = identityNumber };

        using var response = await (await ClientAsync("MANAGER")).PostAsJsonAsync("api/v1/residents", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains(expectedField, problem.Errors.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("CCCD", "064204010555")]
    [InlineData("CMND", "123456789")]
    [InlineData("CMND", "123456789012")]
    public async Task Full_valid_create_accepts_supported_identity_formats(string identityType, string identityNumber)
    {
        var apartmentId = await SeedApartmentAsync("VALID-");
        var request = ValidCreate(apartmentId) with
        {
            FullName = "Trần Hồng Quân",
            DateOfBirth = new DateOnly(2004, 2, 2),
            Gender = "Nam",
            Nationality = "Việt Nam",
            IdentityType = identityType,
            IdentityNumber = identityNumber,
            IdentityIssuedDate = new DateOnly(2021, 5, 31),
            IdentityExpiryDate = new DateOnly(2031, 5, 31),
            PhoneNumber = "0363602027",
            Email = "quan." + Guid.NewGuid().ToString("N")[..8] + "@gmail.com",
            Note = "ok"
        };

        using var response = await (await ClientAsync("MANAGER")).PostAsJsonAsync("api/v1/residents", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_normalizes_identity_and_rejects_the_same_typed_identity()
    {
        var apartmentId = await SeedApartmentAsync("DUP-ID-");
        var identity = UniqueIdentity();
        var formattedIdentity = identity.Insert(3, " ").Insert(7, "-").Insert(11, ".");
        var first = OwnerOnlyCreate(apartmentId) with
        {
            IdentityType = " cccd ",
            IdentityNumber = formattedIdentity,
            Email = "identity-first-" + Guid.NewGuid().ToString("N")[..8] + "@example.test"
        };
        var exactDuplicate = first;
        var sameIdentityDifferentContacts = OwnerOnlyCreate(apartmentId) with
        {
            IdentityType = "CCCD",
            IdentityNumber = identity,
            PhoneNumber = "0399999999",
            Email = "identity-second-" + Guid.NewGuid().ToString("N")[..8] + "@example.test"
        };
        var client = await ClientAsync("MANAGER");

        using var created = await client.PostAsJsonAsync("api/v1/residents", first);
        using var exactConflict = await client.PostAsJsonAsync("api/v1/residents", exactDuplicate);
        using var contactVariantConflict = await client.PostAsJsonAsync("api/v1/residents", sameIdentityDifferentContacts);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, exactConflict.StatusCode);
        Assert.Equal("RESIDENT_IDENTITY_ALREADY_EXISTS", await ProblemCodeAsync(exactConflict));
        Assert.Equal(HttpStatusCode.Conflict, contactVariantConflict.StatusCode);
        Assert.Equal("RESIDENT_IDENTITY_ALREADY_EXISTS", await ProblemCodeAsync(contactVariantConflict));
    }

    [Fact]
    public async Task Create_rejects_normalized_email_but_allows_same_phone_and_cross_type_identity()
    {
        var apartmentId = await SeedApartmentAsync("DUP-EMAIL-");
        var identity = UniqueIdentity();
        var emailToken = Guid.NewGuid().ToString("N")[..8];
        var client = await ClientAsync("MANAGER");
        var first = OwnerOnlyCreate(apartmentId) with
        {
            IdentityType = "CCCD",
            IdentityNumber = identity,
            PhoneNumber = "0363602027",
            Email = $"resident-{emailToken}@example.test"
        };
        var samePhoneAndNumberAcrossType = OwnerOnlyCreate(apartmentId) with
        {
            IdentityType = "CMND",
            IdentityNumber = identity,
            PhoneNumber = "0363602027",
            Email = $"other-{emailToken}@example.test"
        };
        var duplicateEmail = OwnerOnlyCreate(apartmentId) with
        {
            IdentityNumber = UniqueIdentity(),
            PhoneNumber = "0399999999",
            Email = $"  RESIDENT-{emailToken.ToUpperInvariant()}@EXAMPLE.TEST  "
        };

        using var created = await client.PostAsJsonAsync("api/v1/residents", first);
        using var allowed = await client.PostAsJsonAsync("api/v1/residents", samePhoneAndNumberAcrossType);
        using var rejected = await client.PostAsJsonAsync("api/v1/residents", duplicateEmail);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal("RESIDENT_EMAIL_ALREADY_EXISTS", await ProblemCodeAsync(rejected));
    }

    [Fact]
    public async Task Create_allows_same_name_and_birth_date_when_identity_and_email_are_distinct()
    {
        var apartmentId = await SeedApartmentAsync("SAME-PERSON-SIGNAL-");
        var client = await ClientAsync("MANAGER");
        var first = OwnerOnlyCreate(apartmentId) with
        {
            FullName = "Nguyễn Văn An",
            DateOfBirth = new DateOnly(1990, 1, 1),
            IdentityNumber = UniqueIdentity(),
            Email = $"an-{Guid.NewGuid():N}@example.test"
        };
        var second = first with
        {
            IdentityNumber = UniqueIdentity(),
            Email = $"an-{Guid.NewGuid():N}@example.test"
        };

        using var firstResponse = await client.PostAsJsonAsync("api/v1/residents", first);
        using var secondResponse = await client.PostAsJsonAsync("api/v1/residents", second);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Update_excludes_self_but_rejects_another_residents_identity_and_email()
    {
        var sourceIdentity = UniqueIdentity();
        var first = await SeedResidentAsync("Duplicate source", phone: "0363602027", email: "source@example.test", fullPii: true, identityNumber: sourceIdentity);
        var second = await SeedResidentAsync("Duplicate target", phone: "0399999999", email: "target@example.test", fullPii: false);
        var client = await ClientAsync("MANAGER");
        var formattedSourceIdentity = sourceIdentity.Insert(3, " ").Insert(7, "-").Insert(11, ".");
        var self = new UpdateResidentRequest("Duplicate source", new DateOnly(1990, 1, 1), "FEMALE", "Việt Nam", "CCCD", formattedSourceIdentity, new DateOnly(2020, 1, 1), null, "0363602027", " SOURCE@EXAMPLE.TEST ", "private-note");
        var identityConflict = self with { FullName = "Duplicate target", PhoneNumber = "0399999999", Email = "target@example.test" };
        var emailConflict = identityConflict with { IdentityNumber = UniqueIdentity(), Email = " SOURCE@EXAMPLE.TEST " };

        using var selfResponse = await client.PutAsJsonAsync($"api/v1/residents/{first.Id}", self);
        using var identityResponse = await client.PutAsJsonAsync($"api/v1/residents/{second.Id}", identityConflict);
        using var emailResponse = await client.PutAsJsonAsync($"api/v1/residents/{second.Id}", emailConflict);

        Assert.Equal(HttpStatusCode.NoContent, selfResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, identityResponse.StatusCode);
        Assert.Equal("RESIDENT_IDENTITY_ALREADY_EXISTS", await ProblemCodeAsync(identityResponse));
        Assert.Equal(HttpStatusCode.Conflict, emailResponse.StatusCode);
        Assert.Equal("RESIDENT_EMAIL_ALREADY_EXISTS", await ProblemCodeAsync(emailResponse));
    }

    [Fact]
    public async Task Full_valid_owner_and_resident_household_head_creates_all_expected_rows()
    {
        var apartmentId = await SeedApartmentAsync("FULL-OWNER-RESIDENT-");
        var request = ValidCreate(apartmentId) with
        {
            FullName = "Trần Hồng Quân",
            DateOfBirth = new DateOnly(2004, 2, 2),
            Gender = "Nam",
            Nationality = "Việt Nam",
            IdentityType = "CCCD",
            IdentityNumber = UniqueIdentity(),
            IdentityIssuedDate = new DateOnly(2021, 5, 31),
            IdentityExpiryDate = new DateOnly(2031, 5, 31),
            PhoneNumber = "0363602027",
            Email = "quan." + Guid.NewGuid().ToString("N")[..8] + "@gmail.com",
            Note = "ok",
            RelationshipKind = ResidentApartmentRelationshipKind.OWNER_AND_RESIDENT,
            Residency = new CreateResidencyRequest(apartmentId, HouseholdRole.HOUSEHOLD_HEAD,
                ResidencyType.OWNER_OCCUPIED, null, null, Today, null, null)
        };

        using var response = await (await ClientAsync("MANAGER")).PostAsJsonAsync("api/v1/residents", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = Assert.IsType<ResidentDetailResponse>(await response.Content.ReadFromJsonAsync<ResidentDetailResponse>());
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var apartments = scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>();
        Assert.True(await residents.Residents.AnyAsync(x => x.Id == created.Id));
        Assert.True(await apartments.ApartmentOwnerships.AnyAsync(x => x.ApartmentUnitId == apartmentId && x.OwnerResidentId == created.Id && x.EndDate == null));
        var residency = await residents.ResidentApartments.SingleAsync(x => x.ApartmentUnitId == apartmentId && x.ResidentId == created.Id && x.EndDate == null);
        Assert.Equal(ResidencyType.OWNER_OCCUPIED, residency.ResidencyType);
        Assert.Equal(HouseholdRole.HOUSEHOLD_HEAD, residency.HouseholdRole);
        Assert.Null(residency.HouseholdHeadResidencyId);
        Assert.Null(residency.RelationshipToHead);
    }

    [Fact]
    public async Task Fresh_detail_reflects_FE03_owner_add_without_creating_residency_or_occupancy()
    {
        var firstApartment = await SeedApartmentAsync("SYNC-FIRST-");
        var secondApartment = await SeedApartmentAsync("SYNC-SECOND-");
        var resident = await SeedResidentAsync("Ownership sync resident");
        var existingOwner = await SeedResidentAsync("Ownership sync co-owner");
        await using (var seed = database.Factory.Services.CreateAsyncScope())
        {
            var command = seed.ServiceProvider.GetRequiredService<IApartmentOwnershipCommand>();
            await command.AddOwnerAsync(firstApartment, resident.Id, Today.AddDays(-3), null, CancellationToken.None);
            await command.AddOwnerAsync(secondApartment, existingOwner.Id, Today.AddDays(-3), null, CancellationToken.None);
        }
        var manager = await ClientAsync("MANAGER");
        var before = await manager.GetFromJsonAsync<ResidentDetailResponse>($"api/v1/residents/{resident.Id}");
        Assert.Single(before!.Ownerships, x => x.ApartmentUnitId == firstApartment && x.EndDate == null);

        using var added = await manager.PostAsJsonAsync(
            $"api/v1/apartments/{secondApartment}/owners",
            new ChangeApartmentOwnerRequest(resident.Id, null));

        Assert.Equal(HttpStatusCode.NoContent, added.StatusCode);
        var refreshedResident = await manager.GetFromJsonAsync<ResidentDetailResponse>($"api/v1/residents/{resident.Id}");
        Assert.Contains(refreshedResident!.Ownerships, x => x.ApartmentUnitId == firstApartment && x.EndDate == null);
        Assert.Contains(refreshedResident.Ownerships, x => x.ApartmentUnitId == secondApartment && x.EndDate == null);
        var refreshedApartment = await manager.GetFromJsonAsync<ApartmentDetailResponse>($"api/v1/apartments/{secondApartment}");
        Assert.Contains(refreshedApartment!.Owners, x => x.ResidentId == existingOwner.Id);
        Assert.Contains(refreshedApartment.Owners, x => x.ResidentId == resident.Id);
        Assert.Empty(refreshedApartment.CurrentResidents);

        await using var verify = database.Factory.Services.CreateAsyncScope();
        Assert.False(await verify.ServiceProvider.GetRequiredService<ResidentsDbContext>().ResidentApartments
            .AnyAsync(x => x.ResidentId == resident.Id && x.ApartmentUnitId == secondApartment));
    }

    [Fact]
    public async Task Frontend_shaped_owner_and_resident_payload_is_accepted()
    {
        var apartmentId = await SeedApartmentAsync("FRONTEND-SHAPE-");
        var identityNumber = UniqueIdentity();
        var payload = new
        {
            fullName = "Frontend resident",
            dateOfBirth = "2004-02-02",
            gender = "Nam",
            nationality = "Việt Nam",
            identityType = "CCCD",
            identityNumber,
            identityIssuedDate = "2021-05-31",
            identityExpiryDate = "2031-05-31",
            phoneNumber = "0363602027",
            email = "frontend." + Guid.NewGuid().ToString("N")[..8] + "@example.test",
            note = "ok",
            apartmentUnitId = apartmentId,
            relationshipKind = "OWNER_AND_RESIDENT",
            residency = new
            {
                apartmentUnitId = apartmentId,
                householdRole = "HOUSEHOLD_HEAD",
                residencyType = "OWNER_OCCUPIED",
                householdHeadResidencyId = (Guid?)null,
                relationshipToHead = (string?)null,
                startDate = Today.ToString("yyyy-MM-dd"),
                endDate = (string?)null,
                note = (string?)null
            }
        };

        using var response = await (await ClientAsync("MANAGER")).PostAsJsonAsync("api/v1/residents", payload);

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Expected 201 but received {(int)response.StatusCode}: {responseBody}");
    }

    [Fact]
    public async Task Invalid_frontend_enum_returns_one_specific_json_path_error_without_request_duplicate()
    {
        var apartmentId = await SeedApartmentAsync("INVALID-FRONTEND-ENUM-");
        var payload = new
        {
            fullName = "Invalid enum resident",
            dateOfBirth = "2004-02-02",
            nationality = "Việt Nam",
            identityType = "CCCD",
            identityNumber = "064204010555",
            phoneNumber = "0363602027",
            apartmentUnitId = apartmentId,
            relationshipKind = "NOT_A_RELATIONSHIP",
            residency = (object?)null
        };

        using var response = await (await ClientAsync("MANAGER")).PostAsJsonAsync("api/v1/residents", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = Assert.IsType<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>(await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>());
        Assert.Single(problem.Errors);
        Assert.Contains("$.relationshipKind", problem.Errors.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("request", problem.Errors.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ResidentApartmentRelationshipKind.OWNER_ONLY)]
    [InlineData(ResidentApartmentRelationshipKind.RESIDENT_ONLY)]
    [InlineData(ResidentApartmentRelationshipKind.OWNER_AND_RESIDENT)]
    public async Task Full_valid_create_supports_all_onboarding_relationships(ResidentApartmentRelationshipKind relationshipKind)
    {
        var apartmentId = await SeedApartmentAsync("RELATION-");
        var request = ValidCreate(apartmentId) with
        {
            RelationshipKind = relationshipKind,
            Residency = relationshipKind == ResidentApartmentRelationshipKind.OWNER_ONLY
                ? null
                : new CreateResidencyRequest(apartmentId, HouseholdRole.HOUSEHOLD_HEAD,
                    relationshipKind == ResidentApartmentRelationshipKind.OWNER_AND_RESIDENT ? ResidencyType.OWNER_OCCUPIED : ResidencyType.TENANT,
                    null, null, Today, null, null)
        };

        using var response = await (await ClientAsync("MANAGER")).PostAsJsonAsync("api/v1/residents", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData("STAFF")]
    [InlineData("ACCOUNTANT")]
    public async Task Read_only_operational_roles_cannot_mutate(string role)
    {
        var apartmentId = await SeedApartmentAsync();
        var resident = await SeedResidentAsync("Mutation denied", fullPii: true);
        var residencyId = await SeedResidencyAsync(resident.Id, apartmentId, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT);
        var client = await ClientAsync(role);
        var update = new UpdateResidentRequest("Changed", new DateOnly(1990, 1, 1), null, "VN", null, null, null, null, null, null, null);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"api/v1/residents/{resident.Id}", update)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync($"api/v1/residents/{resident.Id}/status", new SetResidentStatusRequest(ResidentStatus.INACTIVE))).StatusCode);
        var residency = new CreateResidencyRequest(apartmentId, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT, null, null, Today, null, null);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"api/v1/residents/{resident.Id}/residencies", residency)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync($"api/v1/residents/{resident.Id}/residencies/{residencyId}/end", new EndResidencyRequest(Today))).StatusCode);
    }

    [Fact]
    public async Task Status_values_are_explicit_persisted_and_invalid_value_is_safe()
    {
        var resident = await SeedResidentAsync("Status target");
        var manager = await ClientAsync("MANAGER");
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PatchAsJsonAsync($"api/v1/residents/{resident.Id}/status", new SetResidentStatusRequest(ResidentStatus.MOVED_OUT))).StatusCode);
        var moved = await manager.GetFromJsonAsync<ResidentDetailResponse>($"api/v1/residents/{resident.Id}");
        Assert.Equal(ResidentStatus.MOVED_OUT.ToString(), moved!.Status);

        using var invalid = await manager.PatchAsJsonAsync($"api/v1/residents/{resident.Id}/status", new { status = 99 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var unchanged = await manager.GetFromJsonAsync<ResidentDetailResponse>($"api/v1/residents/{resident.Id}");
        Assert.Equal(ResidentStatus.MOVED_OUT.ToString(), unchanged!.Status);
    }

    [Fact]
    public async Task PostgreSql_move_preserves_old_residency_identity_and_ownership()
    {
        var p101 = await SeedApartmentAsync("P101-");
        var p102 = await SeedApartmentAsync("P102-");
        var resident = await SeedResidentAsync("Move target");
        var oldResidency = await SeedResidencyAsync(resident.Id, p101, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.OWNER_OCCUPIED);
        await using (var ownershipScope = database.Factory.Services.CreateAsyncScope())
            await ownershipScope.ServiceProvider.GetRequiredService<IApartmentOwnershipCommand>().AddOwnerAsync(p101, resident.Id, Today.AddDays(-10), null, CancellationToken.None);

        var manager = await ClientAsync("MANAGER");
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PatchAsJsonAsync($"api/v1/residents/{resident.Id}/residencies/{oldResidency}/end", new EndResidencyRequest(Today))).StatusCode);
        var add = new CreateResidencyRequest(p102, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT, null, null, Today, null, "moved");
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsJsonAsync($"api/v1/residents/{resident.Id}/residencies", add)).StatusCode);

        await using var scope = database.Factory.Services.CreateAsyncScope();
        var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var rows = await residents.ResidentApartments.AsNoTracking().Where(x => x.ResidentId == resident.Id).OrderBy(x => x.StartDate).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, x => x.Id == oldResidency && x.ApartmentUnitId == p101 && x.Status == ResidencyStatus.ENDED && x.EndDate == Today);
        Assert.Contains(rows, x => x.ApartmentUnitId == p102 && x.Status == ResidencyStatus.ACTIVE && x.EndDate == null);
        Assert.Equal(resident.Id, (await residents.Residents.AsNoTracking().SingleAsync(x => x.Id == resident.Id)).Id);
        Assert.True(await scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>().ApartmentOwnerships.AsNoTracking().AnyAsync(x => x.ApartmentUnitId == p101 && x.OwnerResidentId == resident.Id && x.EndDate == null));
    }

    [Fact]
    public async Task Household_head_cannot_end_before_members_and_history_is_preserved()
    {
        var apartmentId = await SeedApartmentAsync("HOME-");
        var head = await SeedResidentAsync("Head");
        var member = await SeedResidentAsync("Member");
        var headResidency = await SeedResidencyAsync(head.Id, apartmentId, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT);
        var memberResidency = await SeedResidencyAsync(member.Id, apartmentId, HouseholdRole.HOUSEHOLD_MEMBER, ResidencyType.TENANT, headResidency, HouseholdRelationship.CHILD);
        var manager = await ClientAsync("MANAGER");

        Assert.Equal(HttpStatusCode.Conflict, (await manager.PatchAsJsonAsync($"api/v1/residents/{head.Id}/residencies/{headResidency}/end", new EndResidencyRequest(Today))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PatchAsJsonAsync($"api/v1/residents/{member.Id}/residencies/{memberResidency}/end", new EndResidencyRequest(Today))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PatchAsJsonAsync($"api/v1/residents/{head.Id}/residencies/{headResidency}/end", new EndResidencyRequest(Today))).StatusCode);

        await using var scope = database.Factory.Services.CreateAsyncScope();
        var history = await scope.ServiceProvider.GetRequiredService<ResidentsDbContext>().ResidentApartments.AsNoTracking().Where(x => x.Id == headResidency || x.Id == memberResidency).ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.All(history, x => Assert.Equal(ResidencyStatus.ENDED, x.Status));
    }

    [Fact]
    public async Task Ended_relationship_allows_a_new_current_relationship_without_deleting_history()
    {
        var oldApartment = await SeedApartmentAsync("OLD-");
        var newApartment = await SeedApartmentAsync("NEW-");
        var resident = await SeedResidentAsync("Repeat relation");
        var oldId = await SeedResidencyAsync(resident.Id, oldApartment, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT);
        var manager = await ClientAsync("MANAGER");
        await manager.PatchAsJsonAsync($"api/v1/residents/{resident.Id}/residencies/{oldId}/end", new EndResidencyRequest(Today));
        var response = await manager.PostAsJsonAsync($"api/v1/residents/{resident.Id}/residencies", new CreateResidencyRequest(newApartment, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT, null, null, Today, null, null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = database.Factory.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<ResidentsDbContext>().ResidentApartments.AsNoTracking().Where(x => x.ResidentId == resident.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Single(rows, x => x.Status == ResidencyStatus.ACTIVE);
        Assert.Single(rows, x => x.Status == ResidencyStatus.ENDED);
    }

    [Fact]
    public async Task Add_residency_keeps_existing_active_apartments_and_rejects_only_same_apartment()
    {
        var p101 = await SeedApartmentAsync("MULTI-101-");
        var p202 = await SeedApartmentAsync("MULTI-202-");
        var p303 = await SeedApartmentAsync("MULTI-303-");
        var resident = await SeedResidentAsync("Multiple residency");
        await SeedResidencyAsync(resident.Id, p101, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT);
        var manager = await ClientAsync("MANAGER");

        var add = new CreateResidencyRequest(p202, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT, null, null, Today, null, null);
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsJsonAsync($"api/v1/residents/{resident.Id}/residencies", add)).StatusCode);
        var addThird = new CreateResidencyRequest(p303, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.AUTHORIZED_OCCUPANT, null, null, Today, null, null);
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsJsonAsync($"api/v1/residents/{resident.Id}/residencies", addThird)).StatusCode);
        using var duplicate = await manager.PostAsJsonAsync($"api/v1/residents/{resident.Id}/residencies", add);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains("RESIDENT_ALREADY_ACTIVE_IN_APARTMENT", await duplicate.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        await using var scope = database.Factory.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<ResidentsDbContext>().ResidentApartments.AsNoTracking()
            .Where(x => x.ResidentId == resident.Id && x.Status == ResidencyStatus.ACTIVE && x.EndDate == null).ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, x => x.ApartmentUnitId == p101);
        Assert.Contains(rows, x => x.ApartmentUnitId == p202);
        Assert.Contains(rows, x => x.ApartmentUnitId == p303);
    }

    [Fact]
    public async Task End_one_of_many_residencies_keeps_the_other_active()
    {
        var p101 = await SeedApartmentAsync("END-ONE-101-");
        var p202 = await SeedApartmentAsync("END-ONE-202-");
        var resident = await SeedResidentAsync("End one residency");
        var first = await SeedResidencyAsync(resident.Id, p101, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT);
        var second = await SeedResidencyAsync(resident.Id, p202, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT);

        using var response = await (await ClientAsync("MANAGER")).PatchAsJsonAsync(
            $"api/v1/residents/{resident.Id}/residencies/{first}/end", new EndResidencyRequest(Today));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var verify = database.Factory.Services.CreateAsyncScope();
        var rows = await verify.ServiceProvider.GetRequiredService<ResidentsDbContext>().ResidentApartments.AsNoTracking()
            .Where(x => x.ResidentId == resident.Id).ToListAsync();
        Assert.Contains(rows, x => x.Id == first && x.Status == ResidencyStatus.ENDED);
        Assert.Contains(rows, x => x.Id == second && x.Status == ResidencyStatus.ACTIVE && x.EndDate == null);
    }

    [Fact]
    public async Task Add_residency_can_reenter_ended_apartment_without_overwriting_history()
    {
        var p101 = await SeedApartmentAsync("REENTER-101-");
        var p202 = await SeedApartmentAsync("REENTER-202-");
        var resident = await SeedResidentAsync("Reenter apartment");
        var historical = await SeedResidencyAsync(resident.Id, p101, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT);
        var current = await SeedResidencyAsync(resident.Id, p202, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT);
        await using (var endScope = database.Factory.Services.CreateAsyncScope())
        {
            var db = endScope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
            var old = await db.ResidentApartments.SingleAsync(x => x.Id == historical);
            old.EndResidency(Today.AddDays(-1), null, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        var candidates = await (await ClientAsync("MANAGER")).GetFromJsonAsync<IReadOnlyList<ActiveApartmentOption>>($"api/v1/residents/{resident.Id}/residency-candidates");
        Assert.Contains(candidates!, x => x.Id == p101);
        Assert.DoesNotContain(candidates!, x => x.Id == p202);
        var add = new CreateResidencyRequest(p101, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT, null, null, Today, null, null);
        Assert.Equal(HttpStatusCode.OK, (await (await ClientAsync("MANAGER")).PostAsJsonAsync($"api/v1/residents/{resident.Id}/residencies", add)).StatusCode);

        await using var verify = database.Factory.Services.CreateAsyncScope();
        var rows = await verify.ServiceProvider.GetRequiredService<ResidentsDbContext>().ResidentApartments.AsNoTracking()
            .Where(x => x.ResidentId == resident.Id).ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, x => x.Id == historical && x.Status == ResidencyStatus.ENDED);
        Assert.Contains(rows, x => x.Id == current && x.Status == ResidencyStatus.ACTIVE);
        Assert.Contains(rows, x => x.Id != historical && x.ApartmentUnitId == p101 && x.Status == ResidencyStatus.ACTIVE);
    }

    [Fact]
    public async Task Move_residency_is_one_atomic_endpoint_and_preserves_previous_history()
    {
        var p101 = await SeedApartmentAsync("RETURN-101-");
        var p202 = await SeedApartmentAsync("RETURN-202-");
        var resident = await SeedResidentAsync("Atomic move");
        var historical = await SeedResidencyAsync(resident.Id, p101, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT);
        var source = await SeedResidencyAsync(resident.Id, p202, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT);
        await using (var endScope = database.Factory.Services.CreateAsyncScope())
        {
            var db = endScope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
            var old = await db.ResidentApartments.SingleAsync(x => x.Id == historical);
            old.EndResidency(Today.AddDays(-1), null, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        var request = new { sourceResidencyId = source, target = new CreateResidencyRequest(p101, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT, null, null, Today, null, null) };
        var response = await (await ClientAsync("MANAGER")).PostAsJsonAsync($"api/v1/residents/{resident.Id}/residencies/move", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verify = database.Factory.Services.CreateAsyncScope();
        var rows = await verify.ServiceProvider.GetRequiredService<ResidentsDbContext>().ResidentApartments.AsNoTracking().Where(x => x.ResidentId == resident.Id).ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, x => x.Id == historical && x.Status == ResidencyStatus.ENDED);
        Assert.Contains(rows, x => x.Id == source && x.Status == ResidencyStatus.ENDED);
        Assert.Contains(rows, x => x.Id != historical && x.Id != source && x.ApartmentUnitId == p101 && x.Status == ResidencyStatus.ACTIVE);
    }

    [Fact]
    public async Task Move_residency_rolls_back_source_when_target_insert_fails()
    {
        var p101 = await SeedApartmentAsync("ROLLBACK-101-");
        var p202 = await SeedApartmentAsync("ROLLBACK-202-");
        var resident = await SeedResidentAsync("Rollback move");
        Guid historical;
        await using (var seed = database.Factory.Services.CreateAsyncScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<ResidentsDbContext>();
            var old = new ResidentApartment(resident.Id, p101, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT, Today, DateTimeOffset.UtcNow);
            db.ResidentApartments.Add(old);
            await db.SaveChangesAsync();
            old.EndResidency(Today, null, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
            historical = old.Id;
        }
        var source = await SeedResidencyAsync(resident.Id, p202, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT);

        var request = new { sourceResidencyId = source, target = new CreateResidencyRequest(p101, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT, null, null, Today, null, null) };
        using var response = await (await ClientAsync("MANAGER")).PostAsJsonAsync($"api/v1/residents/{resident.Id}/residencies/move", request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("residency_move_failed", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        await using var verify = database.Factory.Services.CreateAsyncScope();
        var rows = await verify.ServiceProvider.GetRequiredService<ResidentsDbContext>().ResidentApartments.AsNoTracking()
            .Where(x => x.ResidentId == resident.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, x => x.Id == historical && x.Status == ResidencyStatus.ENDED);
        Assert.Contains(rows, x => x.Id == source && x.Status == ResidencyStatus.ACTIVE && x.EndDate == null);
    }

    [Fact]
    public async Task Search_filters_apartment_and_pagination_are_server_side()
    {
        var apartmentId = await SeedApartmentAsync("FILTER-");
        var matched = await SeedResidentAsync("Needle Person", phone: "+84111111111", email: "unique-search@example.test");
        await SeedResidencyAsync(matched.Id, apartmentId, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT);
        for (var index = 0; index < 12; index++) await SeedResidentAsync($"Page Person {index:00}");
        var client = await ClientAsync("ACCOUNTANT");

        var byCode = await client.GetFromJsonAsync<PagedResidentsResponse>($"api/v1/residents?search={matched.ResidentCode}");
        Assert.Contains(byCode!.Items, x => x.Id == matched.Id);
        var byName = await client.GetFromJsonAsync<PagedResidentsResponse>("api/v1/residents?search=Needle");
        Assert.Contains(byName!.Items, x => x.Id == matched.Id);
        var byEmail = await client.GetFromJsonAsync<PagedResidentsResponse>("api/v1/residents?search=unique-search%40example.test");
        Assert.Contains(byEmail!.Items, x => x.Id == matched.Id);
        var byPhone = await client.GetFromJsonAsync<PagedResidentsResponse>("api/v1/residents?search=%2B84111111111");
        var accountantRow = Assert.Single(byPhone!.Items, x => x.Id == matched.Id);
        Assert.Null(accountantRow.PhoneNumber);
        Assert.Null(accountantRow.Email);
        var byStatus = await client.GetFromJsonAsync<PagedResidentsResponse>("api/v1/residents?status=ACTIVE&pageSize=5&pageIndex=2");
        Assert.Equal(2, byStatus!.PageIndex); Assert.Equal(5, byStatus.PageSize); Assert.True(byStatus.TotalCount > 5);
        var byApartment = await client.GetFromJsonAsync<PagedResidentsResponse>($"api/v1/residents?apartmentUnitId={apartmentId}");
        Assert.Single(byApartment!.Items, x => x.Id == matched.Id);
    }

    [Fact]
    public async Task Relationship_residency_and_household_filters_are_server_side_and_composable()
    {
        var apartmentId = await SeedApartmentAsync("REL-FILTER-");
        var ownerOnly = await SeedResidentAsync("Owner only filter");
        var tenantHead = await SeedResidentAsync("Tenant head filter");
        var authorizedMember = await SeedResidentAsync("Authorized member filter");
        await using (var scope = database.Factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IApartmentOwnershipCommand>().AddOwnerAsync(apartmentId, ownerOnly.Id, Today.AddDays(-3), null, CancellationToken.None);
        var headResidencyId = await SeedResidencyAsync(tenantHead.Id, apartmentId, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT);
        await SeedResidencyAsync(authorizedMember.Id, apartmentId, HouseholdRole.HOUSEHOLD_MEMBER, ResidencyType.AUTHORIZED_OCCUPANT, headResidencyId, HouseholdRelationship.CHILD);
        var client = await ClientAsync("MANAGER");

        var owners = await client.GetFromJsonAsync<PagedResidentsResponse>("api/v1/residents?relationshipKind=OWNER_ONLY");
        Assert.Contains(owners!.Items, x => x.Id == ownerOnly.Id);
        Assert.DoesNotContain(owners.Items, x => x.Id == tenantHead.Id || x.Id == authorizedMember.Id);

        var tenants = await client.GetFromJsonAsync<PagedResidentsResponse>("api/v1/residents?relationshipKind=RESIDENT_ONLY&residencyType=TENANT&householdRole=HOUSEHOLD_HEAD");
        Assert.Contains(tenants!.Items, x => x.Id == tenantHead.Id);
        Assert.DoesNotContain(tenants.Items, x => x.Id == ownerOnly.Id || x.Id == authorizedMember.Id);

        var members = await client.GetFromJsonAsync<PagedResidentsResponse>($"api/v1/residents?apartmentUnitId={apartmentId}&residencyType=AUTHORIZED_OCCUPANT&householdRole=HOUSEHOLD_MEMBER&pageIndex=1&pageSize=1");
        Assert.Equal(1, members!.TotalCount);
        Assert.Equal(authorizedMember.Id, Assert.Single(members.Items).Id);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Me_supports_owner_resident_and_combined_modes(bool owner, bool occupant)
    {
        var apartmentId = await SeedApartmentAsync("ME-");
        var (userId, resident) = await SeedLinkedResidentAsync(owner, occupant, apartmentId);
        var own = await (await ClientAsync("RESIDENT", userId)).GetFromJsonAsync<ResidentDetailResponse>("api/v1/residents/me");
        Assert.NotNull(own);
        Assert.Equal(resident.Id, own.Id);
        Assert.Equal(owner ? 1 : 0, own.Ownerships.Count);
        Assert.Equal(occupant ? 1 : 0, own.Residencies.Count);
    }

    [Fact]
    public async Task Resident_self_view_is_isolated_from_management_detail()
    {
        var apartmentId = await SeedApartmentAsync("SELF-");
        var (userId, ownResident) = await SeedLinkedResidentAsync(false, true, apartmentId);
        var other = await SeedResidentAsync("Other private resident", fullPii: true);
        var client = await ClientAsync("RESIDENT", userId);
        Assert.Equal(ownResident.Id, (await client.GetFromJsonAsync<ResidentDetailResponse>("api/v1/residents/me"))!.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"api/v1/residents/{other.Id}")).StatusCode);
    }

    private async Task<HttpClient> ClientAsync(string role, Guid? userId = null)
    {
        var permissions = role switch
        {
            "MANAGER" => new[] { SystemPermissionCodes.ManageOperations },
            "STAFF" => new[] { SystemPermissionCodes.PerformAssignedOperations },
            "ACCOUNTANT" => new[] { SystemPermissionCodes.ManageFinance },
            "ADMIN" => new[] { SystemPermissionCodes.ManageInternalAccounts, SystemPermissionCodes.ViewAdministrationActivity, SystemPermissionCodes.ViewSystemOverview },
            "RESIDENT" => new[] { SystemPermissionCodes.UseResidentServices },
            _ => Array.Empty<string>()
        };
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var authentication = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var user = userId.HasValue
            ? await authentication.UserAccounts.SingleAsync(x => x.Id == userId.Value)
            : new UserAccount(role.ToLowerInvariant() + "_" + Guid.NewGuid().ToString("N")[..10], "test-password-hash", role + " test", DateTimeOffset.UtcNow,
                role.ToLowerInvariant() + "-" + Guid.NewGuid().ToString("N")[..10] + "@example.test", status: AccountStatus.ACTIVE, emailVerified: true);
        if (!userId.HasValue)
        {
            authentication.UserAccounts.Add(user);
            await authentication.SaveChangesAsync();
        }
        var administration = scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
        var roleId = await administration.Roles.Where(x => x.Code == role).Select(x => x.Id).SingleAsync();
        if (!await administration.UserRoleAssignments.AnyAsync(x => x.UserId == user.Id && x.RoleId == roleId))
        {
            administration.UserRoleAssignments.Add(new UserRoleAssignment(user.Id, roleId, DateTimeOffset.UtcNow));
            await administration.SaveChangesAsync();
        }
        var account = new AccountResponse(user.Id, user.Username, user.DisplayName, user.Email, user.PhoneNumber, "ACTIVE", true, role, permissions);
        var client = database.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", database.Factory.Services.GetRequiredService<IAuthSecrets>().Issue(account, DateTimeOffset.UtcNow).AccessToken);
        return client;
    }

    private static CreateResidentRequest ValidCreate(Guid apartmentId) => new(
        "HTTP resident " + Guid.NewGuid().ToString("N")[..8], new DateOnly(1990, 1, 1), null, "VN", "CCCD", "0" + Random.Shared.NextInt64(10000000000, 99999999999), null, null,
        "+84901234567", "resident-" + Guid.NewGuid().ToString("N")[..8] + "@example.test", null, apartmentId,
        ResidentApartmentRelationshipKind.RESIDENT_ONLY,
        new CreateResidencyRequest(apartmentId, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT, null, null, Today, null, null));

    private static CreateResidentRequest OwnerOnlyCreate(Guid apartmentId) => ValidCreate(apartmentId) with
    {
        RelationshipKind = ResidentApartmentRelationshipKind.OWNER_ONLY,
        Residency = null,
        IdentityNumber = UniqueIdentity()
    };

    private static string UniqueIdentity() => Random.Shared.NextInt64(100_000_000_000, 1_000_000_000_000).ToString();

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return payload.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task<Guid> SeedApartmentAsync(string prefix = "RM-")
    {
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>();
        var apartment = new ApartmentUnit(prefix + Guid.NewGuid().ToString("N")[..8], 1, DateTimeOffset.UtcNow, await ApartmentTypeTestData.CreateAsync(db));
        db.ApartmentUnits.Add(apartment); await db.SaveChangesAsync(); return apartment.Id;
    }

    private async Task<Resident> SeedResidentAsync(string name, string? phone = null, string? email = null, bool fullPii = false, string? identityNumber = null)
    {
        await using var scope = database.Factory.Services.CreateAsyncScope();
        identityNumber ??= UniqueIdentity();
        var resident = new Resident("RM-" + Guid.NewGuid().ToString("N")[..12], name,
            fullPii ? new DateOnly(1990, 1, 1) : null, fullPii ? "FEMALE" : null, fullPii ? "Việt Nam" : null,
            fullPii ? "CCCD" : null, fullPii ? identityNumber : null, fullPii ? new DateOnly(2020, 1, 1) : null, null,
            DateTimeOffset.UtcNow, phoneNumber: phone, email: email, note: fullPii ? "private-note" : null);
        var db = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        db.Residents.Add(resident); await db.SaveChangesAsync(); return resident;
    }

    private async Task<Guid> SeedResidencyAsync(Guid residentId, Guid apartmentId, HouseholdRole role, ResidencyType type, Guid? headId = null, HouseholdRelationship? relationship = null)
    {
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var relation = new ResidentApartment(residentId, apartmentId, role, type, Today.AddDays(-5), DateTimeOffset.UtcNow, headId, relationship);
        var db = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        db.ResidentApartments.Add(relation); await db.SaveChangesAsync(); return relation.Id;
    }

    private async Task<(Guid UserId, Resident Resident)> SeedLinkedResidentAsync(bool owner, bool occupant, Guid apartmentId)
    {
        var now = DateTimeOffset.UtcNow;
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var user = new UserAccount("self_" + Guid.NewGuid().ToString("N")[..12], "hash", "Self resident", now,
            "self-" + Guid.NewGuid().ToString("N")[..10] + "@example.test", status: AccountStatus.ACTIVE, emailVerified: true);
        var authentication = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        authentication.UserAccounts.Add(user); await authentication.SaveChangesAsync();
        var resident = new Resident("SELF-" + Guid.NewGuid().ToString("N")[..10], "Self resident", now, userId: user.Id);
        var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        residents.Residents.Add(resident); await residents.SaveChangesAsync();
        if (occupant)
        {
            residents.ResidentApartments.Add(new ResidentApartment(resident.Id, apartmentId, HouseholdRole.HOUSEHOLD_HEAD, owner ? ResidencyType.OWNER_OCCUPIED : ResidencyType.TENANT, Today.AddDays(-2), now));
            await residents.SaveChangesAsync();
        }
        if (owner) await scope.ServiceProvider.GetRequiredService<IApartmentOwnershipCommand>().AddOwnerAsync(apartmentId, resident.Id, Today.AddDays(-2), null, CancellationToken.None);
        return (user.Id, resident);
    }
}
