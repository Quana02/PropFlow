using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;
using PropFlow.Modules.Apartments.Presentation;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Billing.Domain.Invoices;
using PropFlow.Modules.Billing.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Domain.Payments;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.Residents.Domain.ResidentApartments;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Presentation;

namespace PropFlow.IntegrationTests;

public sealed partial class PropertyOperationsAuthorizationHttpTests
{
    private static string ContactEmail() => $"b6-contact-{Guid.NewGuid():N}@example.invalid";

    [Fact]
    [Trait("Feature", "FE-02")]
    public async Task Resident_profile_and_status_require_manager_and_keep_login_identity_and_relationships()
    {
        var seed = await Database.SeedResidentAsync(ContactEmail());
        var resident = await fixture.Login("RESIDENT", residentId: seed.ResidentId);
        var other = await Database.SeedResidentAsync(ContactEmail());
        var route = $"residents/{seed.ResidentId}";
        var email = ContactEmail();
        var input = new
        {
            fullName = "Cư dân đã cập nhật", dateOfBirth = new DateOnly(1990, 1, 1), nationality = "VN",
            identityType = "CCCD", identityNumber = seed.IdentityNumber, phoneNumber = seed.PhoneNumber, email,
            userId = resident.Id, role = "ADMIN", status = "INACTIVE", residentId = other.ResidentId,
            permissions = new[] { "*" }, apartmentUnitId = other.ApartmentId
        };
        using var scope = Database.Factory.Services.CreateScope();
        var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var auth = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var loginEmail = await auth.UserAccounts.Where(x => x.Id == resident.Id).Select(x => x.Email).SingleAsync();
        var finance = await SeedFinancialHistory(seed.ApartmentId, seed.ResidentId, resident.Id, seed.Email);
        await Denied(HttpMethod.Put, route, input, ResidentSnapshot);
        await Check(Manager.Client, HttpMethod.Put, route, input, HttpStatusCode.NoContent);
        var updated = await residents.Residents.AsNoTracking().SingleAsync(x => x.Id == seed.ResidentId);
        Assert.Equal("Cư dân đã cập nhật", updated.FullName);
        Assert.Equal(email, updated.Email);
        Assert.Equal(resident.Id, updated.UserId);
        Assert.Equal(ResidentStatus.ACTIVE, updated.Status);
        Assert.Equal(Manager.Id, updated.UpdatedBy);
        Assert.Equal(loginEmail, await auth.UserAccounts.Where(x => x.Id == resident.Id).Select(x => x.Email).SingleAsync());
        Assert.Equal(other.Email, (await residents.Residents.AsNoTracking().SingleAsync(x => x.Id == other.ResidentId)).Email);
        var accountant = (await fixture.Actor("ACCOUNTANT").Client.GetFromJsonAsync<ResidentDetailResponse>("/api/v1/" + route))!;
        Assert.Null(accountant.Email);
        Assert.Null(accountant.PhoneNumber);

        var statusRoute = route + "/status";
        var deactivate = new SetResidentStatusRequest(ResidentStatus.INACTIVE);
        await Denied(HttpMethod.Patch, statusRoute, deactivate, ResidentSnapshot);
        var relationships = await residents.ResidentApartments.AsNoTracking().Where(x => x.ResidentId == seed.ResidentId)
            .Select(x => new { x.Id, x.Status, x.EndDate }).ToArrayAsync();
        await Check(Manager.Client, HttpMethod.Patch, statusRoute, deactivate, HttpStatusCode.NoContent);
        await Check(Manager.Client, HttpMethod.Patch, statusRoute, deactivate, HttpStatusCode.NoContent);
        var inactive = await residents.Residents.AsNoTracking().SingleAsync(x => x.Id == seed.ResidentId);
        Assert.Equal(ResidentStatus.INACTIVE, inactive.Status);
        Assert.Equal(resident.Id, inactive.UserId);
        var residenceSource = scope.ServiceProvider.GetRequiredService<IResidentResidenceSource>();
        var date = DateOnly.FromDateTime(Now.UtcDateTime);
        Assert.Null(await residenceSource.FindActiveAsync(resident.Id, date, default));
        Assert.Equal(seed.ResidentId, (await resident.Client.GetFromJsonAsync<ResidentDetailResponse>("/api/v1/residents/me"))!.Id);
        Assert.Equal(relationships, await residents.ResidentApartments.AsNoTracking().Where(x => x.ResidentId == seed.ResidentId)
            .Select(x => new { x.Id, x.Status, x.EndDate }).ToArrayAsync());
        await Denied(HttpMethod.Patch, statusRoute, new { status = 99 }, ResidentSnapshot, [(Manager.Client, HttpStatusCode.BadRequest)]);
        await Check(Manager.Client, HttpMethod.Patch, statusRoute, new SetResidentStatusRequest(ResidentStatus.ACTIVE), HttpStatusCode.NoContent);
        Assert.Equal(ResidentStatus.ACTIVE, await residents.Residents.Where(x => x.Id == seed.ResidentId).Select(x => x.Status).SingleAsync());
        Assert.Equal(seed.ApartmentId, (await residenceSource.FindActiveAsync(resident.Id, date, default))!.ApartmentUnitId);
        Assert.Equal(seed.ApartmentId, Assert.Single((await resident.Client.GetFromJsonAsync<ResidentApartmentSelfItem[]>("/api/v1/apartments/me"))!).Id);
        Assert.Equal(finance, await FinancialHistory());
    }

    [Fact]
    [Trait("Feature", "FE-02")]
    public async Task Residency_add_move_and_end_require_manager_and_preserve_parent_and_history_under_race()
    {
        var source = await Database.SeedResidentAsync(ContactEmail());
        var other = await Database.SeedResidentAsync(ContactEmail());
        var alternate = await Database.SeedRegistrationResidentAsync(ContactEmail());
        var destination = await Database.SeedRegistrationResidentAsync(ContactEmail());
        var resident = await fixture.Login("RESIDENT", residentId: source.ResidentId);
        var finance = await SeedFinancialHistory(source.ApartmentId, source.ResidentId, resident.Id, source.Email);
        using var scope = Database.Factory.Services.CreateScope();
        var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var original = await residents.ResidentApartments.AsNoTracking().SingleAsync(x => x.ResidentId == source.ResidentId);
        var addRoute = $"residents/{source.ResidentId}/residencies";
        var today = original.StartDate.AddDays(1);
        CreateResidencyRequest At(Guid apartment) => new(apartment, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.TENANT, null, null, today, null, null);
        var add = At(alternate.ApartmentId);
        await Denied(HttpMethod.Post, addRoute, add, ResidentSnapshot);
        await Check(Manager.Client, HttpMethod.Post, addRoute, add, HttpStatusCode.OK);
        await Denied(HttpMethod.Post, addRoute, add, ResidentSnapshot, [(Manager.Client, HttpStatusCode.Conflict)]);
        var added = await residents.ResidentApartments.AsNoTracking().SingleAsync(x => x.ResidentId == source.ResidentId && x.ApartmentUnitId == alternate.ApartmentId);
        Assert.Equal(Manager.Id, added.CreatedBy);
        var endRoute = addRoute + $"/{added.Id}/end";
        var end = new EndResidencyRequest(today);
        await Denied(HttpMethod.Patch, endRoute, end, ResidentSnapshot);
        await Denied(HttpMethod.Patch, $"residents/{other.ResidentId}/residencies/{added.Id}/end", end, ResidentSnapshot, [(Manager.Client, HttpStatusCode.NotFound)]);
        await Denied(HttpMethod.Patch, endRoute, new EndResidencyRequest(today.AddDays(-1)), ResidentSnapshot, [(Manager.Client, HttpStatusCode.BadRequest)]);
        await OneTransition(HttpMethod.Patch, endRoute, end, endRoute, end, HttpStatusCode.NoContent);
        var ended = await residents.ResidentApartments.AsNoTracking().SingleAsync(x => x.Id == added.Id);
        Assert.Equal(ResidencyStatus.ENDED, ended.Status);
        Assert.Equal(today, ended.EndDate);
        Assert.Equal(Manager.Id, ended.UpdatedBy);

        var moveRoute = addRoute + "/move";
        var move = new MoveResidencyRequest(original.Id, At(destination.ApartmentId));
        await Denied(HttpMethod.Post, moveRoute, move, ResidentSnapshot);
        await Denied(HttpMethod.Post, $"residents/{other.ResidentId}/residencies/move", move, ResidentSnapshot, [(Manager.Client, HttpStatusCode.NotFound)]);
        await Denied(HttpMethod.Post, moveRoute, move with { Target = move.Target with { StartDate = original.StartDate.AddDays(-1) } },
            ResidentSnapshot, [(Manager.Client, HttpStatusCode.BadRequest)]);
        await OneTransition(HttpMethod.Post, moveRoute, move, moveRoute, move with { Target = At(alternate.ApartmentId) }, HttpStatusCode.OK);
        await Denied(HttpMethod.Post, moveRoute, move, ResidentSnapshot, [(Manager.Client, HttpStatusCode.Conflict)]);
        var history = await residents.ResidentApartments.AsNoTracking().Where(x => x.ResidentId == source.ResidentId).ToArrayAsync();
        Assert.Equal(3, history.Length);
        Assert.Contains(history, x => x.Id == original.Id && x.EndDate == today && x.UpdatedBy == Manager.Id);
        var current = Assert.Single(history, x => x.Status == ResidencyStatus.ACTIVE);
        Assert.Contains(current.ApartmentUnitId, new[] { alternate.ApartmentId, destination.ApartmentId });
        var own = (await resident.Client.GetFromJsonAsync<ResidentDetailResponse>("/api/v1/residents/me"))!;
        Assert.Contains(own.Residencies, x => x.Id == current.Id);
        Assert.DoesNotContain(own.Residencies, x => x.ApartmentUnitId == other.ApartmentId);
        Assert.Equal(current.ApartmentUnitId,
            Assert.Single((await resident.Client.GetFromJsonAsync<ResidentApartmentSelfItem[]>("/api/v1/apartments/me"))!).Id);
        Assert.Equal(finance, await FinancialHistory());
    }

    [Fact]
    [Trait("Feature", "FE-03")]
    public async Task Ownership_end_requires_manager_matching_apartment_and_preserves_coowner_history()
    {
        var owner = await Database.SeedOwnerOnlyResidentAsync(ContactEmail());
        var coowner = await Database.SeedOwnerOnlyResidentAsync(ContactEmail(), owner.ApartmentId);
        var unrelated = await Database.SeedOwnerOnlyResidentAsync(ContactEmail());
        var resident = await fixture.Login("RESIDENT", residentId: owner.ResidentId);
        using var scope = Database.Factory.Services.CreateScope();
        var apartments = scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>();
        var ownership = await apartments.ApartmentOwnerships.AsNoTracking().SingleAsync(x => x.OwnerResidentId == owner.ResidentId);
        var date = ownership.StartDate.AddDays(1);
        var finance = await SeedFinancialHistory(owner.ApartmentId, owner.ResidentId, resident.Id, ContactEmail());
        var request = new EndApartmentOwnershipRequest(date);
        var route = $"apartments/{owner.ApartmentId}/owners/{ownership.Id}/end";
        await Denied(HttpMethod.Patch, route, request, ResidentSnapshot);
        await Denied(HttpMethod.Patch, $"apartments/{unrelated.ApartmentId}/owners/{ownership.Id}/end", request, ResidentSnapshot,
            [(Manager.Client, HttpStatusCode.NotFound)]);
        await Denied(HttpMethod.Patch, route, new EndApartmentOwnershipRequest(ownership.StartDate.AddDays(-1)), ResidentSnapshot,
            [(Manager.Client, HttpStatusCode.BadRequest)]);
        await OneTransition(HttpMethod.Patch, route, request, route, request, HttpStatusCode.NoContent);
        var closed = await apartments.ApartmentOwnerships.AsNoTracking().SingleAsync(x => x.Id == ownership.Id);
        Assert.Equal(date, closed.EndDate);
        Assert.Equal(Manager.Id, closed.UpdatedBy);
        Assert.Null(await apartments.ApartmentOwnerships.Where(x => x.OwnerResidentId == coowner.ResidentId).Select(x => x.EndDate).SingleAsync());
        Assert.Null(await apartments.ApartmentOwnerships.Where(x => x.OwnerResidentId == unrelated.ResidentId).Select(x => x.EndDate).SingleAsync());
        Assert.Equal(finance, await FinancialHistory());
    }

    private async Task<string> SeedFinancialHistory(Guid apartmentId, Guid residentId, Guid userId, string email)
    {
        using var scope = Database.Factory.Services.CreateScope();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var payments = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        await billing.Database.MigrateAsync();
        await payments.Database.MigrateAsync();
        var now = Now;
        var date = DateOnly.FromDateTime(now.UtcDateTime);
        var invoice = new Invoice("B6-" + Guid.NewGuid().ToString("N"), apartmentId, date, date, Manager.Id, now);
        invoice.AddItem("SERVICE", "Phí kiểm thử", "FIXED", 1, 100000, Manager.Id, now);
        invoice.Issue(Manager.Id, date, now, date.AddDays(7));
        billing.Invoices.Add(invoice);
        billing.InvoiceRecipients.Add(new InvoiceRecipient(invoice.Id, userId, "Cư dân kiểm thử B6", email, "B6-01", 70, residentId));
        await billing.SaveChangesAsync();
        var payment = new Payment("B6-" + Guid.NewGuid().ToString("N"), invoice.Id, 40000, "CASH", now, now);
        payment.Confirm(Manager.Id, 40000, now);
        payments.Payments.Add(payment);
        await payments.SaveChangesAsync();
        return await FinancialHistory();
    }

    private async Task<string> FinancialHistory()
    {
        using var scope = Database.Factory.Services.CreateScope();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var payments = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        return JsonSerializer.Serialize(new
        {
            Invoices = await billing.Invoices.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Recipients = await billing.InvoiceRecipients.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Payments = await payments.Payments.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()
        });
    }

    private async Task OneTransition(HttpMethod method, string route, object? first, string secondRoute, object? second, HttpStatusCode success)
    {
        var responses = await Task.WhenAll(Send(Manager.Client, method, route, first), Send(Manager.Client, method, secondRoute, second));
        using var left = responses[0];
        using var right = responses[1];
        var ordered = responses.OrderBy(x => (int)x.StatusCode).ToArray();
        await Expect(ordered[0], success);
        await Expect(ordered[1], HttpStatusCode.Conflict);
    }
}
