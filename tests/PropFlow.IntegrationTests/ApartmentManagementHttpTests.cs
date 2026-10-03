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

namespace PropFlow.IntegrationTests;

[Trait("Feature", "FE-03-Management")]
public sealed class ApartmentManagementHttpTests(AuthDatabaseFixture database) : IClassFixture<AuthDatabaseFixture>
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    [Theory]
    [InlineData("MANAGER", HttpStatusCode.Created)]
    [InlineData("STAFF", HttpStatusCode.Forbidden)]
    [InlineData("ACCOUNTANT", HttpStatusCode.Forbidden)]
    [InlineData("RESIDENT", HttpStatusCode.Forbidden)]
    [InlineData("ADMIN", HttpStatusCode.Forbidden)]
    public async Task Create_obeys_actor_matrix(string role, HttpStatusCode expected)
    {
        using var response = await (await ClientAsync(role)).PostAsJsonAsync("api/v1/apartments", ValidCreate(await SeedApartmentTypeAsync()));
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Manager_create_persists_and_duplicate_is_safe()
    {
        var client = await ClientAsync("MANAGER");
        var request = ValidCreate(await SeedApartmentTypeAsync());
        using var created = await client.PostAsJsonAsync("api/v1/apartments", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<ApartmentDetailResponse>();
        Assert.NotNull(body);

        await using (var scope = database.Factory.Services.CreateAsyncScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>().ApartmentUnits.AsNoTracking().SingleAsync(x => x.Id == body.Id);
            Assert.Equal(request.UnitNumber, row.UnitNumber);
            Assert.Equal(request.HandoverDate, row.HandoverDate);
        }

        using var duplicate = await client.PostAsJsonAsync("api/v1/apartments", request);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.DoesNotContain("DbUpdateException", await duplicate.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Normalized_type_schema_has_no_unmapped_apartments_or_duplicate_names()
    {
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>();
        var typeIds = await db.ApartmentUnitTypes.AsNoTracking().Select(x => x.Id).ToArrayAsync();
        Assert.DoesNotContain(Guid.Empty, typeIds);
        Assert.Equal(0, await db.ApartmentUnits.AsNoTracking().CountAsync(x => !typeIds.Contains(x.ApartmentUnitTypeId)));
        var names = await db.ApartmentUnitTypes.AsNoTracking().Select(x => x.Name).ToArrayAsync();
        Assert.Equal(names.Length, names.Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [MemberData(nameof(InvalidApartments))]
    public async Task Invalid_create_is_controlled(CreateApartmentRequest request)
    {
        using var response = await (await ClientAsync("MANAGER")).PostAsJsonAsync("api/v1/apartments", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("PostgresException", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DbUpdateException", payload, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<CreateApartmentRequest> InvalidApartments => new()
    {
        new("", 1, Guid.Empty, 50, 1, 1, null, null),
        new("BAD-" + Guid.NewGuid().ToString("N")[..6], -1, Guid.Empty, 50, 1, 1, null, null),
        new("BAD-" + Guid.NewGuid().ToString("N")[..6], 1, Guid.Empty, 0, -1, 1, null, null),
        new("BAD-" + Guid.NewGuid().ToString("N")[..6], 1, Guid.Empty, 50, 1, 1, null, null, "VACANT")
    };

    [Theory]
    [InlineData("MANAGER", HttpStatusCode.OK)]
    [InlineData("STAFF", HttpStatusCode.OK)]
    [InlineData("ACCOUNTANT", HttpStatusCode.OK)]
    [InlineData("RESIDENT", HttpStatusCode.Forbidden)]
    [InlineData("ADMIN", HttpStatusCode.Forbidden)]
    public async Task Detail_obeys_actor_matrix(string role, HttpStatusCode expected)
    {
        var apartment = await SeedApartmentAsync();
        using var response = await (await ClientAsync(role)).GetAsync($"api/v1/apartments/{apartment}");
        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("MANAGER", HttpStatusCode.OK)]
    [InlineData("STAFF", HttpStatusCode.OK)]
    [InlineData("ACCOUNTANT", HttpStatusCode.OK)]
    [InlineData("RESIDENT", HttpStatusCode.Forbidden)]
    [InlineData("ADMIN", HttpStatusCode.Forbidden)]
    public async Task Apartment_type_list_obeys_management_read_matrix(string role, HttpStatusCode expected)
    {
        using var response = await (await ClientAsync(role)).GetAsync("api/v1/apartment-unit-types");
        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("MANAGER", HttpStatusCode.Created)]
    [InlineData("STAFF", HttpStatusCode.Forbidden)]
    [InlineData("ACCOUNTANT", HttpStatusCode.Forbidden)]
    [InlineData("RESIDENT", HttpStatusCode.Forbidden)]
    [InlineData("ADMIN", HttpStatusCode.Forbidden)]
    public async Task Apartment_type_create_obeys_actor_matrix(string role, HttpStatusCode expected)
    {
        using var response = await (await ClientAsync(role)).PostAsJsonAsync("api/v1/apartment-unit-types", new CreateApartmentUnitTypeRequest("Matrix "+Guid.NewGuid().ToString("N")));
        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("MANAGER", HttpStatusCode.OK)]
    [InlineData("STAFF", HttpStatusCode.Forbidden)]
    [InlineData("ACCOUNTANT", HttpStatusCode.Forbidden)]
    [InlineData("RESIDENT", HttpStatusCode.Forbidden)]
    [InlineData("ADMIN", HttpStatusCode.Forbidden)]
    public async Task Apartment_type_update_obeys_actor_matrix(string role, HttpStatusCode expected)
    {
        var typeId = await SeedApartmentTypeAsync("TYPE-EDIT-" + Guid.NewGuid().ToString("N")[..8]);
        using var response = await (await ClientAsync(role)).PutAsJsonAsync(
            $"api/v1/apartment-unit-types/{typeId}",
            new UpdateApartmentUnitTypeRequest("Renamed " + Guid.NewGuid().ToString("N")[..8]));
        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("MANAGER", HttpStatusCode.NoContent)]
    [InlineData("STAFF", HttpStatusCode.Forbidden)]
    [InlineData("ACCOUNTANT", HttpStatusCode.Forbidden)]
    [InlineData("RESIDENT", HttpStatusCode.Forbidden)]
    [InlineData("ADMIN", HttpStatusCode.Forbidden)]
    public async Task Apartment_type_delete_obeys_actor_matrix(string role, HttpStatusCode expected)
    {
        var typeId = await SeedApartmentTypeAsync("TYPE-DELETE-" + Guid.NewGuid().ToString("N")[..8]);
        using var response = await (await ClientAsync(role)).DeleteAsync($"api/v1/apartment-unit-types/{typeId}");
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Manager_renames_type_without_changing_apartment_reference()
    {
        var oldName = "Rename source " + Guid.NewGuid().ToString("N")[..8];
        var newName = "Rename target " + Guid.NewGuid().ToString("N")[..8];
        var typeId = await SeedApartmentTypeAsync(oldName);
        var apartmentId = await SeedApartmentAsync("RENAME-", typeId: typeId);
        var client = await ClientAsync("MANAGER");

        using var renamed = await client.PutAsJsonAsync(
            $"api/v1/apartment-unit-types/{typeId}",
            new UpdateApartmentUnitTypeRequest(newName));

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var response = await renamed.Content.ReadFromJsonAsync<ApartmentUnitTypeResponse>();
        Assert.Equal(typeId, response!.Id);
        Assert.Equal(newName, response.Name);
        var apartment = await client.GetFromJsonAsync<ApartmentDetailResponse>($"api/v1/apartments/{apartmentId}");
        Assert.Equal(typeId, apartment!.ApartmentUnitTypeId);
        Assert.Equal(newName, apartment.UnitType);
    }

    [Fact]
    public async Task Manager_cannot_rename_type_to_normalized_duplicate()
    {
        var existingName = "Duplicate target " + Guid.NewGuid().ToString("N")[..8];
        await SeedApartmentTypeAsync(existingName);
        var sourceId = await SeedApartmentTypeAsync("Duplicate source " + Guid.NewGuid().ToString("N")[..8]);
        var client = await ClientAsync("MANAGER");

        using var response = await client.PutAsJsonAsync(
            $"api/v1/apartment-unit-types/{sourceId}",
            new UpdateApartmentUnitTypeRequest("  " + existingName.ToUpperInvariant() + "  "));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("APARTMENT_UNIT_TYPE_DUPLICATE", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Manager_deletes_unused_type_and_it_disappears_from_options()
    {
        var typeId = await SeedApartmentTypeAsync("Unused " + Guid.NewGuid().ToString("N")[..8]);
        var client = await ClientAsync("MANAGER");

        using var deleted = await client.DeleteAsync($"api/v1/apartment-unit-types/{typeId}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var types = await client.GetFromJsonAsync<IReadOnlyList<ApartmentUnitTypeResponse>>("api/v1/apartment-unit-types");
        Assert.DoesNotContain(types!, x => x.Id == typeId);
    }

    [Fact]
    public async Task Manager_cannot_delete_used_type_and_apartment_is_preserved()
    {
        var typeId = await SeedApartmentTypeAsync("Used " + Guid.NewGuid().ToString("N")[..8]);
        var apartmentId = await SeedApartmentAsync("USED-TYPE-", typeId: typeId);
        var client = await ClientAsync("MANAGER");

        using var deleted = await client.DeleteAsync($"api/v1/apartment-unit-types/{typeId}");

        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);
        var payload = await deleted.Content.ReadAsStringAsync();
        Assert.Contains("APARTMENT_TYPE_IN_USE", payload);
        Assert.DoesNotContain("foreign key", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DbUpdateException", payload, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await client.GetFromJsonAsync<ApartmentDetailResponse>($"api/v1/apartments/{apartmentId}"));
    }

    [Fact]
    public async Task Create_apartment_with_unknown_type_is_controlled()
    {
        using var response = await (await ClientAsync("MANAGER")).PostAsJsonAsync("api/v1/apartments", ValidCreate(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload=await response.Content.ReadAsStringAsync();
        Assert.Contains("APARTMENT_UNIT_TYPE_NOT_FOUND",payload);
        Assert.DoesNotContain("foreign key",payload,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Manager_creates_type_duplicate_is_safe_and_selected_type_filters_server_side()
    {
        var name = "Căn hộ tiêu chuẩn " + Guid.NewGuid().ToString("N")[..5];
        var client = await ClientAsync("MANAGER");
        using var createdResponse = await client.PostAsJsonAsync("api/v1/apartment-unit-types", new CreateApartmentUnitTypeRequest(name));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<ApartmentUnitTypeResponse>();
        Assert.NotNull(created);
        using var duplicate = await client.PostAsJsonAsync("api/v1/apartment-unit-types", new CreateApartmentUnitTypeRequest("  " + name.ToUpperInvariant() + "  "));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.DoesNotContain("DbUpdateException", await duplicate.Content.ReadAsStringAsync());
        var listed = await (await ClientAsync("ACCOUNTANT")).GetFromJsonAsync<IReadOnlyList<ApartmentUnitTypeResponse>>("api/v1/apartment-unit-types");
        Assert.Contains(listed!, x => x.Id == created.Id && x.Name == name);

        var unique = "TYPE-" + Guid.NewGuid().ToString("N")[..6] + "-";
        await SeedApartmentAsync(unique, typeId: created.Id);
        var result = await client.GetFromJsonAsync<PagedApartmentsResponse>($"api/v1/apartments?search={unique}&apartmentUnitTypeId={created.Id}&pageSize=100");
        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal(name, result.Items[0].UnitType);
    }

    [Theory]
    [InlineData("MANAGER", HttpStatusCode.NoContent)]
    [InlineData("STAFF", HttpStatusCode.Forbidden)]
    [InlineData("ACCOUNTANT", HttpStatusCode.Forbidden)]
    [InlineData("RESIDENT", HttpStatusCode.Forbidden)]
    [InlineData("ADMIN", HttpStatusCode.Forbidden)]
    public async Task Update_obeys_actor_matrix_and_manager_persists(string role, HttpStatusCode expected)
    {
        var apartment = await SeedApartmentAsync();
        var targetType = await SeedApartmentTypeAsync("DUPLEX-"+Guid.NewGuid().ToString("N")[..5]);
        var request = new UpdateApartmentRequest("UPDATED-" + Guid.NewGuid().ToString("N")[..6], 18, targetType, 123.5m, 3, 2, new DateOnly(2026, 8, 1), "updated");
        using var response = await (await ClientAsync(role)).PutAsJsonAsync($"api/v1/apartments/{apartment}", request);
        Assert.Equal(expected, response.StatusCode);
        if (role != "MANAGER") return;
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>().ApartmentUnits.AsNoTracking().SingleAsync(x => x.Id == apartment);
        Assert.Equal(targetType, row.ApartmentUnitTypeId);
        Assert.Equal(123.5m, row.UsableAreaM2);
        var detail = await (await ClientAsync("STAFF")).GetFromJsonAsync<ApartmentDetailResponse>($"api/v1/apartments/{apartment}");
        Assert.Equal(targetType, detail!.ApartmentUnitTypeId);
        Assert.StartsWith("DUPLEX-", detail.UnitType);
    }

    [Theory]
    [InlineData("MANAGER", HttpStatusCode.NoContent)]
    [InlineData("STAFF", HttpStatusCode.Forbidden)]
    [InlineData("ACCOUNTANT", HttpStatusCode.Forbidden)]
    [InlineData("RESIDENT", HttpStatusCode.Forbidden)]
    [InlineData("ADMIN", HttpStatusCode.Forbidden)]
    public async Task Status_mutation_obeys_actor_matrix(string role, HttpStatusCode expected)
    {
        var apartment = await SeedApartmentAsync();
        using var response = await (await ClientAsync(role)).PatchAsJsonAsync($"api/v1/apartments/{apartment}/status", new SetApartmentStatusRequest("INACTIVE"));
        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("MANAGER", HttpStatusCode.NoContent)]
    [InlineData("STAFF", HttpStatusCode.Forbidden)]
    [InlineData("ACCOUNTANT", HttpStatusCode.Forbidden)]
    [InlineData("RESIDENT", HttpStatusCode.Forbidden)]
    [InlineData("ADMIN", HttpStatusCode.Forbidden)]
    public async Task Ownership_mutation_obeys_actor_matrix(string role, HttpStatusCode expected)
    {
        var apartment = await SeedApartmentAsync();
        var resident = await SeedResidentAsync("Owner matrix");
        using var response = await (await ClientAsync(role)).PostAsJsonAsync($"api/v1/apartments/{apartment}/owners", new ChangeApartmentOwnerRequest(resident.Id, Today));
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Resident_ownership_candidates_exclude_already_owned_and_inactive_apartments()
    {
        var marker = "OC" + Guid.NewGuid().ToString("N")[..5];
        var alreadyOwned = await SeedApartmentAsync(marker + "A-");
        var coOwnerCandidate = await SeedApartmentAsync(marker + "B-");
        var emptyCandidate = await SeedApartmentAsync(marker + "C-");
        var inactive = await SeedApartmentAsync(marker + "D-");
        var resident = await SeedResidentAsync("Candidate resident");
        var existingOwner = await SeedResidentAsync("Existing co-owner");
        await AddOwnerAsync(alreadyOwned, resident.Id, Today.AddDays(-2));
        await AddOwnerAsync(coOwnerCandidate, existingOwner.Id, Today.AddDays(-2));
        var manager = await ClientAsync("MANAGER");
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PatchAsJsonAsync(
            $"api/v1/apartments/{inactive}/status", new SetApartmentStatusRequest("INACTIVE"))).StatusCode);

        var candidates = await manager.GetFromJsonAsync<IReadOnlyList<ApartmentOwnershipCandidateResponse>>(
            $"api/v1/apartments/ownership-candidates?ownerResidentId={resident.Id}&search={marker}");

        Assert.NotNull(candidates);
        Assert.DoesNotContain(candidates, x => x.Id == alreadyOwned);
        Assert.Contains(candidates, x => x.Id == coOwnerCandidate);
        Assert.Contains(candidates, x => x.Id == emptyCandidate);
        Assert.DoesNotContain(candidates, x => x.Id == inactive);
        Assert.All(candidates, x => Assert.Equal("ACTIVE", x.Status));
    }

    [Theory]
    [InlineData("MANAGER", HttpStatusCode.OK)]
    [InlineData("STAFF", HttpStatusCode.Forbidden)]
    [InlineData("ACCOUNTANT", HttpStatusCode.Forbidden)]
    [InlineData("RESIDENT", HttpStatusCode.Forbidden)]
    [InlineData("ADMIN", HttpStatusCode.Forbidden)]
    public async Task Resident_ownership_candidates_obey_mutation_role_matrix(string role, HttpStatusCode expected)
    {
        var resident = await SeedResidentAsync("Candidate role matrix");
        using var response = await (await ClientAsync(role)).GetAsync(
            $"api/v1/apartments/ownership-candidates?ownerResidentId={resident.Id}");
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Current_ownership_and_active_residency_each_block_deactivation()
    {
        var ownerOnly = await SeedApartmentAsync("OWNER-");
        var owner = await SeedResidentAsync("Owner only");
        await AddOwnerAsync(ownerOnly, owner.Id, Today.AddDays(-2));
        using var ownerBlocked = await (await ClientAsync("MANAGER")).PatchAsJsonAsync($"api/v1/apartments/{ownerOnly}/status", new SetApartmentStatusRequest("INACTIVE"));
        Assert.Equal(HttpStatusCode.Conflict, ownerBlocked.StatusCode);
        Assert.Contains("APARTMENT_HAS_CURRENT_OWNERS", await ownerBlocked.Content.ReadAsStringAsync());
        await using (var verifyOwner = database.Factory.Services.CreateAsyncScope())
        {
            var apartments = verifyOwner.ServiceProvider.GetRequiredService<ApartmentsDbContext>();
            Assert.Equal(MasterDataStatus.ACTIVE, (await apartments.ApartmentUnits.AsNoTracking().SingleAsync(x => x.Id == ownerOnly)).Status);
            Assert.True(await apartments.ApartmentOwnerships.AsNoTracking().AnyAsync(x => x.ApartmentUnitId == ownerOnly && x.OwnerResidentId == owner.Id && x.EndDate == null));
        }

        var occupied = await SeedApartmentAsync("OCC-");
        var occupant = await SeedResidentAsync("Active occupant");
        await SeedResidencyAsync(occupant.Id, occupied, Today.AddDays(-2));
        using var blocked = await (await ClientAsync("MANAGER")).PatchAsJsonAsync($"api/v1/apartments/{occupied}/status", new SetApartmentStatusRequest("INACTIVE"));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains("APARTMENT_HAS_ACTIVE_RESIDENTS", await blocked.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Inactive_apartment_rejects_new_owner_without_creating_ownership()
    {
        var apartment = await SeedApartmentAsync("INACTIVE-OWNER-");
        var resident = await SeedResidentAsync("Rejected inactive owner");
        var manager = await ClientAsync("MANAGER");
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PatchAsJsonAsync(
            $"api/v1/apartments/{apartment}/status", new SetApartmentStatusRequest("INACTIVE"))).StatusCode);

        using var response = await manager.PostAsJsonAsync(
            $"api/v1/apartments/{apartment}/owners", new ChangeApartmentOwnerRequest(resident.Id, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("APARTMENT_INACTIVE", await response.Content.ReadAsStringAsync());
        await using var verify = database.Factory.Services.CreateAsyncScope();
        Assert.False(await verify.ServiceProvider.GetRequiredService<ApartmentsDbContext>().ApartmentOwnerships.AsNoTracking()
            .AnyAsync(x => x.ApartmentUnitId == apartment && x.OwnerResidentId == resident.Id && x.EndDate == null));
    }

    [Fact]
    public async Task Every_current_owner_must_end_before_deactivation_and_history_is_preserved()
    {
        var apartment = await SeedApartmentAsync("MULTI-OWNER-");
        var first = await SeedResidentAsync("First current owner");
        var second = await SeedResidentAsync("Second current owner");
        var firstOwnership = await AddOwnerAsync(apartment, first.Id, Today.AddDays(-10));
        var secondOwnership = await AddOwnerAsync(apartment, second.Id, Today.AddDays(-5));
        var manager = await ClientAsync("MANAGER");

        Assert.Equal(HttpStatusCode.NoContent, (await manager.PatchAsJsonAsync(
            $"api/v1/apartments/{apartment}/owners/{firstOwnership}/end", new EndApartmentOwnershipRequest(Today))).StatusCode);
        using var stillBlocked = await manager.PatchAsJsonAsync(
            $"api/v1/apartments/{apartment}/status", new SetApartmentStatusRequest("INACTIVE"));
        Assert.Equal(HttpStatusCode.Conflict, stillBlocked.StatusCode);
        Assert.Contains("APARTMENT_HAS_CURRENT_OWNERS", await stillBlocked.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await manager.PatchAsJsonAsync(
            $"api/v1/apartments/{apartment}/owners/{secondOwnership}/end", new EndApartmentOwnershipRequest(Today))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PatchAsJsonAsync(
            $"api/v1/apartments/{apartment}/status", new SetApartmentStatusRequest("INACTIVE"))).StatusCode);

        await using var verify = database.Factory.Services.CreateAsyncScope();
        var apartments = verify.ServiceProvider.GetRequiredService<ApartmentsDbContext>();
        Assert.Equal(MasterDataStatus.INACTIVE, (await apartments.ApartmentUnits.AsNoTracking().SingleAsync(x => x.Id == apartment)).Status);
        var history = await apartments.ApartmentOwnerships.AsNoTracking().Where(x => x.ApartmentUnitId == apartment).ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.All(history, x => Assert.NotNull(x.EndDate));
    }

    [Fact]
    public async Task Reactivated_apartment_can_receive_a_new_owner()
    {
        var apartment = await SeedApartmentAsync("REACTIVATE-");
        var owner = await SeedResidentAsync("Owner after reactivation");
        var manager = await ClientAsync("MANAGER");
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PatchAsJsonAsync(
            $"api/v1/apartments/{apartment}/status", new SetApartmentStatusRequest("INACTIVE"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PatchAsJsonAsync(
            $"api/v1/apartments/{apartment}/status", new SetApartmentStatusRequest("ACTIVE"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PostAsJsonAsync(
            $"api/v1/apartments/{apartment}/owners", new ChangeApartmentOwnerRequest(owner.Id, Today))).StatusCode);
    }

    [Fact]
    public async Task Detail_returns_current_and_historical_residents_without_private_pii()
    {
        var apartment = await SeedApartmentAsync();
        var current = await SeedResidentAsync("Current resident", note: "private current");
        var historical = await SeedResidentAsync("Historical resident", note: "private history");
        await SeedResidencyAsync(current.Id, apartment, Today.AddDays(-10));
        await SeedResidencyAsync(historical.Id, apartment, Today.AddYears(-1), Today.AddDays(-1));

        using var response = await (await ClientAsync("STAFF")).GetAsync($"api/v1/apartments/{apartment}");
        response.EnsureSuccessStatusCode();
        var detail = await response.Content.ReadFromJsonAsync<ApartmentDetailResponse>();
        Assert.Contains(detail!.CurrentResidents, x => x.ResidentId == current.Id);
        Assert.Contains(detail.ResidentHistory, x => x.ResidentId == historical.Id);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private current", json);
        Assert.DoesNotContain("private history", json);
        Assert.DoesNotContain("identityNumber", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_filters_pagination_and_global_kpis_are_dataset_wide()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        var occupiedId = Guid.Empty;
        for (var index = 0; index < 12; index++)
        {
            var id = await SeedApartmentAsync($"KPI-{unique}-{index:D2}-", index == 0 ? 22 : 21);
            if (index == 0) occupiedId = id;
        }
        var occupant = await SeedResidentAsync("KPI occupant");
        await SeedResidencyAsync(occupant.Id, occupiedId, Today.AddDays(-1));
        var client = await ClientAsync("ACCOUNTANT");

        var page = await client.GetFromJsonAsync<PagedApartmentsResponse>($"api/v1/apartments?search=KPI-{unique}&pageIndex=1&pageSize=10");
        Assert.NotNull(page); Assert.Equal(12, page.TotalCount); Assert.Equal(10, page.Items.Count); Assert.Equal(12, page.Summary.TotalCount); Assert.Equal(1, page.Summary.OccupiedCount); Assert.Equal(11, page.Summary.VacantCount);
        var second = await client.GetFromJsonAsync<PagedApartmentsResponse>($"api/v1/apartments?search=KPI-{unique}&pageIndex=2&pageSize=10");
        Assert.Equal(2, second!.Items.Count);
        var occupiedTypeId = (await client.GetFromJsonAsync<PagedApartmentsResponse>($"api/v1/apartments?search=KPI-{unique}&floorNumber=22"))!.Items.Single().ApartmentUnitTypeId;
        var floor = await client.GetFromJsonAsync<PagedApartmentsResponse>($"api/v1/apartments?search=KPI-{unique}&floorNumber=22&apartmentUnitTypeId={occupiedTypeId}&occupancy=OCCUPIED");
        Assert.Single(floor!.Items); Assert.Equal(occupiedId, floor.Items[0].Id);
        var vacant = await client.GetFromJsonAsync<PagedApartmentsResponse>($"api/v1/apartments?search=KPI-{unique}&occupancy=VACANT");
        Assert.Equal(11, vacant!.TotalCount);
    }

    [Fact]
    public async Task Occupancy_modes_are_derived_only_from_active_residency()
    {
        var ownerOnly = await SeedApartmentAsync("MODE-OWNER-");
        var residentOnly = await SeedApartmentAsync("MODE-RESIDENT-");
        var ownerAndResident = await SeedApartmentAsync("MODE-BOTH-");
        var ended = await SeedApartmentAsync("MODE-ENDED-");
        var owner = await SeedResidentAsync("Mode owner");
        var resident = await SeedResidentAsync("Mode resident");
        var both = await SeedResidentAsync("Mode both");
        var old = await SeedResidentAsync("Mode ended");
        await AddOwnerAsync(ownerOnly, owner.Id, Today.AddDays(-5));
        await SeedResidencyAsync(resident.Id, residentOnly, Today.AddDays(-5));
        await AddOwnerAsync(ownerAndResident, both.Id, Today.AddDays(-5));
        await SeedResidencyAsync(both.Id, ownerAndResident, Today.AddDays(-5));
        await SeedResidencyAsync(old.Id, ended, Today.AddDays(-10), Today.AddDays(-1));

        var client = await ClientAsync("STAFF");
        var occupied = await client.GetFromJsonAsync<PagedApartmentsResponse>("api/v1/apartments?occupancy=OCCUPIED&pageSize=100");
        var vacant = await client.GetFromJsonAsync<PagedApartmentsResponse>("api/v1/apartments?occupancy=VACANT&pageSize=100");
        Assert.DoesNotContain(occupied!.Items, x => x.Id == ownerOnly);
        Assert.Contains(occupied.Items, x => x.Id == residentOnly);
        Assert.Contains(occupied.Items, x => x.Id == ownerAndResident);
        Assert.Contains(vacant!.Items, x => x.Id == ownerOnly);
        Assert.Contains(vacant.Items, x => x.Id == ended);
    }

    [Fact]
    public async Task Ending_owner_b_preserves_owner_a_history_and_residency()
    {
        var apartment = await SeedApartmentAsync();
        var a = await SeedResidentAsync("Owner A"); var b = await SeedResidentAsync("Owner B");
        var residency = await SeedResidencyAsync(b.Id, apartment, Today.AddDays(-4));
        await AddOwnerAsync(apartment, a.Id, Today.AddDays(-5));
        var bOwnership = await AddOwnerAsync(apartment, b.Id, Today.AddDays(-3));
        using var ended = await (await ClientAsync("MANAGER")).PatchAsJsonAsync($"api/v1/apartments/{apartment}/owners/{bOwnership}/end", new EndApartmentOwnershipRequest(Today));
        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var owners = await scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>().ApartmentOwnerships.AsNoTracking().Where(x => x.ApartmentUnitId == apartment).ToListAsync();
        Assert.Null(owners.Single(x => x.OwnerResidentId == a.Id).EndDate);
        Assert.Equal(Today, owners.Single(x => x.OwnerResidentId == b.Id).EndDate);
        Assert.True(await scope.ServiceProvider.GetRequiredService<ResidentsDbContext>().ResidentApartments.AnyAsync(x => x.Id == residency));
    }

    [Fact]
    public async Task Ending_ownership_is_blocked_for_same_residents_active_owner_occupied_residency()
    {
        var apartment = await SeedApartmentAsync("OWNER-OCCUPIED-");
        var resident = await SeedResidentAsync("Owner occupied");
        await SeedResidencyAsync(resident.Id, apartment, Today.AddDays(-3));
        await using (var scope = database.Factory.Services.CreateAsyncScope())
        {
            var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
            var row = await residents.ResidentApartments.SingleAsync(x => x.ResidentId == resident.Id && x.ApartmentUnitId == apartment);
            row.UpdateHousehold(HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.OWNER_OCCUPIED, null, null, null, DateTimeOffset.UtcNow);
            await residents.SaveChangesAsync();
        }
        var ownership = await AddOwnerAsync(apartment, resident.Id, Today.AddDays(-3));

        using var response = await (await ClientAsync("MANAGER")).PatchAsJsonAsync($"api/v1/apartments/{apartment}/owners/{ownership}/end", new EndApartmentOwnershipRequest(Today));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("OWNER_HAS_ACTIVE_OWNER_OCCUPIED_RESIDENCY", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Default_ownership_dates_use_building_timezone_at_utc_midnight_boundary()
    {
        database.Factory.Clock.Current = new DateTimeOffset(2026, 9, 30, 18, 30, 0, TimeSpan.Zero);
        try
        {
            var apartment = await SeedApartmentAsync(); var resident = await SeedResidentAsync("Timezone owner"); var client = await ClientAsync("MANAGER");
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"api/v1/apartments/{apartment}/owners", new ChangeApartmentOwnerRequest(resident.Id, null))).StatusCode);
            Guid ownership;
            await using (var scope = database.Factory.Services.CreateAsyncScope())
            {
                var row = await scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>().ApartmentOwnerships.AsNoTracking().SingleAsync(x => x.ApartmentUnitId == apartment && x.OwnerResidentId == resident.Id);
                Assert.Equal(Today, row.StartDate); ownership = row.Id;
            }
            Assert.Equal(HttpStatusCode.NoContent, (await client.PatchAsJsonAsync($"api/v1/apartments/{apartment}/owners/{ownership}/end", new EndApartmentOwnershipRequest(null))).StatusCode);
            await using var verify = database.Factory.Services.CreateAsyncScope();
            Assert.Equal(Today, (await verify.ServiceProvider.GetRequiredService<ApartmentsDbContext>().ApartmentOwnerships.AsNoTracking().SingleAsync(x => x.Id == ownership)).EndDate);
        }
        finally { database.Factory.Clock.Current = null; }
    }

    [Theory]
    [InlineData(false, true, false, 1)]
    [InlineData(true, true, false, 1)]
    [InlineData(true, false, false, 0)]
    [InlineData(false, true, true, 0)]
    public async Task Resident_self_view_is_strictly_active_residency_based(bool owner, bool occupant, bool ended, int expectedCount)
    {
        var apartment = await SeedApartmentAsync();
        var linked = await SeedLinkedResidentAsync(apartment, owner, occupant, ended);
        var items = await (await ClientAsync("RESIDENT", linked.UserId)).GetFromJsonAsync<IReadOnlyList<ResidentApartmentSelfItem>>("api/v1/apartments/me");
        Assert.Equal(expectedCount, items!.Count);
        if (expectedCount == 1) Assert.Equal(apartment, items[0].Id);
    }

    [Fact]
    public async Task Resident_cannot_browse_management_list_or_arbitrary_detail()
    {
        var own = await SeedApartmentAsync(); var other = await SeedApartmentAsync();
        var linked = await SeedLinkedResidentAsync(own, false, true, false); var client = await ClientAsync("RESIDENT", linked.UserId);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("api/v1/apartments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"api/v1/apartments/{other}")).StatusCode);
    }

    private static CreateApartmentRequest ValidCreate(Guid typeId) => new("HTTP-" + Guid.NewGuid().ToString("N")[..8], 9, typeId, 72.5m, 2, 2, new DateOnly(2026, 1, 5), "test");

    private async Task<HttpClient> ClientAsync(string role, Guid? userId = null)
    {
        var permissions = role switch
        {
            "MANAGER" => new[] { SystemPermissionCodes.ManageOperations }, "STAFF" => new[] { SystemPermissionCodes.PerformAssignedOperations },
            "ACCOUNTANT" => new[] { SystemPermissionCodes.ManageFinance }, "RESIDENT" => new[] { SystemPermissionCodes.UseResidentServices },
            "ADMIN" => new[] { SystemPermissionCodes.ManageInternalAccounts, SystemPermissionCodes.ViewAdministrationActivity, SystemPermissionCodes.ViewSystemOverview }, _ => []
        };
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var user = userId.HasValue ? await auth.UserAccounts.SingleAsync(x => x.Id == userId) : new UserAccount(role.ToLowerInvariant()+"_"+Guid.NewGuid().ToString("N")[..10], "hash", role, DateTimeOffset.UtcNow, $"{Guid.NewGuid():N}@test.local", status: AccountStatus.ACTIVE, emailVerified: true);
        if (!userId.HasValue) { auth.UserAccounts.Add(user); await auth.SaveChangesAsync(); }
        var admin = scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
        var roleId = await admin.Roles.Where(x => x.Code == role).Select(x => x.Id).SingleAsync();
        if (!await admin.UserRoleAssignments.AnyAsync(x => x.UserId == user.Id && x.RoleId == roleId)) { admin.UserRoleAssignments.Add(new UserRoleAssignment(user.Id, roleId, DateTimeOffset.UtcNow)); await admin.SaveChangesAsync(); }
        var account = new AccountResponse(user.Id, user.Username, user.DisplayName, user.Email, user.PhoneNumber, "ACTIVE", true, role, permissions);
        var client = database.Factory.CreateClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", database.Factory.Services.GetRequiredService<IAuthSecrets>().Issue(account, DateTimeOffset.UtcNow).AccessToken); return client;
    }

    private async Task<Guid> SeedApartmentAsync(string prefix = "AP-", int floor = 1, Guid? typeId = null)
    {
        await using var scope = database.Factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>(); var actualTypeId=typeId??await ApartmentTypeTestData.CreateAsync(db); var row = new ApartmentUnit(prefix+Guid.NewGuid().ToString("N")[..6], floor, database.Factory.Clock.GetUtcNow(), actualTypeId, 80, 2, 2);
        db.ApartmentUnits.Add(row); await db.SaveChangesAsync(); return row.Id;
    }

    private async Task<Guid> SeedApartmentTypeAsync(string? name=null){await using var scope=database.Factory.Services.CreateAsyncScope();return await ApartmentTypeTestData.CreateAsync(scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>(),name);}

    private async Task<Resident> SeedResidentAsync(string name, string? note = null)
    {
        await using var scope = database.Factory.Services.CreateAsyncScope(); var row = new Resident("AR-"+Guid.NewGuid().ToString("N")[..10], name, database.Factory.Clock.GetUtcNow(), note: note);
        var db = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>(); db.Residents.Add(row); await db.SaveChangesAsync(); return row;
    }

    private async Task<Guid> SeedResidencyAsync(Guid residentId, Guid apartmentId, DateOnly start, DateOnly? end = null)
    {
        await using var scope = database.Factory.Services.CreateAsyncScope(); var row = new ResidentApartment(residentId, apartmentId, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT, start, database.Factory.Clock.GetUtcNow(), endDate: end);
        var db = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>(); db.ResidentApartments.Add(row); await db.SaveChangesAsync(); return row.Id;
    }

    private async Task<Guid> AddOwnerAsync(Guid apartmentId, Guid residentId, DateOnly start)
    {
        await using var scope = database.Factory.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<IApartmentOwnershipCommand>().AddOwnerAsync(apartmentId, residentId, start, null, CancellationToken.None);
        return await scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>().ApartmentOwnerships.Where(x => x.ApartmentUnitId == apartmentId && x.OwnerResidentId == residentId && x.EndDate == null).Select(x => x.Id).SingleAsync();
    }

    private async Task<(Guid UserId, Guid ResidentId)> SeedLinkedResidentAsync(Guid apartmentId, bool owner, bool occupant, bool ended)
    {
        await using var scope = database.Factory.Services.CreateAsyncScope(); var now = database.Factory.Clock.GetUtcNow();
        var user = new UserAccount("self_"+Guid.NewGuid().ToString("N")[..10], "hash", "Resident self", now, $"{Guid.NewGuid():N}@self.local", status: AccountStatus.ACTIVE, emailVerified: true);
        var auth = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>(); auth.UserAccounts.Add(user); await auth.SaveChangesAsync();
        var resident = new Resident("SELF-"+Guid.NewGuid().ToString("N")[..8], "Resident self", now, userId: user.Id);
        var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>(); residents.Residents.Add(resident); await residents.SaveChangesAsync();
        if (occupant) { residents.ResidentApartments.Add(new ResidentApartment(resident.Id, apartmentId, HouseholdRole.HOUSEHOLD_HEAD, owner ? ResidencyType.OWNER_OCCUPIED : ResidencyType.TENANT, Today.AddDays(-3), now, endDate: ended ? Today.AddDays(-1) : null)); await residents.SaveChangesAsync(); }
        if (owner) await scope.ServiceProvider.GetRequiredService<IApartmentOwnershipCommand>().AddOwnerAsync(apartmentId, resident.Id, Today.AddDays(-3), null, CancellationToken.None);
        return (user.Id, resident.Id);
    }
}
