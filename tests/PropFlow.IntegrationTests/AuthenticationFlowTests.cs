using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Domain.ResidentApartments;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Administration.Domain.Permissions;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;
using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed record RegistrationResidentSeed(
    Guid ResidentId,
    Guid ApartmentId,
    Guid? OwnershipId,
    Guid? ResidencyId,
    string Email,
    string PhoneNumber,
    string IdentityType,
    string IdentityNumber);

// Real HTTP handlers and PostgreSQL. Only outbound email is captured, exclusively in this test host.
public sealed class AuthTestMail : IAuthEmail
{
    public ConcurrentDictionary<string, string> Codes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool FailNext { get; set; }
    public Task SendCodeAsync(string email, string code, bool recovery, int expiryMinutes, CancellationToken ct)
    {
        if (FailNext) { FailNext = false; throw new InvalidOperationException("Test delivery failure"); }
        Codes[email] = code; return Task.CompletedTask;
    }
}
public sealed class AuthTestClock : TimeProvider
{
    public TimeSpan Offset { get; set; }
    public DateTimeOffset? Current { get; set; }
    public override DateTimeOffset GetUtcNow() => Current ?? DateTimeOffset.UtcNow.Add(Offset);
}
public sealed class AuthFlowFactory(string connection) : PropFlowApiFactory
{
    public AuthTestMail Mail { get; } = new();
    public AuthTestClock Clock { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:PropFlowDatabase"] = connection,
            ["Authentication:RateLimits:auth-login:PermitLimit"] = "1000",
            ["Authentication:RateLimits:auth-entry:PermitLimit"] = "1000"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAuthEmail>(); services.AddSingleton<IAuthEmail>(Mail);
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(Clock);
        });
    }
}

public sealed class AuthDatabaseFixture : IAsyncLifetime
{
    private string databaseName = "";
    private string adminConnection = "";
    public AuthFlowFactory Factory { get; private set; } = null!;
    public async Task InitializeAsync()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "PropFlow.sln"))) root = root.Parent;
        if (root == null) throw new DirectoryNotFoundException("Không tìm thấy PropFlow.sln từ thư mục kiểm thử.");
        var configured = new NpgsqlConnectionStringBuilder(TestDatabaseConfiguration.GetConnectionString());
        if (configured.Database != "propflow_test") throw new InvalidOperationException("Auth tests require the configured propflow_test connection as the isolated test server source.");
        databaseName = "propflow_fe01_test_" + Guid.NewGuid().ToString("N");
        configured.Database = "postgres";
        adminConnection = configured.ConnectionString;
        await using (var admin = new NpgsqlConnection(adminConnection))
        {
            await admin.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
            await command.ExecuteNonQueryAsync();
        }
        configured.Database = databaseName;
        Factory = new AuthFlowFactory(configured.ConnectionString);
        using var scope = Factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        foreach (var context in new DbContext[] { services.GetRequiredService<AuthenticationDbContext>(), services.GetRequiredService<PropertyAssetsDbContext>(), services.GetRequiredService<ApartmentsDbContext>(), services.GetRequiredService<ResidentsDbContext>(), services.GetRequiredService<AdministrationDbContext>() })
            Assert.Equal(databaseName, context.Database.GetDbConnection().Database);
        // Initial schemas must exist before the cross-module integrity migrations run.
        await services.GetRequiredService<AuthenticationDbContext>().GetService<IMigrator>().MigrateAsync("20260914101250_CorrectAuthenticationEmailOtpVerification");
        await services.GetRequiredService<PropertyAssetsDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<ApartmentsDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<ResidentsDbContext>().GetService<IMigrator>().MigrateAsync("20260914063807_InitialResidents");
        await services.GetRequiredService<AdministrationDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<AuthenticationDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<ResidentsDbContext>().Database.MigrateAsync();
    }
    public async Task DisposeAsync()
    {
        if (Factory != null) await Factory.DisposeAsync();
        if (!databaseName.StartsWith("propflow_fe01_test_", StringComparison.Ordinal) || databaseName.Length != 51) return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(adminConnection);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
        await command.ExecuteNonQueryAsync();
    }
    public Task<RegistrationResidentSeed> SeedResidentAsync(string email) =>
        SeedRegistrationResidentAsync(email, activeResidency: true);

    public async Task<(Guid ResidentId, Guid ApartmentId)> SeedOwnerOnlyResidentAsync(string email, Guid? apartmentId = null)
    {
        var seed = await SeedRegistrationResidentAsync(email, currentOwnership: true, apartmentId: apartmentId);
        return (seed.ResidentId, seed.ApartmentId);
    }

    public async Task<RegistrationResidentSeed> SeedRegistrationResidentAsync(
        string email,
        string phoneNumber = "0363602027",
        string identityType = "CCCD",
        string? identityNumber = null,
        bool currentOwnership = false,
        bool endedOwnership = false,
        bool activeResidency = false,
        bool endedResidency = false,
        bool inactive = false,
        bool alreadyLinked = false,
        Guid? apartmentId = null)
    {
        using var scope = Factory.Services.CreateScope();
        var now = Factory.Clock.GetUtcNow();
        var apartments = scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>();
        var apartment = apartmentId.HasValue
            ? await apartments.ApartmentUnits.SingleAsync(x => x.Id == apartmentId.Value)
            : new ApartmentUnit($"O{Guid.NewGuid():N}"[..10], 1, now, await ApartmentTypeTestData.CreateAsync(apartments));
        if (!apartmentId.HasValue)
        {
            apartments.ApartmentUnits.Add(apartment);
            await apartments.SaveChangesAsync();
        }

        Guid? linkedUserId = null;
        if (alreadyLinked)
        {
            var linkedAccount = new UserAccount(
                "linked_" + Guid.NewGuid().ToString("N")[..12],
                "test-password-hash",
                "Tài khoản đã liên kết",
                now,
                $"linked-{Guid.NewGuid():N}@example.invalid",
                status: AccountStatus.ACTIVE,
                emailVerified: true);
            var authentication = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
            authentication.UserAccounts.Add(linkedAccount);
            await authentication.SaveChangesAsync();
            linkedUserId = linkedAccount.Id;
        }

        identityNumber ??= string.Concat(Enumerable.Range(0, 12).Select(_ => Random.Shared.Next(0, 10)));
        var resident = new Resident(
            Guid.NewGuid().ToString("N")[..20],
            "Cư dân đăng ký kiểm thử",
            dateOfBirth: null,
            gender: null,
            nationality: null,
            identityType,
            identityNumber,
            identityIssuedDate: null,
            identityExpiryDate: null,
            now,
            userId: linkedUserId,
            phoneNumber,
            email);
        if (inactive) resident.Deactivate(null, now);
        var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        residents.Residents.Add(resident);
        await residents.SaveChangesAsync();

        Guid? ownershipId = null;
        if (currentOwnership || endedOwnership)
        {
            var ownership = new ApartmentOwnership(
                apartment.Id,
                resident.Id,
                DateOnly.FromDateTime(now.AddDays(-5).DateTime),
                now,
                null);
            if (endedOwnership)
                ownership.End(DateOnly.FromDateTime(now.AddDays(-1).DateTime), now, null);
            apartments.ApartmentOwnerships.Add(ownership);
            await apartments.SaveChangesAsync();
            ownershipId = ownership.Id;
        }

        Guid? residencyId = null;
        if (activeResidency || endedResidency)
        {
            var residency = new ResidentApartment(
                resident.Id,
                apartment.Id,
                HouseholdRole.HOUSEHOLD_HEAD,
                ResidencyType.OWNER_OCCUPIED,
                DateOnly.FromDateTime(now.AddDays(-5).DateTime),
                now);
            if (endedResidency)
                residency.EndResidency(DateOnly.FromDateTime(now.AddDays(-1).DateTime), null, now);
            residents.ResidentApartments.Add(residency);
            await residents.SaveChangesAsync();
            residencyId = residency.Id;
        }

        return new(resident.Id, apartment.Id, ownershipId, residencyId, email, phoneNumber, identityType, identityNumber);
    }

    public async Task EndOwnershipAsync(RegistrationResidentSeed seed)
    {
        Assert.NotNull(seed.OwnershipId);
        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IApartmentOwnershipCommand>().EndOwnershipAsync(
            seed.ApartmentId,
            seed.OwnershipId!.Value,
            DateOnly.FromDateTime(Factory.Clock.GetLocalNow().DateTime),
            null,
            CancellationToken.None);
    }

    public async Task ChangeRegistrationMatchFieldAsync(RegistrationResidentSeed seed, string field)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var resident = await db.Residents.SingleAsync(x => x.Id == seed.ResidentId);
        resident.UpdateProfile(resident.FullName, resident.DateOfBirth, resident.Gender, resident.Nationality,
            resident.IdentityType, field == "identity" ? "064204019999" : resident.IdentityNumber,
            resident.IdentityIssuedDate, resident.IdentityExpiryDate,
            field == "phone" ? "0399999999" : resident.PhoneNumber,
            field == "email" ? $"changed-{Guid.NewGuid():N}@example.invalid" : resident.Email,
            resident.Note, null, Factory.Clock.GetUtcNow());
        await db.SaveChangesAsync();
    }
}

[Trait("Feature", "FE-01")]
public sealed class AuthenticationFlowTests(AuthDatabaseFixture database) : IClassFixture<AuthDatabaseFixture>
{
    private const string Password = "A secure passphrase 123!";
    private HttpClient Client() => database.Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body, bool csrf = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/" + path) { Content = JsonContent.Create(body) };
        if (csrf) { var token = await client.GetFromJsonAsync<CsrfResponse>("api/v1/auth/csrf"); request.Headers.Add("X-CSRF-TOKEN", token!.Token); }
        return await client.SendAsync(request);
    }
    private async Task<RegisterRequest> RegistrationRequestAsync(string username, string displayName, string email, string password = Password)
    {
        using var scope = database.Factory.Services.CreateScope();
        var resident = await scope.ServiceProvider.GetRequiredService<ResidentsDbContext>().Residents.AsNoTracking()
            .Where(x => x.Email != null && x.Email.ToLower() == email.Trim().ToLower())
            .Select(x => new { x.PhoneNumber, x.IdentityType, x.IdentityNumber })
            .SingleAsync();
        return new(username, displayName, email, password, resident.PhoneNumber, resident.IdentityType, resident.IdentityNumber);
    }

    [Fact, Trait("UseCase", "FE-01.1")]
    public async Task Registration_requires_all_four_fields_to_match_the_same_eligible_resident()
    {
        using var client = Client();
        var token = Guid.NewGuid().ToString("N")[..10];
        var email = $"four-field-{token}@example.invalid";
        const string phone = "0363602027";
        const string identityType = "CCCD";
        const string identityNumber = "064204010555";
        await database.SeedRegistrationResidentAsync(email, phone, identityType, identityNumber, currentOwnership: true);

        using var wrongPhone = await Post(client, "register", new
        {
            username = "wrong_phone_" + token,
            displayName = "Sai số điện thoại",
            email,
            password = Password,
            phoneNumber = "0399999999",
            identityType,
            identityNumber
        });

        Assert.Equal(HttpStatusCode.OK, wrongPhone.StatusCode);
        var rejected = (await wrongPhone.Content.ReadFromJsonAsync<ChallengeResponse>())!;
        Assert.Equal(Guid.Empty, rejected.ChallengeId);
        Assert.Equal("Thông tin cư dân không khớp hoặc chưa đủ điều kiện đăng ký tài khoản.", rejected.Message);
        Assert.False(database.Factory.Mail.Codes.ContainsKey(email));

        using var scope = database.Factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>().UserAccounts
            .AnyAsync(x => x.Username == "wrong_phone_" + token));
    }

    [Theory, Trait("UseCase", "FE-01.1")]
    [InlineData("wrong-email")]
    [InlineData("wrong-phone")]
    [InlineData("wrong-identity-type")]
    [InlineData("wrong-identity-number")]
    public async Task Registration_rejects_every_partial_match_without_creating_account_or_otp(string mismatch)
    {
        using var client = Client();
        var token = Guid.NewGuid().ToString("N")[..10];
        var email = $"partial-{token}@example.invalid";
        var seed = await database.SeedRegistrationResidentAsync(email, currentOwnership: true);
        var request = new RegisterRequest(
            "partial_" + token,
            "Đối chiếu một phần",
            mismatch == "wrong-email" ? $"other-{token}@example.invalid" : seed.Email,
            Password,
            mismatch == "wrong-phone" ? "0399999999" : seed.PhoneNumber,
            mismatch == "wrong-identity-type" ? "CMND" : seed.IdentityType,
            mismatch == "wrong-identity-number" ? "064204019999" : seed.IdentityNumber);

        using var response = await Post(client, "register", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<ChallengeResponse>())!;
        Assert.Equal(Guid.Empty, body.ChallengeId);
        Assert.Equal("Thông tin cư dân không khớp hoặc chưa đủ điều kiện đăng ký tài khoản.", body.Message);
        Assert.Empty(database.Factory.Mail.Codes.Where(x => x.Key.Contains(token, StringComparison.OrdinalIgnoreCase)));
        using var scope = database.Factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>().UserAccounts.AnyAsync(x => x.Username == request.Username));
    }

    [Fact, Trait("UseCase", "FE-01.1")]
    public async Task Email_only_registration_is_rejected_by_the_public_contract()
    {
        using var client = Client();
        var username = "email_only_" + Guid.NewGuid().ToString("N")[..10];
        using var response = await Post(client, "register", new
        {
            username,
            displayName = "Chỉ có email",
            email = "email-only@example.test",
            password = Password
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = database.Factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>().UserAccounts.AnyAsync(x => x.Username == username));
    }

    [Theory, Trait("UseCase", "FE-01.1")]
    [InlineData("CCCD", "064204010557", "064 204-010.557")]
    [InlineData("CMND", "123456789", "123 456 789")]
    [InlineData("CMND", "123456789012", "123-456-789-012")]
    public async Task Registration_accepts_supported_identity_formats_and_normalized_email(
        string identityType,
        string storedIdentity,
        string submittedIdentity)
    {
        using var client = Client();
        var token = Guid.NewGuid().ToString("N")[..10];
        var email = $"format-{token}@example.invalid";
        var seed = await database.SeedRegistrationResidentAsync(email, identityType: identityType,
            identityNumber: storedIdentity, currentOwnership: true);
        var request = new RegisterRequest("format_" + token, "Định dạng hợp lệ", $"  {email.ToUpperInvariant()}  ",
            Password, seed.PhoneNumber, identityType, submittedIdentity);

        using var response = await Post(client, "register", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(Guid.Empty, (await response.Content.ReadFromJsonAsync<ChallengeResponse>())!.ChallengeId);
        Assert.True(database.Factory.Mail.Codes.ContainsKey(email));
    }

    [Fact, Trait("UseCase", "FE-01.1")]
    public async Task Eligible_registration_does_not_persist_user_account_before_otp_verification()
    {
        using var client = Client();
        var token = Guid.NewGuid().ToString("N")[..10];
        var email = $"deferred-account-{token}@example.invalid";
        var seed = await database.SeedRegistrationResidentAsync(email, currentOwnership: true);
        var username = "deferred_" + token;

        using var response = await Post(client, "register", new RegisterRequest(
            username, "Chờ xác minh OTP", email, Password,
            seed.PhoneNumber, seed.IdentityType, seed.IdentityNumber));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(Guid.Empty, (await response.Content.ReadFromJsonAsync<ChallengeResponse>())!.ChallengeId);
        using var scope = database.Factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>().UserAccounts
            .AnyAsync(x => x.Username == username || x.Email == email));
    }

    [Theory, Trait("UseCase", "FE-01.1")]
    [InlineData("CCCD", "12345678901", "Số CCCD phải gồm đúng 12 chữ số.")]
    [InlineData("CMND", "1234567890", "Số CMND phải gồm 9 hoặc 12 chữ số.")]
    public async Task Registration_contract_rejects_invalid_identity_length(string identityType, string identityNumber, string expected)
    {
        using var response = await Post(Client(), "register", new RegisterRequest(
            "invalid_" + Guid.NewGuid().ToString("N")[..10], "Sai định dạng", "invalid@example.test", Password,
            "0363602027", identityType, identityNumber));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var messages = problem.RootElement.GetProperty("errors").EnumerateObject()
            .SelectMany(property => property.Value.EnumerateArray().Select(value => value.GetString()));
        Assert.Contains(expected, messages);
    }

    [Theory, Trait("UseCase", "FE-01.3")]
    [InlineData("identity")]
    [InlineData("phone")]
    [InlineData("email")]
    public async Task Otp_activation_rejects_when_bound_match_data_changes_before_verification(string changedField)
    {
        using var client = Client();
        var token = Guid.NewGuid().ToString("N")[..10];
        var email = $"changed-{token}@example.invalid";
        var seed = await database.SeedRegistrationResidentAsync(email, currentOwnership: true);
        using var registration = await Post(client, "register", await RegistrationRequestAsync("changed_" + token, "Đổi giấy tờ", email));
        var challenge = (await registration.Content.ReadFromJsonAsync<ChallengeResponse>())!;
        var otp = database.Factory.Mail.Codes[email];

        await database.ChangeRegistrationMatchFieldAsync(seed, changedField);
        using var activation = await Post(client, "registration/verify", new VerifyChallengeRequest(challenge.ChallengeId, otp));

        await AssertConflictAsync(activation, "eligibility_changed", "Thông tin cư dân không khớp hoặc chưa đủ điều kiện đăng ký tài khoản.");
        using var scope = database.Factory.Services.CreateScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<ResidentsDbContext>().Residents
            .Where(x => x.Id == seed.ResidentId).Select(x => x.UserId).SingleAsync());
        Assert.False(await scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>().UserAccounts
            .AnyAsync(x => x.Username == "changed_" + token || x.Email == email.ToUpperInvariant()));
    }
    private async Task<(string Username, string Email)> ActivatedAccount(HttpClient client)
    {
        var username = "resident_" + Guid.NewGuid().ToString("N")[..12];
        var email = username + "@example.invalid";
        await database.SeedResidentAsync(email);
        var registration = await Post(client, "register", await RegistrationRequestAsync(username, "Cư dân kiểm thử", email));
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        var challenge = await registration.Content.ReadFromJsonAsync<ChallengeResponse>();
        Assert.NotEqual(Guid.Empty, challenge!.ChallengeId);
        var activate = await Post(client, "registration/verify", new VerifyChallengeRequest(challenge.ChallengeId, database.Factory.Mail.Codes[email]));
        Assert.Equal(HttpStatusCode.NoContent, activate.StatusCode);
        using (var scope = database.Factory.Services.CreateScope())
        {
            var userId = await scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>().UserAccounts.Where(x => x.Username == username).Select(x => x.Id).SingleAsync();
            var history = await scope.ServiceProvider.GetRequiredService<AdministrationDbContext>().UserAccessHistories.SingleAsync(x => x.TargetUserId == userId);
            Assert.Equal(PropFlow.Modules.Administration.Domain.UserAccessHistories.AccessActionType.ROLE_CHANGED, history.Action);
            Assert.NotNull(history.NewRoleId);
        }
        return (username, email);
    }

    private async Task<(string Username, SessionResponse Session)> RegisterActivateAndLoginAsync(HttpClient client, string email)
    {
        var username = "eligible_" + Guid.NewGuid().ToString("N")[..12];
        using var registration = await Post(client, "register", await RegistrationRequestAsync(username, "Cư dân đủ điều kiện", email));
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        var challenge = (await registration.Content.ReadFromJsonAsync<ChallengeResponse>())!;
        Assert.NotEqual(Guid.Empty, challenge.ChallengeId);
        Assert.True(database.Factory.Mail.Codes.TryGetValue(email, out var otp));
        using var activation = await Post(client, "registration/verify", new VerifyChallengeRequest(challenge.ChallengeId, otp!));
        Assert.Equal(HttpStatusCode.NoContent, activation.StatusCode);
        using var login = await Post(client, "login", new LoginRequest(username, Password), true);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var session = (await login.Content.ReadFromJsonAsync<SessionResponse>())!;
        Assert.Equal("RESIDENT", session.User.Role);
        using (var scope = database.Factory.Services.CreateScope())
        {
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>().UserAccounts
                .CountAsync(x => x.Username == username && x.Email == email.ToUpperInvariant()));
            Assert.Equal(session.User.Id, await scope.ServiceProvider.GetRequiredService<ResidentsDbContext>().Residents
                .Where(x => x.Email == email).Select(x => x.UserId).SingleAsync());
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AdministrationDbContext>().UserRoleAssignments
                .CountAsync(x => x.UserId == session.User.Id));
        }
        return (username, session);
    }

    private async Task<ChallengeResponse> RegisterForEligibilityAsync(HttpClient client, string email)
    {
        var username = "candidate_" + Guid.NewGuid().ToString("N")[..12];
        using var registration = await Post(client, "register", await RegistrationRequestAsync(username, "Ứng viên đăng ký", email));
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        return (await registration.Content.ReadFromJsonAsync<ChallengeResponse>())!;
    }

    [Fact, Trait("Feature", "FE-02")]
    public async Task Manager_can_list_residents_after_residency_migration()
    {
        await database.SeedResidentAsync($"manager-list-{Guid.NewGuid():N}@example.invalid");
        using var client = Client();
        var now = DateTimeOffset.UtcNow;
        var user = new UserAccount($"manager_{Guid.NewGuid():N}", "test-password-hash", "Manager", now,
            email: $"manager-{Guid.NewGuid():N}@example.invalid", status: AccountStatus.ACTIVE, emailVerified: true);
        using (var scope = database.Factory.Services.CreateScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
            var administration = scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
            auth.UserAccounts.Add(user);
            await auth.SaveChangesAsync();
            var managerRoleId = await administration.Roles.Where(role => role.Code == "MANAGER").Select(role => role.Id).SingleAsync();
            administration.UserRoleAssignments.Add(new PropFlow.Modules.Administration.Domain.UserRoleAssignments.UserRoleAssignment(user.Id, managerRoleId, now));
            await administration.SaveChangesAsync();
        }
        var manager = new AccountResponse(user.Id, user.Username, user.DisplayName, user.Email, null, "ACTIVE", true,
            "MANAGER", [SystemPermissionCodes.ManageOperations]);
        var token = database.Factory.Services.GetRequiredService<IAuthSecrets>().Issue(manager, DateTimeOffset.UtcNow).AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("api/v1/residents?pageIndex=1&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact, Trait("UseCase", "FE-01.1")]
    public async Task Registration_keeps_account_conflicts_but_does_not_treat_phone_as_globally_unique()
    {
        using var client = Client();
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var username = "resident_" + suffix;
        var email = username + "@example.invalid";
        var seed = await database.SeedRegistrationResidentAsync(email, currentOwnership: true);

        var created = await Post(client, "register", await RegistrationRequestAsync(username, "Cư dân", email));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        await AssertConflictAsync(
            await Post(client, "register", await RegistrationRequestAsync(username, "Tên khác", email)),
            "username_conflict", "Tên đăng nhập đã được sử dụng.");
        await AssertConflictAsync(
            await Post(client, "register", await RegistrationRequestAsync("other_" + suffix, "Tên khác", email)),
            "email_conflict", "Email đã được sử dụng.");

        var secondEmail = $"shared-phone-{suffix}@example.invalid";
        var second = await database.SeedRegistrationResidentAsync(secondEmail, seed.PhoneNumber,
            identityNumber: "064204010556", currentOwnership: true);
        using var sharedPhone = await Post(client, "register", await RegistrationRequestAsync("phone_" + suffix, "Cùng số điện thoại", secondEmail));
        Assert.Equal(HttpStatusCode.OK, sharedPhone.StatusCode);
        Assert.NotEqual(Guid.Empty, (await sharedPhone.Content.ReadFromJsonAsync<ChallengeResponse>())!.ChallengeId);
    }

    private static async Task AssertConflictAsync(HttpResponseMessage response, string code, string title)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
            Assert.Equal(title, problem.RootElement.GetProperty("title").GetString());
        }
    }

    [Fact, Trait("UseCase", "FE-01.1/FE-01.2/FE-01.3/FE-01.6/FE-01.7")]
    public async Task Resident_can_activate_login_rotate_restore_update_profile_and_logout()
    {
        using var client = Client();
        var account = await ActivatedAccount(client);
        var login = await Post(client, "login", new LoginRequest(account.Username, Password), true);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookies = string.Join(";", login.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookies, StringComparison.OrdinalIgnoreCase); Assert.Contains("secure", cookies, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookies, StringComparison.OrdinalIgnoreCase);
        var session = (await login.Content.ReadFromJsonAsync<SessionResponse>())!;
        Assert.Equal("RESIDENT", session.User.Role);
        Assert.DoesNotContain("refreshToken", await login.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        Assert.Equal(session.User.Id, (await client.GetFromJsonAsync<AccountResponse>("api/v1/auth/me"))!.Id);
        var update = await client.PutAsJsonAsync("api/v1/auth/me", new UpdateProfileRequest("Tên mới", null));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        // Browser reload: access token is gone; cookie alone must bootstrap a new session.
        client.DefaultRequestHeaders.Authorization = null;
        var refresh = await Post(client, "refresh", new { }, true);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.NotEqual(session.AccessToken, (await refresh.Content.ReadFromJsonAsync<SessionResponse>())!.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(client, "logout", new { }, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "refresh", new { }, true)).StatusCode);
    }

    [Fact, Trait("UseCase", "FE-01.1/FE-01.2/FE-01.3")]
    public async Task Owner_only_can_register_activate_login_and_read_own_profile_without_creating_residency()
    {
        using var client = Client();
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var username = "owner_" + suffix;
        var email = username + "@example.invalid";
        var seed = await database.SeedOwnerOnlyResidentAsync(email);

        using var registration = await Post(client, "register", await RegistrationRequestAsync(username, "Chủ sở hữu", email));
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        var challenge = (await registration.Content.ReadFromJsonAsync<ChallengeResponse>())!;
        Assert.NotEqual(Guid.Empty, challenge.ChallengeId);
        Assert.True(database.Factory.Mail.Codes.TryGetValue(email, out var otp));

        using var activation = await Post(client, "registration/verify", new VerifyChallengeRequest(challenge.ChallengeId, otp!));
        Assert.Equal(HttpStatusCode.NoContent, activation.StatusCode);

        using var login = await Post(client, "login", new LoginRequest(username, Password), true);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var session = (await login.Content.ReadFromJsonAsync<SessionResponse>())!;
        Assert.Equal("RESIDENT", session.User.Role);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using var self = await client.GetAsync("api/v1/residents/me");
        Assert.Equal(HttpStatusCode.OK, self.StatusCode);
        using var profile = JsonDocument.Parse(await self.Content.ReadAsStringAsync());
        Assert.Equal(seed.ResidentId, profile.RootElement.GetProperty("id").GetGuid());
        Assert.Empty(profile.RootElement.GetProperty("residencies").EnumerateArray());
        var ownership = Assert.Single(profile.RootElement.GetProperty("ownerships").EnumerateArray());
        Assert.Equal(seed.ApartmentId, ownership.GetProperty("apartmentUnitId").GetGuid());
        Assert.Equal(JsonValueKind.Null, ownership.GetProperty("endDate").ValueKind);
    }

    [Fact, Trait("UseCase", "FE-01.1/FE-01.2/FE-01.3")]
    public async Task Resident_only_and_owner_resident_can_still_register_activate_and_login()
    {
        using var client = Client();
        var residentOnlyEmail = $"resident-only-{Guid.NewGuid():N}@example.invalid";
        await database.SeedRegistrationResidentAsync(residentOnlyEmail, activeResidency: true);
        var residentOnly = await RegisterActivateAndLoginAsync(client, residentOnlyEmail);
        Assert.NotEqual(Guid.Empty, residentOnly.Session.User.Id);

        var bothEmail = $"owner-resident-{Guid.NewGuid():N}@example.invalid";
        await database.SeedRegistrationResidentAsync(bothEmail, currentOwnership: true, activeResidency: true);
        var both = await RegisterActivateAndLoginAsync(client, bothEmail);
        Assert.NotEqual(residentOnly.Session.User.Id, both.Session.User.Id);
    }

    [Fact, Trait("UseCase", "FE-01.1")]
    public async Task Registration_uses_any_current_relationship_but_ignores_ended_history()
    {
        using var client = Client();

        var endedOwnershipEmail = $"ended-owner-{Guid.NewGuid():N}@example.invalid";
        await database.SeedRegistrationResidentAsync(endedOwnershipEmail, endedOwnership: true);
        var historicalOnly = await RegisterForEligibilityAsync(client, endedOwnershipEmail);
        Assert.Equal(Guid.Empty, historicalOnly.ChallengeId);
        Assert.Equal("Thông tin cư dân không khớp hoặc chưa đủ điều kiện đăng ký tài khoản.", historicalOnly.Message);
        Assert.False(database.Factory.Mail.Codes.ContainsKey(endedOwnershipEmail));

        var residencyWinsEmail = $"residency-wins-{Guid.NewGuid():N}@example.invalid";
        await database.SeedRegistrationResidentAsync(residencyWinsEmail, endedOwnership: true, activeResidency: true);
        Assert.NotEqual(Guid.Empty, (await RegisterForEligibilityAsync(client, residencyWinsEmail)).ChallengeId);

        var ownershipWinsEmail = $"ownership-wins-{Guid.NewGuid():N}@example.invalid";
        await database.SeedRegistrationResidentAsync(ownershipWinsEmail, currentOwnership: true, endedResidency: true);
        Assert.NotEqual(Guid.Empty, (await RegisterForEligibilityAsync(client, ownershipWinsEmail)).ChallengeId);

        var noRelationshipEmail = $"no-relationship-{Guid.NewGuid():N}@example.invalid";
        await database.SeedRegistrationResidentAsync(noRelationshipEmail);
        Assert.Equal(Guid.Empty, (await RegisterForEligibilityAsync(client, noRelationshipEmail)).ChallengeId);
        Assert.False(database.Factory.Mail.Codes.ContainsKey(noRelationshipEmail));
    }

    [Fact, Trait("UseCase", "FE-01.1/FE-01.3")]
    public async Task Inactive_linked_residents_cannot_be_claimed_and_duplicate_email_is_rejected()
    {
        using var client = Client();

        foreach (var hasResidency in new[] { false, true })
        {
            var inactiveEmail = $"inactive-{hasResidency}-{Guid.NewGuid():N}@example.invalid";
            await database.SeedRegistrationResidentAsync(inactiveEmail,
                currentOwnership: !hasResidency,
                activeResidency: hasResidency,
                inactive: true);
            var inactive = await RegisterForEligibilityAsync(client, inactiveEmail);
            Assert.Equal(Guid.Empty, inactive.ChallengeId);
            Assert.Equal("Thông tin cư dân không khớp hoặc chưa đủ điều kiện đăng ký tài khoản.", inactive.Message);
        }

        var linkedEmail = $"linked-resident-{Guid.NewGuid():N}@example.invalid";
        await database.SeedRegistrationResidentAsync(linkedEmail, currentOwnership: true, activeResidency: true, alreadyLinked: true);
        var linked = await RegisterForEligibilityAsync(client, linkedEmail);
        Assert.Equal(Guid.Empty, linked.ChallengeId);
        Assert.Equal("Thông tin cư dân không khớp hoặc chưa đủ điều kiện đăng ký tài khoản.", linked.Message);

        var ambiguousEmail = $"ambiguous-{Guid.NewGuid():N}@example.invalid";
        await database.SeedRegistrationResidentAsync(ambiguousEmail.ToLowerInvariant(), currentOwnership: true);
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() =>
            database.SeedRegistrationResidentAsync(ambiguousEmail.ToUpperInvariant(), activeResidency: true));
        var postgres = Assert.IsType<PostgresException>(duplicate.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        Assert.Equal("ux_residents_email_canonical", postgres.ConstraintName);
    }

    [Fact, Trait("UseCase", "FE-01.3")]
    public async Task Otp_activation_revalidates_current_ownership()
    {
        using var client = Client();
        var username = "revalidate_" + Guid.NewGuid().ToString("N")[..12];
        var email = username + "@example.invalid";
        var seed = await database.SeedRegistrationResidentAsync(email, currentOwnership: true);
        using var registration = await Post(client, "register", await RegistrationRequestAsync(username, "Revalidate owner", email));
        var challenge = (await registration.Content.ReadFromJsonAsync<ChallengeResponse>())!;
        Assert.NotEqual(Guid.Empty, challenge.ChallengeId);
        var otp = database.Factory.Mail.Codes[email];

        await database.EndOwnershipAsync(seed);

        using var activation = await Post(client, "registration/verify", new VerifyChallengeRequest(challenge.ChallengeId, otp));
        await AssertConflictAsync(activation, "eligibility_changed", "Thông tin cư dân không khớp hoặc chưa đủ điều kiện đăng ký tài khoản.");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "login", new LoginRequest(username, Password), true)).StatusCode);
        using var scope = database.Factory.Services.CreateScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<ResidentsDbContext>().Residents
            .Where(x => x.Id == seed.ResidentId).Select(x => x.UserId).SingleAsync());
        Assert.False(await scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>().UserAccounts
            .AnyAsync(x => x.Username == username || x.Email == email.ToUpperInvariant()));
    }

    [Fact, Trait("UseCase", "FE-01.1/FE-01.2/FE-01.3")]
    public async Task Multiple_owners_of_one_apartment_can_register_independent_accounts()
    {
        using var firstClient = Client();
        using var secondClient = Client();
        var firstEmail = $"multi-owner-a-{Guid.NewGuid():N}@example.invalid";
        var firstSeed = await database.SeedRegistrationResidentAsync(firstEmail, currentOwnership: true);
        var secondEmail = $"multi-owner-b-{Guid.NewGuid():N}@example.invalid";
        var secondSeed = await database.SeedRegistrationResidentAsync(secondEmail, currentOwnership: true, apartmentId: firstSeed.ApartmentId);

        var first = await RegisterActivateAndLoginAsync(firstClient, firstEmail);
        var second = await RegisterActivateAndLoginAsync(secondClient, secondEmail);

        Assert.NotEqual(first.Session.User.Id, second.Session.User.Id);
        using var scope = database.Factory.Services.CreateScope();
        var linkedResidents = await scope.ServiceProvider.GetRequiredService<ResidentsDbContext>().Residents.AsNoTracking()
            .Where(x => x.Id == firstSeed.ResidentId || x.Id == secondSeed.ResidentId)
            .Select(x => new { x.Id, x.UserId })
            .ToArrayAsync();
        Assert.Equal(2, linkedResidents.Length);
        Assert.All(linkedResidents, row => Assert.NotNull(row.UserId));
        Assert.Equal(2, linkedResidents.Select(row => row.UserId).Distinct().Count());
    }

    [Fact, Trait("UseCase", "FE-01.4/FE-01.5")]
    public async Task Recovery_proof_is_single_use_and_password_changes_revoke_sessions()
    {
        using var client = Client(); var account = await ActivatedAccount(client);
        Assert.Equal(HttpStatusCode.OK, (await Post(client, "login", new LoginRequest(account.Username, Password), true)).StatusCode);
        var forgot = await Post(client, "password/forgot", new ForgotPasswordRequest(account.Email));
        var challenge = (await forgot.Content.ReadFromJsonAsync<ChallengeResponse>())!;
        var verify = await Post(client, "password/verify", new VerifyChallengeRequest(challenge.ChallengeId, database.Factory.Mail.Codes[account.Email]));
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var proof = (await verify.Content.ReadFromJsonAsync<ResetProofResponse>())!;
        var newPassword = "New secure passphrase 456!";
        var resetRequest = new ResetPasswordRequest(proof.ChallengeId, proof.Proof, newPassword);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(client, "password/reset", resetRequest, true)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "password/reset", resetRequest, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "refresh", new { }, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "login", new LoginRequest(account.Username, Password), true)).StatusCode);
        var loggedIn = await Post(client, "login", new LoginRequest(account.Username, newPassword), true);
        var session = (await loggedIn.Content.ReadFromJsonAsync<SessionResponse>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(client, "password/change", new ChangePasswordRequest(newPassword, Password), true)).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "refresh", new { }, true)).StatusCode);
    }

    [Fact, Trait("UseCase", "FE-01.1/FE-01.3")]
    public async Task Missing_eligibility_cannot_activate_and_wrong_OTP_attempts_are_persisted()
    {
        using var client = Client();
        var username = "pending_" + Guid.NewGuid().ToString("N")[..12]; var email = username + "@example.invalid";
        var pending = await Post(client, "register", new RegisterRequest(username, "Chờ xác minh", email, Password, "0363602027", "CCCD", "064204010555"));
        Assert.Equal(Guid.Empty, (await pending.Content.ReadFromJsonAsync<ChallengeResponse>())!.ChallengeId);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "login", new LoginRequest(username, Password), true)).StatusCode);
        using (var scope = database.Factory.Services.CreateScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>().UserAccounts.AnyAsync(x => x.Username == username));
        await database.SeedResidentAsync(email);
        var registered = await Post(client, "register", await RegistrationRequestAsync(username, "Đã đủ điều kiện", email));
        var challenge = (await registered.Content.ReadFromJsonAsync<ChallengeResponse>())!;
        var correct = database.Factory.Mail.Codes[email]; var wrong = correct == "000000" ? "111111" : "000000";
        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "registration/verify", new VerifyChallengeRequest(challenge.ChallengeId, wrong))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "registration/verify", new VerifyChallengeRequest(challenge.ChallengeId, correct))).StatusCode);
        using var failedScope = database.Factory.Services.CreateScope();
        Assert.False(await failedScope.ServiceProvider.GetRequiredService<AuthenticationDbContext>().UserAccounts
            .AnyAsync(x => x.Username == username || x.Email == email.ToUpperInvariant()));
    }

    [Fact, Trait("UseCase", "FE-01.1/FE-01.3")]
    public async Task Failed_email_delivery_releases_registration_email_for_retry()
    {
        using var client = Client();
        var token = Guid.NewGuid().ToString("N")[..10];
        var username = "mail_retry_" + token;
        var email = $"mail-retry-{token}@example.invalid";
        await database.SeedResidentAsync(email);
        var request = await RegistrationRequestAsync(username, "Thử lại gửi mail", email);
        database.Factory.Mail.FailNext = true;
        using var failed = await Post(client, "register", request);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        try
        {
            database.Factory.Clock.Offset = TimeSpan.FromSeconds(61);
            using var retry = await Post(client, "register", request);
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            var next = (await retry.Content.ReadFromJsonAsync<ChallengeResponse>())!;
            Assert.NotEqual(Guid.Empty, next.ChallengeId);
            using var activation = await Post(client, "registration/verify", new VerifyChallengeRequest(next.ChallengeId, database.Factory.Mail.Codes[email]));
            Assert.Equal(HttpStatusCode.NoContent, activation.StatusCode);
        }
        finally { database.Factory.Mail.FailNext = false; database.Factory.Clock.Offset = TimeSpan.Zero; }
    }

    [Fact, Trait("UseCase", "FE-01.1/FE-01.3")]
    public async Task Failed_eligibility_verification_releases_registration_email_for_corrected_retry()
    {
        using var client = Client();
        var token = Guid.NewGuid().ToString("N")[..10];
        var username = "elig_retry_" + token;
        var email = $"elig-retry-{token}@example.invalid";
        var seed = await database.SeedResidentAsync(email);
        using var registration = await Post(client, "register", await RegistrationRequestAsync(username, "Thử lại hồ sơ", email));
        var challenge = (await registration.Content.ReadFromJsonAsync<ChallengeResponse>())!;
        await database.ChangeRegistrationMatchFieldAsync(seed, "phone");
        using var failed = await Post(client, "registration/verify", new VerifyChallengeRequest(challenge.ChallengeId, database.Factory.Mail.Codes[email]));
        Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);
        try
        {
            database.Factory.Clock.Offset = TimeSpan.FromSeconds(61);
            using var retry = await Post(client, "register", await RegistrationRequestAsync(username, "Thử lại hồ sơ", email));
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            var next = (await retry.Content.ReadFromJsonAsync<ChallengeResponse>())!;
            using var activation = await Post(client, "registration/verify", new VerifyChallengeRequest(next.ChallengeId, database.Factory.Mail.Codes[email]));
            Assert.Equal(HttpStatusCode.NoContent, activation.StatusCode);
        }
        finally { database.Factory.Clock.Offset = TimeSpan.Zero; }
    }

    [Fact, Trait("UseCase", "FE-01.1/FE-01.3")]
    public async Task Cancelled_registration_can_retry_without_orphan_email_conflict()
    {
        using var client = Client();
        var token = Guid.NewGuid().ToString("N")[..10];
        var username = "retry_" + token;
        var email = $"retry-{token}@example.invalid";
        await database.SeedRegistrationResidentAsync(email, currentOwnership: true);
        using var firstResponse = await Post(client, "register", await RegistrationRequestAsync(username, "Thử lại", email));
        var first = (await firstResponse.Content.ReadFromJsonAsync<ChallengeResponse>())!;
        var correct = database.Factory.Mail.Codes[email];
        var wrong = correct == "000000" ? "111111" : "000000";
        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Equal(HttpStatusCode.BadRequest,
                (await Post(client, "registration/verify", new VerifyChallengeRequest(first.ChallengeId, wrong))).StatusCode);

        try
        {
            database.Factory.Clock.Offset = TimeSpan.FromSeconds(61);
            using var retry = await Post(client, "register", await RegistrationRequestAsync(username, "Thử lại", email));
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            Assert.NotEqual(Guid.Empty, (await retry.Content.ReadFromJsonAsync<ChallengeResponse>())!.ChallengeId);
            using var scope = database.Factory.Services.CreateScope();
            Assert.False(await scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>().UserAccounts
                .AnyAsync(x => x.Username == username || x.Email == email.ToUpperInvariant()));
        }
        finally { database.Factory.Clock.Offset = TimeSpan.Zero; }
    }

    [Fact, Trait("UseCase", "FE-01.3")]
    public async Task Concurrent_registration_finalization_creates_at_most_one_account()
    {
        using var registerClient = Client();
        using var firstClient = Client();
        using var secondClient = Client();
        var token = Guid.NewGuid().ToString("N")[..10];
        var username = "finalize_" + token;
        var email = $"finalize-{token}@example.invalid";
        var seed = await database.SeedRegistrationResidentAsync(email, currentOwnership: true);
        using var registration = await Post(registerClient, "register", await RegistrationRequestAsync(username, "Finalize once", email));
        var challenge = (await registration.Content.ReadFromJsonAsync<ChallengeResponse>())!;
        var otp = database.Factory.Mail.Codes[email];

        var responses = await Task.WhenAll(
            Post(firstClient, "registration/verify", new VerifyChallengeRequest(challenge.ChallengeId, otp)),
            Post(secondClient, "registration/verify", new VerifyChallengeRequest(challenge.ChallengeId, otp)));

        Assert.Single(responses.Where(x => x.StatusCode == HttpStatusCode.NoContent));
        Assert.Single(responses.Where(x => x.StatusCode == HttpStatusCode.BadRequest));
        foreach (var response in responses) response.Dispose();
        using var scope = database.Factory.Services.CreateScope();
        var user = Assert.Single(await scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>().UserAccounts
            .Where(x => x.Username == username || x.Email == email.ToUpperInvariant()).ToArrayAsync());
        Assert.Equal(user.Id, await scope.ServiceProvider.GetRequiredService<ResidentsDbContext>().Residents
            .Where(x => x.Id == seed.ResidentId).Select(x => x.UserId).SingleAsync());
    }

    [Fact, Trait("UseCase", "FE-01.2/FE-01.4/FE-01.7")]
    public async Task Cookie_operations_require_csrf_and_unknown_recovery_is_neutral()
    {
        using var client = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "refresh", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "logout", new { })).StatusCode);
        var response = await Post(client, "password/forgot", new ForgotPasswordRequest("absent@example.invalid"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var challenge = (await response.Content.ReadFromJsonAsync<ChallengeResponse>())!;
        Assert.NotEqual(Guid.Empty, challenge.ChallengeId);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "password/verify", new VerifyChallengeRequest(challenge.ChallengeId, "123456"))).StatusCode);
    }

    [Fact, Trait("UseCase", "FE-01.2/FE-01.7")]
    public async Task Rotation_rejects_replay_and_logout_preserves_other_devices()
    {
        using var client = Client(); var account = await ActivatedAccount(client);
        var login = await Post(client, "login", new LoginRequest(account.Username, Password), true);
        var oldCookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Secure-PropFlow.Refresh=")).Split(';')[0];
        using var device = Client();
        Assert.Equal(HttpStatusCode.OK, (await Post(device, "login", new LoginRequest(account.Username, Password), true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(client, "refresh", new { }, true)).StatusCode);
        using var replay = database.Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost"), HandleCookies = false });
        var csrf = await replay.GetAsync("api/v1/auth/csrf");
        var token = (await csrf.Content.ReadFromJsonAsync<CsrfResponse>())!.Token;
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/refresh");
        request.Headers.Add("Cookie", oldCookie + "; " + csrf.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Secure-PropFlow.Csrf=")).Split(';')[0]);
        request.Headers.Add("X-CSRF-TOKEN", token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.SendAsync(request)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(client, "logout", new { }, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "refresh", new { }, true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(device, "refresh", new { }, true)).StatusCode);
    }

    [Fact, Trait("UseCase", "FE-01.3")]
    public async Task Resend_is_throttled_cancels_old_challenge_and_expiry_is_enforced()
    {
        using var client = Client();
        var username = "expiry_" + Guid.NewGuid().ToString("N")[..12]; var email = username + "@example.invalid";
        await database.SeedResidentAsync(email);
        var registered = await Post(client, "register", await RegistrationRequestAsync(username, "Kiểm thử hết hạn", email));
        var first = (await registered.Content.ReadFromJsonAsync<ChallengeResponse>())!;
        var firstCode = database.Factory.Mail.Codes[email];
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Post(client, "registration/resend", new ResumeRegistrationRequest(username, Password))).StatusCode);
        try
        {
            database.Factory.Clock.Offset = TimeSpan.FromSeconds(61);
            var resent = await Post(client, "registration/resend", new ResumeRegistrationRequest(username, Password));
            Assert.Equal(HttpStatusCode.OK, resent.StatusCode);
            var second = (await resent.Content.ReadFromJsonAsync<ChallengeResponse>())!;
            Assert.NotEqual(first.ChallengeId, second.ChallengeId);
            Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "registration/verify", new VerifyChallengeRequest(first.ChallengeId, firstCode))).StatusCode);
            database.Factory.Clock.Offset = TimeSpan.FromMinutes(7);
            Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "registration/verify", new VerifyChallengeRequest(second.ChallengeId, database.Factory.Mail.Codes[email]))).StatusCode);
            using var scope = database.Factory.Services.CreateScope();
            Assert.False(await scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>().UserAccounts
                .AnyAsync(x => x.Username == username || x.Email == email.ToUpperInvariant()));
        }
        finally { database.Factory.Clock.Offset = TimeSpan.Zero; }
    }

    [Fact, Trait("UseCase", "FE-01.2/FE-01.6")]
    public async Task Lockout_is_enforced_and_profile_payload_cannot_escalate_role()
    {
        using var client = Client(); var account = await ActivatedAccount(client);
        var login = await Post(client, "login", new LoginRequest(account.Username, Password), true);
        var session = (await login.Content.ReadFromJsonAsync<SessionResponse>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        var update = await client.PutAsJsonAsync("api/v1/auth/me", new { displayName = "Tên hợp lệ", role = "ADMIN", status = "ACTIVE", email = "other@example.invalid", residentId = Guid.NewGuid(), apartmentUnitId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var profile = (await update.Content.ReadFromJsonAsync<AccountResponse>())!;
        Assert.Equal("RESIDENT", profile.Role); Assert.Equal(account.Email.ToUpperInvariant(), profile.Email);
        client.DefaultRequestHeaders.Authorization = null;
        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "login", new LoginRequest(account.Username, "incorrect password"), true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "login", new LoginRequest(account.Username, Password), true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "refresh", new { }, true)).StatusCode);
    }

    [Fact, Trait("UseCase", "FE-01.2/FE-01.6")]
    public async Task Expired_bearer_and_stale_account_access_are_401()
    {
        using var client = Client(); var account = await ActivatedAccount(client);
        var login = await Post(client, "login", new LoginRequest(account.Username, Password), true);
        var session = (await login.Content.ReadFromJsonAsync<SessionResponse>())!;
        using (var scope = database.Factory.Services.CreateScope())
        {
            var expired = scope.ServiceProvider.GetRequiredService<IAuthSecrets>().Issue(session.User, DateTimeOffset.UtcNow.AddHours(-1));
            client.DefaultRequestHeaders.Authorization = new("Bearer", expired.AccessToken);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("api/v1/auth/me")).StatusCode);
        var refreshed = await Post(client, "refresh", new { }, true);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await refreshed.Content.ReadFromJsonAsync<SessionResponse>())!.AccessToken);
        using (var scope = database.Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AdministrationDbContext>().UserRoleAssignments.Where(x => x.UserId == session.User.Id).ExecuteDeleteAsync();
        // Access tokens are validated against the current account, role and effective
        // permissions. Removing the role assignment makes this bearer token stale,
        // so it is no longer accepted as an authenticated identity.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("api/v1/auth/me")).StatusCode);
    }

    [Fact, Trait("UseCase", "FE-01.2")]
    public async Task Credentialed_CORS_allows_only_configured_frontend_origin()
    {
        using var client = Client();
        using var accepted = new HttpRequestMessage(HttpMethod.Options, "api/v1/auth/refresh");
        accepted.Headers.Add("Origin", "https://localhost:7201");
        accepted.Headers.Add("Access-Control-Request-Method", "POST");
        accepted.Headers.Add("Access-Control-Request-Headers", "content-type,x-csrf-token");
        using var acceptedResponse = await client.SendAsync(accepted);
        Assert.Equal(HttpStatusCode.NoContent, acceptedResponse.StatusCode);
        Assert.Equal("https://localhost:7201", acceptedResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", acceptedResponse.Headers.GetValues("Access-Control-Allow-Credentials").Single());

        using var rejected = new HttpRequestMessage(HttpMethod.Options, "api/v1/auth/refresh");
        rejected.Headers.Add("Origin", "https://untrusted.example.invalid");
        rejected.Headers.Add("Access-Control-Request-Method", "POST");
        using var rejectedResponse = await client.SendAsync(rejected);
        Assert.False(rejectedResponse.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(rejectedResponse.Headers.Contains("Access-Control-Allow-Credentials"));
    }
}
