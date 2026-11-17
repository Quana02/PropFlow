using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

internal sealed class DatabaseMigration(string connection)
{
    internal DbContext Open(string module, string typeName, string schema)
    {
        var type = Assembly.Load("PropFlow.Modules." + module).GetTypes().Single(candidate => candidate.Name == typeName);
        var options = (DbContextOptionsBuilder)Activator.CreateInstance(typeof(DbContextOptionsBuilder<>).MakeGenericType(type))!;
        options.UseNpgsql(connection, provider => provider.MigrationsHistoryTable("__EFMigrationsHistory", schema));
        return (DbContext)Activator.CreateInstance(type, options.Options)!;
    }

    internal async Task RunAsync(bool apply)
    {
        await TestDatabaseSafety.VerifyTargetAsync(connection, TestDatabaseSafety.Parse(connection).Database!);
        await using var auth = Open("Authentication", "AuthenticationDbContext", "auth");
        await using var residents = Open("Residents", "ResidentsDbContext", "residents");

        if (apply)
            await EnsureCheckpointAsync(auth, "20260914101250_CorrectAuthenticationEmailOtpVerification");
        await RunModuleAsync("PropertyAssets", "property_assets");
        await RunModuleAsync("Apartments", "apartments");
        if (apply)
            await EnsureCheckpointAsync(residents, "20260914063807_InitialResidents");
        await RunModuleAsync("Administration", "administration");
        await RunContextAsync(auth, "auth", apply);
        await RunContextAsync(residents, "residents", apply);
        foreach (var (module, schema) in new[]
        {
            ("ServiceRequests", "service_requests"), ("Maintenance", "maintenance"), ("Complaints", "complaints"),
            ("Billing", "billing"), ("Payments", "payments"), ("AiClassification", "ai_classification"),
            ("AiRecommendation", "ai_recommendation"), ("AiChatbot", "ai_chatbot"), ("Communication", "communication")
        })
            await RunModuleAsync(module, schema);

        async Task RunModuleAsync(string module, string schema)
        {
            await using var context = Open(module, module + "DbContext", schema);
            await RunContextAsync(context, schema, apply);
        }
    }

    private static async Task EnsureCheckpointAsync(DbContext context, string checkpoint)
    {
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        if (applied.Contains(checkpoint, StringComparer.Ordinal)) return;
        if (applied.Any(existing => StringComparer.Ordinal.Compare(existing, checkpoint) > 0))
            throw new InvalidOperationException("Migration history is missing an earlier prerequisite.");
        Console.WriteLine($"{context.GetType().Name} prerequisite {checkpoint}");
        await context.GetService<IMigrator>().MigrateAsync(checkpoint);
    }

    private static async Task RunContextAsync(DbContext context, string schema, bool apply)
    {
        var pending = (await context.Database.GetPendingMigrationsAsync()).ToArray();
        Console.WriteLine($"{context.GetType().Name} [{schema}] pending={pending.Length}");
        foreach (var migration in pending) Console.WriteLine($"  {migration}");
        if (apply && pending.Length > 0)
            await context.Database.MigrateAsync();
    }
}
