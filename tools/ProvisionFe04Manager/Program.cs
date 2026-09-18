using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Npgsql;
using PropFlow.Modules.Administration.Domain.Roles;
using PropFlow.Modules.Administration.Domain.UserBuildingAccesses;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;

const string username = "fe04.manager";

var configuration = new ConfigurationBuilder()
    .AddUserSecrets("adbac45b-651b-498f-a797-12585846625f")
    .AddEnvironmentVariables()
    .Build();
var connectionString = configuration.GetConnectionString("PropFlowDatabase")
    ?? throw new InvalidOperationException("Configure ConnectionStrings:PropFlowDatabase in API user secrets or environment.");
var connectionSettings = new NpgsqlConnectionStringBuilder(connectionString);
var targetDatabase = connectionSettings.Database;
if (targetDatabase != "propflow" ||
    !new[] { "localhost", "127.0.0.1", "::1" }.Contains(connectionSettings.Host, StringComparer.OrdinalIgnoreCase))
    throw new InvalidOperationException("Provisioning is allowed only in the local propflow database.");

await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();
var authOptions = new DbContextOptionsBuilder<AuthenticationDbContext>().UseNpgsql(connection).Options;
var adminOptions = new DbContextOptionsBuilder<AdministrationDbContext>().UseNpgsql(connection).Options;
var assetOptions = new DbContextOptionsBuilder<PropertyAssetsDbContext>().UseNpgsql(connection).Options;
await using var auth = new AuthenticationDbContext(authOptions);
await using var admin = new AdministrationDbContext(adminOptions);
await using var assets = new PropertyAssetsDbContext(assetOptions);

if (await auth.UserAccounts.AnyAsync(x => x.Username == username))
    throw new InvalidOperationException("The FE-04 manager account already exists. No account was changed.");
var buildingIds = await assets.Buildings.AsNoTracking().Select(x => x.Id).ToArrayAsync();
if (buildingIds.Length != 1)
    throw new InvalidOperationException($"Expected exactly one building, found {buildingIds.Length}. Select a building explicitly before provisioning.");
var roleId = await admin.Roles.AsNoTracking().Where(x => x.Code == SystemRoleCodes.Manager && x.IsActive)
    .Select(x => x.Id).SingleAsync();

var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
var now = DateTimeOffset.UtcNow;
var user = new UserAccount(username, "not-persisted", "FE-04 Manager", now,
    status: AccountStatus.ACTIVE, emailVerified: true);
user.ChangePasswordHash(new PasswordHasher<UserAccount>().HashPassword(user, password), null, now);

await using var transaction = await auth.Database.BeginTransactionAsync();
await admin.Database.UseTransactionAsync(transaction.GetDbTransaction());
auth.UserAccounts.Add(user);
await auth.SaveChangesAsync();
admin.UserRoleAssignments.Add(new UserRoleAssignment(user.Id, roleId, now));
admin.UserBuildingAccesses.Add(new UserBuildingAccess(user.Id, buildingIds[0], now,
    reason: "Local FE-04 evaluation account"));
await admin.SaveChangesAsync();
await transaction.CommitAsync();

Console.WriteLine($"Database: {targetDatabase}");
Console.WriteLine($"BuildingId: {buildingIds[0]}");
Console.WriteLine($"Username: {username}");
Console.WriteLine($"Password: {password}");
