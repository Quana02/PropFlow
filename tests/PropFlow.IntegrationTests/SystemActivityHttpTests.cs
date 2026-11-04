using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Data.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;
using PropFlow.Modules.Administration.Infrastructure;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Administration.Presentation;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

[Trait("UseCase", "FE-15.9 System-wide audit extension")]
public sealed class SystemActivityHttpTests(AuthDatabaseFixture database) : IClassFixture<AuthDatabaseFixture>
{
    private const string Endpoint = "api/v1/administration/system-activity";
    private async Task<(HttpClient Client, Guid Id, string Username)> Client(string role, WebApplicationFactory<Program>? factory = null)
    {
        factory ??= database.Factory;
        using var scope = factory.Services.CreateScope();
        var now = database.Factory.Clock.GetUtcNow();
        var auth = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var access = scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
        var username = "audit_" + Guid.NewGuid().ToString("N")[..12];
        var user = new UserAccount(username, "unused-test-hash", "Người thử nhật ký", now, status: AccountStatus.ACTIVE, emailVerified: true);
        auth.UserAccounts.Add(user); await auth.SaveChangesAsync();
        var assigned = await access.Roles.Include(x => x.RolePermissions).ThenInclude(x => x.Permission).SingleAsync(x => x.Code == role);
        access.UserRoleAssignments.Add(new UserRoleAssignment(user.Id, assigned.Id, now)); await access.SaveChangesAsync();
        var account = new AccountResponse(user.Id, username, user.DisplayName, null, null, "ACTIVE", true, role, assigned.RolePermissions.Select(x => x.Permission.Code).ToArray());
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scope.ServiceProvider.GetRequiredService<IAuthSecrets>().Issue(account, now).AccessToken);
        return (client, user.Id, username);
    }

    [Fact]
    public async Task Apartment_type_create_and_edit_record_actor_and_safe_before_after_values()
    {
        var manager = await Client("MANAGER"); using var client = manager.Client;
        using var created = await client.PostAsJsonAsync("api/v1/apartment-unit-types", new { Name = "Audit " + Guid.NewGuid(), DefaultUsableAreaM2 = 45, DefaultBedroomCount = 1, DefaultBathroomCount = 1 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var entity = (await created.Content.ReadFromJsonAsync<PropFlow.Modules.Apartments.Presentation.ApartmentUnitTypeResponse>())!;
        using var edited = await client.PutAsJsonAsync($"api/v1/apartment-unit-types/{entity.Id}", new { Name = entity.Name, DefaultUsableAreaM2 = 50, DefaultBedroomCount = 1, DefaultBathroomCount = 1 });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var admin = await Client("ADMIN"); using var reader = admin.Client;
        var page = (await reader.GetFromJsonAsync<PagedAdministrationActivitiesResponse>(Endpoint + "?module=Apartments&pageSize=100"))!;
        var row = Assert.Single(page.Items.Where(x => x.EntityId == entity.Id && x.Action == "updated"));
        Assert.Equal(manager.Id, row.ActorUserId);
        Assert.Equal("SUCCESS", row.Outcome);
        var area = Assert.Single(row.Changes!.Where(x => x.Field == "DefaultUsableAreaM2"));
        Assert.Equal(45m, decimal.Parse(area.Before!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(50m, decimal.Parse(area.After!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains(page.Items, x => x.EntityId == entity.Id && x.Action == "created");
    }

    [Fact]
    public async Task Resident_personal_fields_are_redacted_in_persisted_audit()
    {
        var seed = await database.SeedRegistrationResidentAsync($"audit-pii-{Guid.NewGuid():N}@example.invalid");
        var manager = await Client("MANAGER"); using var client = manager.Client;
        using var scope = database.Factory.Services.CreateScope();
        var resident = await scope.ServiceProvider.GetRequiredService<ResidentsDbContext>().Residents.AsNoTracking().SingleAsync(x => x.Id == seed.ResidentId);
        var privateName = "Thông tin riêng " + Guid.NewGuid();
        using var response = await client.PutAsJsonAsync($"api/v1/residents/{seed.ResidentId}", new { FullName = privateName,
            DateOfBirth = new DateOnly(1990, 1, 1), resident.Gender, Nationality = "Việt Nam", resident.IdentityType, resident.IdentityNumber,
            resident.IdentityIssuedDate, resident.IdentityExpiryDate, resident.PhoneNumber, resident.Email, resident.Note });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var rows = await scope.ServiceProvider.GetRequiredService<AdministrationDbContext>().AuditLogs.AsNoTracking().Where(x => x.EntityId == seed.ResidentId).ToArrayAsync();
        Assert.NotEmpty(rows);
        Assert.All(rows, x => { Assert.DoesNotContain(privateName, x.NewValues!); Assert.DoesNotContain(seed.IdentityNumber, x.NewValues!); Assert.DoesNotContain(seed.Email, x.NewValues!); });
        var update = Assert.Single(rows.Where(x => x.Action == "updated"));
        var metadata = System.Text.Json.JsonSerializer.Deserialize<ActivityMetadata>(update.NewValues!)!;
        Assert.Equal("[Đã ẩn nội dung]", Assert.Single(metadata.Changes!.Where(x => x.Field == "FullName")).After);
    }

    [Fact]
    public async Task Denied_mutation_records_failure_without_recording_a_successful_data_change()
    {
        var staff = await Client("STAFF"); using var client = staff.Client;
        using var response = await client.PostAsJsonAsync("api/v1/apartment-unit-types", new { Name = "Denied " + Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var admin = await Client("ADMIN"); using var reader = admin.Client;
        var page = (await reader.GetFromJsonAsync<PagedAdministrationActivitiesResponse>($"{Endpoint}?module=Apartments&actor={staff.Username}&outcome=DENIED"))!;
        var row = Assert.Single(page.Items); Assert.Equal(403, row.HttpStatus); Assert.Equal("operation", row.Action);
        Assert.Empty(row.Changes!);
    }

    [Fact]
    public async Task Failed_login_is_recorded_without_password_otp_or_request_content()
    {
        using var client = database.Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = (await client.GetFromJsonAsync<CsrfResponse>("api/v1/auth/csrf"))!;
        var secret = "Never-log-this-" + Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/login") { Content = JsonContent.Create(new LoginRequest("not-an-account", secret)) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var admin = await Client("ADMIN"); using var reader = admin.Client;
        var page = (await reader.GetFromJsonAsync<PagedAdministrationActivitiesResponse>(Endpoint + "?module=Authentication"))!;
        var login = Assert.Single(page.Items.Where(x => x.Label == "Đăng nhập"));
        Assert.Equal(401, login.HttpStatus);
        Assert.Equal("DENIED", login.Outcome);
        using var scope = database.Factory.Services.CreateScope();
        var metadata = await scope.ServiceProvider.GetRequiredService<AdministrationDbContext>().AuditLogs.Select(x => x.NewValues).ToArrayAsync();
        Assert.DoesNotContain(metadata, x => x?.Contains(secret, StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData("MANAGER")][InlineData("STAFF")][InlineData("ACCOUNTANT")][InlineData("RESIDENT")]
    public async Task Non_admin_cannot_read_system_activity(string role)
    {
        var user = await Client(role); using var client = user.Client;
        using var response = await client.GetAsync(Endpoint); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reading_logs_does_not_generate_more_logs()
    {
        var admin = await Client("ADMIN"); using var client = admin.Client;
        using var scope = database.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
        var before = await db.AuditLogs.CountAsync();
        using var response = await client.GetAsync(Endpoint); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Filters_use_building_dates_actor_and_stable_pagination()
    {
        var admin = await Client("ADMIN"); using var client = admin.Client;
        using var scope = database.Factory.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<ISystemAuditWriter>();
        foreach (var time in new[] { "2026-11-03T16:59:59Z", "2026-11-03T17:00:00Z", "2026-11-04T16:59:59Z", "2026-11-04T17:00:00Z" })
            await writer.AppendAsync(new("Residents", "test_window", "Kiểm thử khoảng ngày", "Resident", null, admin.Id, "SUCCESS", DateTimeOffset.Parse(time), null, []), default);
        var query = $"{Endpoint}?module=Residents&actor={admin.Username}&action=test_window&dateFrom=2026-11-04&dateTo=2026-11-04&pageSize=1";
        var first = (await client.GetFromJsonAsync<PagedAdministrationActivitiesResponse>(query))!;
        var second = (await client.GetFromJsonAsync<PagedAdministrationActivitiesResponse>(query + "&page=2"))!;
        Assert.Equal(2, first.Total); Assert.Single(first.Items); Assert.Single(second.Items); Assert.NotEqual(first.Items[0].Id, second.Items[0].Id);
        Assert.Equal("Asia/Ho_Chi_Minh", first.TimeZoneId);
        using var bad = await client.GetAsync(Endpoint + "?dateFrom=2026-11-05&dateTo=2026-11-04"); Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Audit_failure_rolls_back_the_source_change_and_does_not_log_false_success()
    {
        await using var factory = database.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddScoped<SystemAuditWriter>(); services.RemoveAll<ISystemAuditWriter>();
            services.AddScoped<ISystemAuditWriter>(sp => new FailTransactionalAudit(sp.GetRequiredService<SystemAuditWriter>()));
        }));
        var manager = await Client("MANAGER", factory); using var client = manager.Client;
        var name = "Rollback " + Guid.NewGuid();
        using var response = await client.PostAsJsonAsync("api/v1/apartment-unit-types", new { Name = name });
        Assert.False(response.IsSuccessStatusCode);
        using var scope = database.Factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>().ApartmentUnitTypes.AnyAsync(x => x.Name == name));
        var logs = await scope.ServiceProvider.GetRequiredService<AdministrationDbContext>().AuditLogs.AsNoTracking().Where(x => x.ActorUserId == manager.Id).ToArrayAsync();
        Assert.DoesNotContain(logs, x => x.Action == "created");
        Assert.Contains(logs, x => x.NewValues!.Contains("FAILED"));
    }
    private sealed class FailTransactionalAudit(ISystemAuditWriter inner) : ISystemAuditWriter
    {
        public Task AppendAsync(SystemAuditEvent entry, CancellationToken ct) => inner.AppendAsync(entry, ct);
        public Task AppendInTransactionAsync(SystemAuditEvent entry, DbConnection connection, DbTransaction transaction, CancellationToken ct) => throw new InvalidOperationException("Injected audit failure.");
    }
}
