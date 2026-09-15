using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PropFlow.Modules.AiClassification.Infrastructure.Persistence;
using PropFlow.Modules.AiRecommendation.Infrastructure.Persistence;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Billing.Infrastructure.Persistence;
using PropFlow.Modules.Complaints.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public class DatabaseVerificationTests : IClassFixture<PropFlowApiFactory>
{
    private readonly PropFlowApiFactory _factory;
    private readonly string _connectionString;

    public DatabaseVerificationTests(PropFlowApiFactory factory)
    {
        _factory = factory;
        _connectionString = factory.GetConnectionString();
    }

    [Fact]
    public async Task Database_ShouldUsePropFlowTestDatabase()
    {
        // Bắt buộc: Integration test PHẢI kết nối chính xác vào database 'propflow_test'.
        // Test này sẽ fail ngay lập tức nếu vô tình kết nối vào 'propflow' hoặc database khác.
        var options = new DbContextOptionsBuilder<PropertyAssetsDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new PropertyAssetsDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT current_database();";
        var currentDb = (string?)await cmd.ExecuteScalarAsync();

        Assert.Equal("propflow_test", currentDb);
    }

    [Fact]
    public async Task Database_ShouldContainExpectedImplementedSchemas()
    {
        var options = new DbContextOptionsBuilder<PropertyAssetsDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new PropertyAssetsDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT schema_name 
            FROM information_schema.schemata 
            WHERE schema_name IN ('property_assets', 'apartments', 'auth', 'residents', 'administration', 'service_requests', 'complaints', 'maintenance', 'billing', 'payments', 'ai_classification', 'ai_recommendation', 'communication');";

        var schemas = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            schemas.Add(reader.GetString(0));
        }

        Assert.Equal(13, schemas.Count);
        Assert.Contains("property_assets", schemas);
        Assert.Contains("apartments", schemas);
        Assert.Contains("auth", schemas);
        Assert.Contains("residents", schemas);
        Assert.Contains("administration", schemas);
        Assert.Contains("service_requests", schemas);
        Assert.Contains("complaints", schemas);
        Assert.Contains("maintenance", schemas);
        Assert.Contains("billing", schemas);
        Assert.Contains("payments", schemas);
        Assert.Contains("ai_classification", schemas);
        Assert.Contains("ai_recommendation", schemas);
        Assert.Contains("communication", schemas);
    }

    [Fact]
    public async Task Database_ShouldContainExactly45BusinessTables_CurrentBaseline()
    {
        var options = new DbContextOptionsBuilder<PropertyAssetsDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new PropertyAssetsDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT table_schema, table_name 
            FROM information_schema.tables 
            WHERE table_schema IN ('property_assets', 'apartments', 'auth', 'residents', 'administration', 'service_requests', 'complaints', 'maintenance', 'billing', 'payments', 'ai_classification', 'ai_recommendation', 'communication')
              AND table_type = 'BASE TABLE'
              AND table_name <> '__EFMigrationsHistory'
            ORDER BY table_schema, table_name;";

        var tables = new List<(string Schema, string Table)>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add((reader.GetString(0), reader.GetString(1)));
        }

        Assert.Equal(45, tables.Count);

        // PropertyAssets: 3
        Assert.Contains(("property_assets", "buildings"), tables);
        Assert.Contains(("property_assets", "facilities"), tables);
        Assert.Contains(("property_assets", "equipment"), tables);

        // Apartments: 1
        Assert.Contains(("apartments", "apartment_units"), tables);

        // Authentication: 4
        Assert.Contains(("auth", "users"), tables);
        Assert.Contains(("auth", "password_reset_tokens"), tables);
        Assert.Contains(("auth", "refresh_tokens"), tables);
        Assert.Contains(("auth", "resident_verifications"), tables);

        // Residents: 2
        Assert.Contains(("residents", "residents"), tables);
        Assert.Contains(("residents", "resident_apartments"), tables);

        // Administration: 8
        Assert.Contains(("administration", "roles"), tables);
        Assert.Contains(("administration", "permissions"), tables);
        Assert.Contains(("administration", "role_permissions"), tables);
        Assert.Contains(("administration", "user_role_assignments"), tables);
        Assert.Contains(("administration", "user_building_accesses"), tables);
        Assert.Contains(("administration", "user_access_history"), tables);
        Assert.Contains(("administration", "system_configurations"), tables);
        Assert.Contains(("administration", "audit_logs"), tables);

        // ServiceRequests: 4
        Assert.Contains(("service_requests", "service_request_categories"), tables);
        Assert.Contains(("service_requests", "service_requests"), tables);
        Assert.Contains(("service_requests", "service_request_assignments"), tables);
        Assert.Contains(("service_requests", "service_request_activities"), tables);

        // Complaints: 3
        Assert.Contains(("complaints", "complaints"), tables);
        Assert.Contains(("complaints", "complaint_followups"), tables);
        Assert.Contains(("complaints", "complaint_activities"), tables);

        // Maintenance: 5
        Assert.Contains(("maintenance", "maintenance_schedules"), tables);
        Assert.Contains(("maintenance", "maintenance_tasks"), tables);
        Assert.Contains(("maintenance", "maintenance_assignments"), tables);
        Assert.Contains(("maintenance", "maintenance_task_activities"), tables);
        Assert.Contains(("maintenance", "maintenance_results"), tables);

        // Billing: 5
        Assert.Contains(("billing", "fee_types"), tables);
        Assert.Contains(("billing", "fee_rate_rules"), tables);
        Assert.Contains(("billing", "invoices"), tables);
        Assert.Contains(("billing", "invoice_items"), tables);
        Assert.Contains(("billing", "invoice_status_history"), tables);

        // Payments: 2
        Assert.Contains(("payments", "payments"), tables);
        Assert.Contains(("payments", "payment_status_history"), tables);

        // AiClassification: 2
        Assert.Contains(("ai_classification", "ai_request_classifications"), tables);
        Assert.Contains(("ai_classification", "ai_classification_reviews"), tables);

        // AiRecommendation: 2
        Assert.Contains(("ai_recommendation", "ai_request_recommendations"), tables);
        Assert.Contains(("ai_recommendation", "ai_recommendation_reviews"), tables);

        // Communication: 4
        Assert.Contains(("communication", "notifications"), tables);
        Assert.Contains(("communication", "announcements"), tables);
        Assert.Contains(("communication", "announcement_versions"), tables);
        Assert.Contains(("communication", "announcement_audiences"), tables);
    }

    [Fact]
    public async Task Database_ShouldHaveMigrationHistoryTableForEachImplementedModule()
    {
        var options = new DbContextOptionsBuilder<PropertyAssetsDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new PropertyAssetsDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT table_schema, table_name 
            FROM information_schema.tables 
            WHERE table_schema IN ('property_assets', 'apartments', 'auth', 'residents', 'administration', 'service_requests', 'complaints', 'maintenance', 'billing', 'payments', 'ai_classification', 'ai_recommendation', 'communication')
              AND table_name = '__EFMigrationsHistory';";

        var historyTables = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            historyTables.Add(reader.GetString(0));
        }

        Assert.Equal(13, historyTables.Count);
        Assert.Contains("property_assets", historyTables);
        Assert.Contains("apartments", historyTables);
        Assert.Contains("auth", historyTables);
        Assert.Contains("residents", historyTables);
        Assert.Contains("administration", historyTables);
        Assert.Contains("service_requests", historyTables);
        Assert.Contains("complaints", historyTables);
        Assert.Contains("maintenance", historyTables);
        Assert.Contains("billing", historyTables);
        Assert.Contains("payments", historyTables);
        Assert.Contains("ai_classification", historyTables);
        Assert.Contains("ai_recommendation", historyTables);
        Assert.Contains("communication", historyTables);
    }

    [Fact]
    public async Task Database_ShouldNotHaveCrossModulePhysicalForeignKeys()
    {
        var options = new DbContextOptionsBuilder<PropertyAssetsDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new PropertyAssetsDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT 
                tc.table_schema, 
                tc.table_name, 
                ccu.table_schema AS foreign_table_schema,
                ccu.table_name AS foreign_table_name
            FROM information_schema.table_constraints AS tc 
            JOIN information_schema.constraint_column_usage AS ccu 
              ON ccu.constraint_name = tc.constraint_name 
             AND ccu.table_schema = tc.table_schema
            WHERE tc.constraint_type = 'FOREIGN KEY'
              AND tc.table_schema IN ('property_assets', 'apartments', 'auth', 'residents', 'administration', 'service_requests', 'complaints', 'maintenance', 'billing', 'payments', 'ai_classification', 'ai_recommendation', 'communication');";

        var crossFks = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var srcSchema = reader.GetString(0);
            var srcTable = reader.GetString(1);
            var foreignSchema = reader.GetString(2);
            var foreignTable = reader.GetString(3);

            if (srcSchema != foreignSchema)
            {
                crossFks.Add($"{srcSchema}.{srcTable} -> {foreignSchema}.{foreignTable}");
            }
        }

        Assert.Empty(crossFks);
    }

    [Fact]
    public async Task Database_ShouldBeReachableAndContainExpectedImplementedTables()
    {
        // Thay thế test ZeroRows: Kiểm tra database có thể truy cập được và các bảng đều có thể SELECT mà không phát sinh lỗi
        var options = new DbContextOptionsBuilder<PropertyAssetsDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new PropertyAssetsDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        var tables = new[]
        {
            "property_assets.buildings",
            "property_assets.facilities",
            "property_assets.equipment",
            "apartments.apartment_units",
            "auth.users",
            "auth.password_reset_tokens",
            "auth.refresh_tokens",
            "auth.resident_verifications",
            "residents.residents",
            "residents.resident_apartments",
            "administration.roles",
            "administration.permissions",
            "administration.role_permissions",
            "administration.user_role_assignments",
            "administration.user_building_accesses",
            "administration.user_access_history",
            "administration.system_configurations",
            "administration.audit_logs",
            "service_requests.service_request_categories",
            "service_requests.service_requests",
            "service_requests.service_request_assignments",
            "service_requests.service_request_activities",
            "complaints.complaints",
            "complaints.complaint_followups",
            "complaints.complaint_activities",
            "maintenance.maintenance_schedules",
            "maintenance.maintenance_tasks",
            "maintenance.maintenance_assignments",
            "maintenance.maintenance_task_activities",
            "maintenance.maintenance_results",
            "billing.fee_types",
            "billing.fee_rate_rules",
            "billing.invoices",
            "billing.invoice_items",
            "billing.invoice_status_history",
            "payments.payments",
            "payments.payment_status_history",
            "ai_classification.ai_request_classifications",
            "ai_classification.ai_classification_reviews",
            "ai_recommendation.ai_request_recommendations",
            "ai_recommendation.ai_recommendation_reviews"
        };

        foreach (var table in tables)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {table};";
            var count = await cmd.ExecuteScalarAsync();
            Assert.NotNull(count);
            Assert.True((long)count >= 0L);
        }
    }

    [Fact]
    public async Task ServiceRequests_ShouldContainExpectedFourTables()
    {
        var options = new DbContextOptionsBuilder<ServiceRequestsDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new ServiceRequestsDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'service_requests'
              AND table_type = 'BASE TABLE'
              AND table_name <> '__EFMigrationsHistory'
            ORDER BY table_name;";

        var tables = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Equal(4, tables.Count);
        Assert.Contains("service_request_categories", tables);
        Assert.Contains("service_requests", tables);
        Assert.Contains("service_request_assignments", tables);
        Assert.Contains("service_request_activities", tables);
    }

    [Fact]
    public async Task Complaints_ShouldContainExpectedThreeTables()
    {
        var options = new DbContextOptionsBuilder<ComplaintsDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new ComplaintsDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'complaints'
              AND table_type = 'BASE TABLE'
              AND table_name <> '__EFMigrationsHistory'
            ORDER BY table_name;";

        var tables = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Equal(3, tables.Count);
        Assert.Contains("complaints", tables);
        Assert.Contains("complaint_followups", tables);
        Assert.Contains("complaint_activities", tables);
    }

    [Fact]
    public async Task Maintenance_ShouldContainExpectedFiveTables()
    {
        var options = new DbContextOptionsBuilder<MaintenanceDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new MaintenanceDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'maintenance'
              AND table_type = 'BASE TABLE'
              AND table_name <> '__EFMigrationsHistory'
            ORDER BY table_name;";

        var tables = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Equal(5, tables.Count);
        Assert.Contains("maintenance_schedules", tables);
        Assert.Contains("maintenance_tasks", tables);
        Assert.Contains("maintenance_assignments", tables);
        Assert.Contains("maintenance_task_activities", tables);
        Assert.Contains("maintenance_results", tables);
    }

    [Fact]
    public async Task Billing_ShouldContainExpectedFiveTables()
    {
        var options = new DbContextOptionsBuilder<BillingDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new BillingDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'billing'
              AND table_type = 'BASE TABLE'
              AND table_name <> '__EFMigrationsHistory'
            ORDER BY table_name;";

        var tables = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Equal(5, tables.Count);
        Assert.Contains("fee_types", tables);
        Assert.Contains("fee_rate_rules", tables);
        Assert.Contains("invoices", tables);
        Assert.Contains("invoice_items", tables);
        Assert.Contains("invoice_status_history", tables);
    }

    [Fact]
    public async Task Billing_ShouldHaveInitialMigrationInHistory()
    {
        var options = new DbContextOptionsBuilder<BillingDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new BillingDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT ""MigrationId""
            FROM billing.""__EFMigrationsHistory""
            WHERE ""MigrationId"" LIKE '%InitialBilling';";

        var migrationId = (string?)await cmd.ExecuteScalarAsync();
        Assert.NotNull(migrationId);
    }

    [Fact]
    public async Task Billing_ShouldNotContainDemoRows()
    {
        await using var context = new BillingDbContext(
            new DbContextOptionsBuilder<BillingDbContext>()
                .UseNpgsql(_connectionString)
                .Options);

        Assert.Empty(await context.FeeTypes.AsNoTracking().ToListAsync());
        Assert.Empty(await context.FeeRateRules.AsNoTracking().ToListAsync());
        Assert.Empty(await context.Invoices.AsNoTracking().ToListAsync());
        Assert.Empty(await context.InvoiceItems.AsNoTracking().ToListAsync());
        Assert.Empty(await context.InvoiceStatusHistory.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Billing_ShouldNotHaveCrossModulePhysicalForeignKeys()
    {
        var options = new DbContextOptionsBuilder<BillingDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new BillingDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT
                tc.table_schema,
                tc.table_name,
                ccu.table_schema AS foreign_table_schema,
                ccu.table_name AS foreign_table_name
            FROM information_schema.table_constraints AS tc
            JOIN information_schema.constraint_column_usage AS ccu
              ON ccu.constraint_name = tc.constraint_name
             AND ccu.table_schema = tc.table_schema
            WHERE tc.constraint_type = 'FOREIGN KEY'
              AND tc.table_schema = 'billing';";

        var crossFks = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var srcSchema = reader.GetString(0);
            var srcTable = reader.GetString(1);
            var foreignSchema = reader.GetString(2);
            var foreignTable = reader.GetString(3);

            if (srcSchema != foreignSchema)
            {
                crossFks.Add($"{srcSchema}.{srcTable} -> {foreignSchema}.{foreignTable}");
            }
        }

        Assert.Empty(crossFks);
    }

    [Fact]
    public async Task Billing_ShouldEnforceMonetaryAndDateChecks()
    {
        await using var context = new BillingDbContext(
            new DbContextOptionsBuilder<BillingDbContext>()
                .UseNpgsql(_connectionString)
                .Options);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var now = DateTimeOffset.UtcNow;
        var feeTypeId = await InsertFeeTypeForConstraintTest(context, now);

        var invalidRule = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO billing.fee_rate_rules
                    (id, fee_type_id, rule_name, calculation_method_code, billing_frequency_code, unit_rate, effective_from, effective_to, is_active, created_by, created_at, updated_at)
                VALUES
                    ({Guid.NewGuid()}, {feeTypeId}, 'Invalid rule', 'FIXED', 'MONTHLY', {-1m}, {new DateOnly(2026, 9, 1)}, {new DateOnly(2026, 9, 30)}, true, {Guid.NewGuid()}, {now}, {now});"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalidRule.SqlState);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Billing_ShouldRejectInvalidInvoicePeriod()
    {
        await using var context = new BillingDbContext(
            new DbContextOptionsBuilder<BillingDbContext>()
                .UseNpgsql(_connectionString)
                .Options);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var now = DateTimeOffset.UtcNow;
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO billing.invoices
                    (id, invoice_number, apartment_unit_id, billing_period_start, billing_period_end, subtotal, total_amount, status, created_by, created_at, updated_at)
                VALUES
                    ({Guid.NewGuid()}, {$"INV-{Guid.NewGuid():N}"[..20]}, {Guid.NewGuid()}, {new DateOnly(2026, 9, 30)}, {new DateOnly(2026, 9, 1)}, {0m}, {0m}, 'DRAFT', {Guid.NewGuid()}, {now}, {now});"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    [Fact]
    public async Task Billing_ShouldEnforceUniqueInvoicePerApartmentAndBillingPeriod()
    {
        await using var context = new BillingDbContext(
            new DbContextOptionsBuilder<BillingDbContext>()
                .UseNpgsql(_connectionString)
                .Options);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var now = DateTimeOffset.UtcNow;
        var apartmentUnitId = Guid.NewGuid();
        var start = new DateOnly(2026, 9, 1);
        var end = new DateOnly(2026, 9, 30);

        await context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO billing.invoices
                (id, invoice_number, apartment_unit_id, billing_period_start, billing_period_end, subtotal, total_amount, status, created_by, created_at, updated_at)
            VALUES
                ({Guid.NewGuid()}, {$"INV-{Guid.NewGuid():N}"[..20]}, {apartmentUnitId}, {start}, {end}, {0m}, {0m}, 'DRAFT', {Guid.NewGuid()}, {now}, {now});");

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO billing.invoices
                    (id, invoice_number, apartment_unit_id, billing_period_start, billing_period_end, subtotal, total_amount, status, created_by, created_at, updated_at)
                VALUES
                    ({Guid.NewGuid()}, {$"INV-{Guid.NewGuid():N}"[..20]}, {apartmentUnitId}, {start}, {end}, {0m}, {0m}, 'DRAFT', {Guid.NewGuid()}, {now}, {now});"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Payments_ShouldContainExpectedTwoTables()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new PaymentsDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'payments'
              AND table_type = 'BASE TABLE'
              AND table_name <> '__EFMigrationsHistory'
            ORDER BY table_name;";

        var tables = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Equal(2, tables.Count);
        Assert.Contains("payments", tables);
        Assert.Contains("payment_status_history", tables);
    }

    [Fact]
    public async Task Payments_ShouldHaveInitialMigrationInHistory()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new PaymentsDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT ""MigrationId""
            FROM payments.""__EFMigrationsHistory""
            WHERE ""MigrationId"" LIKE '%InitialPayments';";

        var migrationId = (string?)await cmd.ExecuteScalarAsync();
        Assert.NotNull(migrationId);
    }

    [Fact]
    public async Task Payments_ShouldNotContainDemoRows()
    {
        await using var context = new PaymentsDbContext(
            new DbContextOptionsBuilder<PaymentsDbContext>()
                .UseNpgsql(_connectionString)
                .Options);

        Assert.Empty(await context.Payments.AsNoTracking().ToListAsync());
        Assert.Empty(await context.PaymentStatusHistory.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Payments_ShouldNotHaveCrossModulePhysicalForeignKeys()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new PaymentsDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT
                tc.table_schema,
                tc.table_name,
                ccu.table_schema AS foreign_table_schema,
                ccu.table_name AS foreign_table_name
            FROM information_schema.table_constraints AS tc
            JOIN information_schema.constraint_column_usage AS ccu
              ON ccu.constraint_name = tc.constraint_name
             AND ccu.table_schema = tc.table_schema
            WHERE tc.constraint_type = 'FOREIGN KEY'
              AND tc.table_schema = 'payments';";

        var crossFks = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var srcSchema = reader.GetString(0);
            var srcTable = reader.GetString(1);
            var foreignSchema = reader.GetString(2);
            var foreignTable = reader.GetString(3);

            if (srcSchema != foreignSchema)
            {
                crossFks.Add($"{srcSchema}.{srcTable} -> {foreignSchema}.{foreignTable}");
            }
        }

        Assert.Empty(crossFks);
    }

    [Fact]
    public async Task Payments_ShouldEnforceAmountAndStatusChecks()
    {
        await using var context = new PaymentsDbContext(
            new DbContextOptionsBuilder<PaymentsDbContext>()
                .UseNpgsql(_connectionString)
                .Options);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var now = DateTimeOffset.UtcNow;
        var invalidAmount = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO payments.payments
                    (id, payment_number, invoice_id, amount, payment_method_code, payment_date, status, created_at, updated_at)
                VALUES
                    ({Guid.NewGuid()}, {$"PAY-{Guid.NewGuid():N}"[..20]}, {Guid.NewGuid()}, {0m}, 'BANK_TRANSFER', {now}, 'PENDING', {now}, {now});"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalidAmount.SqlState);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Payments_ShouldRejectDuplicateReferenceNumberWhenPresent()
    {
        await using var context = new PaymentsDbContext(
            new DbContextOptionsBuilder<PaymentsDbContext>()
                .UseNpgsql(_connectionString)
                .Options);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var now = DateTimeOffset.UtcNow;
        var referenceNumber = $"TXN-{Guid.NewGuid():N}"[..20];

        await context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO payments.payments
                (id, payment_number, invoice_id, amount, payment_method_code, payment_date, reference_number, status, created_at, updated_at)
            VALUES
                ({Guid.NewGuid()}, {$"PAY-{Guid.NewGuid():N}"[..20]}, {Guid.NewGuid()}, {100000m}, 'BANK_TRANSFER', {now}, {referenceNumber}, 'PENDING', {now}, {now});");

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO payments.payments
                    (id, payment_number, invoice_id, amount, payment_method_code, payment_date, reference_number, status, created_at, updated_at)
                VALUES
                    ({Guid.NewGuid()}, {$"PAY-{Guid.NewGuid():N}"[..20]}, {Guid.NewGuid()}, {100000m}, 'BANK_TRANSFER', {now}, {referenceNumber}, 'PENDING', {now}, {now});"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task AiClassification_ShouldContainExpectedTwoTables()
    {
        var options = new DbContextOptionsBuilder<AiClassificationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new AiClassificationDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'ai_classification'
              AND table_type = 'BASE TABLE'
              AND table_name <> '__EFMigrationsHistory'
            ORDER BY table_name;";

        var tables = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Equal(2, tables.Count);
        Assert.Contains("ai_request_classifications", tables);
        Assert.Contains("ai_classification_reviews", tables);
    }

    [Fact]
    public async Task AiClassification_ShouldHaveInitialMigrationInHistory()
    {
        var options = new DbContextOptionsBuilder<AiClassificationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new AiClassificationDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT ""MigrationId""
            FROM ai_classification.""__EFMigrationsHistory""
            WHERE ""MigrationId"" LIKE '%InitialAiClassification';";

        var migrationId = (string?)await cmd.ExecuteScalarAsync();
        Assert.NotNull(migrationId);
    }

    [Fact]
    public async Task AiClassification_ShouldHaveCorrectiveDomainConsistencyMigrationInHistory()
    {
        var options = new DbContextOptionsBuilder<AiClassificationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new AiClassificationDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT ""MigrationId""
            FROM ai_classification.""__EFMigrationsHistory""
            WHERE ""MigrationId"" LIKE '%CorrectAiClassificationDomainConsistency';";

        var migrationId = (string?)await cmd.ExecuteScalarAsync();
        Assert.NotNull(migrationId);
    }

    [Fact]
    public async Task AiClassification_ShouldNotContainFakeAiRows()
    {
        await using var context = new AiClassificationDbContext(
            new DbContextOptionsBuilder<AiClassificationDbContext>()
                .UseNpgsql(_connectionString)
                .Options);

        Assert.Empty(await context.AiRequestClassifications.AsNoTracking().ToListAsync());
        Assert.Empty(await context.AiClassificationReviews.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task AiClassification_ShouldNotHaveCrossModulePhysicalForeignKeys()
    {
        var options = new DbContextOptionsBuilder<AiClassificationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new AiClassificationDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT
                tc.table_schema,
                tc.table_name,
                ccu.table_schema AS foreign_table_schema,
                ccu.table_name AS foreign_table_name
            FROM information_schema.table_constraints AS tc
            JOIN information_schema.constraint_column_usage AS ccu
              ON ccu.constraint_name = tc.constraint_name
             AND ccu.table_schema = tc.table_schema
            WHERE tc.constraint_type = 'FOREIGN KEY'
              AND tc.table_schema = 'ai_classification';";

        var crossFks = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var srcSchema = reader.GetString(0);
            var srcTable = reader.GetString(1);
            var foreignSchema = reader.GetString(2);
            var foreignTable = reader.GetString(3);

            if (srcSchema != foreignSchema)
            {
                crossFks.Add($"{srcSchema}.{srcTable} -> {foreignSchema}.{foreignTable}");
            }
        }

        Assert.Empty(crossFks);
    }

    [Fact]
    public async Task AiClassification_ShouldEnforceAttemptAndConfidenceChecks()
    {
        await using var context = new AiClassificationDbContext(
            new DbContextOptionsBuilder<AiClassificationDbContext>()
                .UseNpgsql(_connectionString)
                .Options);

        var now = DateTimeOffset.UtcNow;
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ai_classification.ai_request_classifications
                    (id, service_request_id, attempt_no, provider, model_name, predicted_category_id, confidence_score, run_status, created_at)
                VALUES
                    ({Guid.NewGuid()}, {Guid.NewGuid()}, {0}, 'provider', 'model', {Guid.NewGuid()}, {1.1000m}, 'SUCCESS', {now});"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    [Fact]
    public async Task AiClassification_ShouldRejectDuplicateAttemptForServiceRequest()
    {
        await using var context = new AiClassificationDbContext(
            new DbContextOptionsBuilder<AiClassificationDbContext>()
                .UseNpgsql(_connectionString)
                .Options);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var now = DateTimeOffset.UtcNow;
        var serviceRequestId = Guid.NewGuid();

        await context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO ai_classification.ai_request_classifications
                (id, service_request_id, attempt_no, provider, model_name, predicted_category_id, confidence_score, run_status, created_at)
            VALUES
                ({Guid.NewGuid()}, {serviceRequestId}, {1}, 'provider', 'model', {Guid.NewGuid()}, {0.9000m}, 'SUCCESS', {now});");

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ai_classification.ai_request_classifications
                    (id, service_request_id, attempt_no, provider, model_name, predicted_category_id, confidence_score, run_status, created_at)
                VALUES
                    ({Guid.NewGuid()}, {serviceRequestId}, {1}, 'provider', 'model', {Guid.NewGuid()}, {0.8000m}, 'SUCCESS', {now});"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task AiClassification_ShouldRejectInvalidSuccessAndFailureFieldCombinations()
    {
        var now = DateTimeOffset.UtcNow;
        var options = new DbContextOptionsBuilder<AiClassificationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var successContext = new AiClassificationDbContext(options);
        var successWithError = await Assert.ThrowsAsync<PostgresException>(() =>
            successContext.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ai_classification.ai_request_classifications
                    (id, service_request_id, attempt_no, provider, model_name, predicted_category_id, confidence_score, run_status, error_message, created_at)
                VALUES
                    ({Guid.NewGuid()}, {Guid.NewGuid()}, {1}, 'provider', 'model', {Guid.NewGuid()}, {0.9000m}, 'SUCCESS', 'should not be present', {now});"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, successWithError.SqlState);

        await using var failedContext = new AiClassificationDbContext(options);
        var failedWithPrediction = await Assert.ThrowsAsync<PostgresException>(() =>
            failedContext.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ai_classification.ai_request_classifications
                    (id, service_request_id, attempt_no, provider, model_name, predicted_category_id, confidence_score, reasoning_summary, run_status, error_message, created_at)
                VALUES
                    ({Guid.NewGuid()}, {Guid.NewGuid()}, {1}, 'provider', 'model', {Guid.NewGuid()}, {0.9000m}, 'summary', 'FAILED', 'provider failed', {now});"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, failedWithPrediction.SqlState);
    }

    [Fact]
    public async Task AiClassification_ShouldRejectDuplicateReviewForClassification()
    {
        await using var context = new AiClassificationDbContext(
            new DbContextOptionsBuilder<AiClassificationDbContext>()
                .UseNpgsql(_connectionString)
                .Options);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var now = DateTimeOffset.UtcNow;
        var classificationId = Guid.NewGuid();
        var predictedCategoryId = Guid.NewGuid();

        await context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO ai_classification.ai_request_classifications
                (id, service_request_id, attempt_no, provider, model_name, predicted_category_id, confidence_score, run_status, created_at)
            VALUES
                ({classificationId}, {Guid.NewGuid()}, {1}, 'provider', 'model', {predictedCategoryId}, {0.9000m}, 'SUCCESS', {now});");

        await context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO ai_classification.ai_classification_reviews
                (id, classification_id, reviewed_by, decision, final_category_id, reviewed_at)
            VALUES
                ({Guid.NewGuid()}, {classificationId}, {Guid.NewGuid()}, 'CONFIRMED', {predictedCategoryId}, {now});");

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ai_classification.ai_classification_reviews
                    (id, classification_id, reviewed_by, decision, final_category_id, reviewed_at)
                VALUES
                    ({Guid.NewGuid()}, {classificationId}, {Guid.NewGuid()}, 'CONFIRMED', {predictedCategoryId}, {now});"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task AiRecommendation_ShouldContainExpectedTwoTables()
    {
        var options = new DbContextOptionsBuilder<AiRecommendationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new AiRecommendationDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'ai_recommendation'
              AND table_type = 'BASE TABLE'
              AND table_name <> '__EFMigrationsHistory'
            ORDER BY table_name;";

        var tables = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Equal(2, tables.Count);
        Assert.Contains("ai_request_recommendations", tables);
        Assert.Contains("ai_recommendation_reviews", tables);
    }

    [Fact]
    public async Task AiRecommendation_ShouldHaveInitialMigrationInHistory()
    {
        var options = new DbContextOptionsBuilder<AiRecommendationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new AiRecommendationDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT ""MigrationId""
            FROM ai_recommendation.""__EFMigrationsHistory""
            WHERE ""MigrationId"" LIKE '%InitialAiRecommendation';";

        var migrationId = (string?)await cmd.ExecuteScalarAsync();
        Assert.NotNull(migrationId);
    }

    [Fact]
    public async Task AiRecommendation_ShouldNotContainFakeAiRows()
    {
        await using var context = new AiRecommendationDbContext(
            new DbContextOptionsBuilder<AiRecommendationDbContext>()
                .UseNpgsql(_connectionString)
                .Options);

        Assert.Empty(await context.AiRequestRecommendations.AsNoTracking().ToListAsync());
        Assert.Empty(await context.AiRecommendationReviews.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task AiRecommendation_ShouldNotHaveCrossModulePhysicalForeignKeys()
    {
        var options = new DbContextOptionsBuilder<AiRecommendationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new AiRecommendationDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT
                tc.table_schema,
                tc.table_name,
                ccu.table_schema AS foreign_table_schema,
                ccu.table_name AS foreign_table_name
            FROM information_schema.table_constraints AS tc
            JOIN information_schema.constraint_column_usage AS ccu
              ON ccu.constraint_name = tc.constraint_name
             AND ccu.table_schema = tc.table_schema
            WHERE tc.constraint_type = 'FOREIGN KEY'
              AND tc.table_schema = 'ai_recommendation';";

        var crossFks = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var srcSchema = reader.GetString(0);
            var srcTable = reader.GetString(1);
            var foreignSchema = reader.GetString(2);
            var foreignTable = reader.GetString(3);

            if (srcSchema != foreignSchema)
            {
                crossFks.Add($"{srcSchema}.{srcTable} -> {foreignSchema}.{foreignTable}");
            }
        }

        Assert.Empty(crossFks);
    }

    [Fact]
    public async Task AiRecommendation_ShouldEnforceAttemptAndOutputConsistencyChecks()
    {
        var now = DateTimeOffset.UtcNow;
        var options = new DbContextOptionsBuilder<AiRecommendationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var invalidAttemptContext = new AiRecommendationDbContext(options);
        var invalidAttempt = await Assert.ThrowsAsync<PostgresException>(() =>
            invalidAttemptContext.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ai_recommendation.ai_request_recommendations
                    (id, service_request_id, attempt_no, provider, model_name, suggested_priority_code, run_status, created_at)
                VALUES
                    ({Guid.NewGuid()}, {Guid.NewGuid()}, {0}, 'provider', 'model', 'URGENT', 'SUCCESS', {now});"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, invalidAttempt.SqlState);

        await using var successContext = new AiRecommendationDbContext(options);
        var successWithError = await Assert.ThrowsAsync<PostgresException>(() =>
            successContext.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ai_recommendation.ai_request_recommendations
                    (id, service_request_id, attempt_no, provider, model_name, suggested_priority_code, run_status, error_message, created_at)
                VALUES
                    ({Guid.NewGuid()}, {Guid.NewGuid()}, {1}, 'provider', 'model', 'URGENT', 'SUCCESS', 'should not be present', {now});"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, successWithError.SqlState);

        await using var failedContext = new AiRecommendationDbContext(options);
        var failedWithOutput = await Assert.ThrowsAsync<PostgresException>(() =>
            failedContext.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ai_recommendation.ai_request_recommendations
                    (id, service_request_id, attempt_no, provider, model_name, suggested_priority_code, maintenance_recommended, run_status, error_message, created_at)
                VALUES
                    ({Guid.NewGuid()}, {Guid.NewGuid()}, {1}, 'provider', 'model', 'URGENT', {true}, 'FAILED', 'provider failed', {now});"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, failedWithOutput.SqlState);
    }

    [Fact]
    public async Task AiRecommendation_ShouldRejectDuplicateAttemptForServiceRequest()
    {
        await using var context = new AiRecommendationDbContext(
            new DbContextOptionsBuilder<AiRecommendationDbContext>()
                .UseNpgsql(_connectionString)
                .Options);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var now = DateTimeOffset.UtcNow;
        var serviceRequestId = Guid.NewGuid();

        await context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO ai_recommendation.ai_request_recommendations
                (id, service_request_id, attempt_no, provider, model_name, suggested_priority_code, run_status, created_at)
            VALUES
                ({Guid.NewGuid()}, {serviceRequestId}, {1}, 'provider', 'model', 'URGENT', 'SUCCESS', {now});");

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ai_recommendation.ai_request_recommendations
                    (id, service_request_id, attempt_no, provider, model_name, suggested_priority_code, run_status, created_at)
                VALUES
                    ({Guid.NewGuid()}, {serviceRequestId}, {1}, 'provider', 'model', 'NORMAL', 'SUCCESS', {now});"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task AiRecommendation_ShouldRejectDuplicateReviewForRecommendation()
    {
        await using var context = new AiRecommendationDbContext(
            new DbContextOptionsBuilder<AiRecommendationDbContext>()
                .UseNpgsql(_connectionString)
                .Options);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var now = DateTimeOffset.UtcNow;
        var recommendationId = Guid.NewGuid();

        await context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO ai_recommendation.ai_request_recommendations
                (id, service_request_id, attempt_no, provider, model_name, suggested_priority_code, run_status, created_at)
            VALUES
                ({recommendationId}, {Guid.NewGuid()}, {1}, 'provider', 'model', 'URGENT', 'SUCCESS', {now});");

        await context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO ai_recommendation.ai_recommendation_reviews
                (id, recommendation_id, reviewed_by, decision, final_priority_code, reviewed_at)
            VALUES
                ({Guid.NewGuid()}, {recommendationId}, {Guid.NewGuid()}, 'CONFIRMED', 'URGENT', {now});");

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ai_recommendation.ai_recommendation_reviews
                    (id, recommendation_id, reviewed_by, decision, final_priority_code, reviewed_at)
                VALUES
                    ({Guid.NewGuid()}, {recommendationId}, {Guid.NewGuid()}, 'CONFIRMED', 'URGENT', {now});"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Communication_ShouldContainExpectedFourTables()
    {
        var options = new DbContextOptionsBuilder<CommunicationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new CommunicationDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'communication'
              AND table_type = 'BASE TABLE'
              AND table_name <> '__EFMigrationsHistory'
            ORDER BY table_name;";

        var tables = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Equal(4, tables.Count);
        Assert.Contains("notifications", tables);
        Assert.Contains("announcements", tables);
        Assert.Contains("announcement_versions", tables);
        Assert.Contains("announcement_audiences", tables);
    }

    [Fact]
    public async Task Communication_ShouldHaveInitialMigrationInHistory()
    {
        var options = new DbContextOptionsBuilder<CommunicationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new CommunicationDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT ""MigrationId""
            FROM communication.""__EFMigrationsHistory""
            WHERE ""MigrationId"" LIKE '%InitialCommunication';";

        var migrationId = (string?)await cmd.ExecuteScalarAsync();
        Assert.NotNull(migrationId);
    }

    [Fact]
    public async Task Communication_ShouldHaveAudienceUniquenessCorrectiveMigrationInHistory()
    {
        var options = new DbContextOptionsBuilder<CommunicationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new CommunicationDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT ""MigrationId""
            FROM communication.""__EFMigrationsHistory""
            WHERE ""MigrationId"" LIKE '%CorrectCommunicationAudienceUniqueness';";

        var migrationId = (string?)await cmd.ExecuteScalarAsync();
        Assert.NotNull(migrationId);
    }

    [Fact]
    public async Task Communication_ShouldNotContainSeedOrDemoRows()
    {
        await using var context = new CommunicationDbContext(
            new DbContextOptionsBuilder<CommunicationDbContext>()
                .UseNpgsql(_connectionString)
                .Options);

        Assert.Empty(await context.Notifications.AsNoTracking().ToListAsync());
        Assert.Empty(await context.Announcements.AsNoTracking().ToListAsync());
        Assert.Empty(await context.AnnouncementAudiences.AsNoTracking().ToListAsync());
        Assert.Empty(await context.AnnouncementVersions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Communication_ShouldNotHaveCrossModulePhysicalForeignKeys()
    {
        var options = new DbContextOptionsBuilder<CommunicationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new CommunicationDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT
                tc.table_schema,
                tc.table_name,
                ccu.table_schema AS foreign_table_schema,
                ccu.table_name AS foreign_table_name
            FROM information_schema.table_constraints AS tc
            JOIN information_schema.constraint_column_usage AS ccu
              ON ccu.constraint_name = tc.constraint_name
             AND ccu.table_schema = tc.table_schema
            WHERE tc.constraint_type = 'FOREIGN KEY'
              AND tc.table_schema = 'communication';";

        var crossFks = new List<string>();
        var internalFks = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var srcSchema = reader.GetString(0);
            var srcTable = reader.GetString(1);
            var foreignSchema = reader.GetString(2);
            var foreignTable = reader.GetString(3);
            var label = $"{srcSchema}.{srcTable} -> {foreignSchema}.{foreignTable}";

            if (srcSchema != foreignSchema)
            {
                crossFks.Add(label);
            }
            else
            {
                internalFks.Add(label);
            }
        }

        Assert.Empty(crossFks);
        Assert.Contains("communication.announcement_audiences -> communication.announcements", internalFks);
        Assert.Contains("communication.announcement_versions -> communication.announcements", internalFks);
    }

    [Fact]
    public async Task Communication_ShouldEnforceReadAudienceAndLifecycleChecks()
    {
        var now = DateTimeOffset.UtcNow;
        var options = new DbContextOptionsBuilder<CommunicationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var notificationContext = new CommunicationDbContext(options);
        var invalidReadState = await Assert.ThrowsAsync<PostgresException>(() =>
            notificationContext.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO communication.notifications
                    (id, recipient_user_id, type, title, message, is_read, read_at, created_at)
                VALUES
                    ({Guid.NewGuid()}, {Guid.NewGuid()}, 'SYSTEM', 'Title', 'Message', {true}, {null}, {now});"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalidReadState.SqlState);

        await using var sourceContext = new CommunicationDbContext(options);
        var invalidSourceReference = await Assert.ThrowsAsync<PostgresException>(() =>
            sourceContext.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO communication.notifications
                    (id, recipient_user_id, type, title, message, source_type, is_read, created_at)
                VALUES
                    ({Guid.NewGuid()}, {Guid.NewGuid()}, 'SYSTEM', 'Title', 'Message', 'INVOICE', {false}, {now});"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalidSourceReference.SqlState);

        await using var announcementContext = new CommunicationDbContext(options);
        var invalidLifecycle = await Assert.ThrowsAsync<PostgresException>(() =>
            announcementContext.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO communication.announcements
                    (id, title, content, status, created_by, created_at, updated_at)
                VALUES
                    ({Guid.NewGuid()}, 'Title', 'Content', 'PUBLISHED', {Guid.NewGuid()}, {now}, {now});"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalidLifecycle.SqlState);

        await using var audienceContext = new CommunicationDbContext(options);
        await using var transaction = await audienceContext.Database.BeginTransactionAsync();
        var announcementId = Guid.NewGuid();
        await audienceContext.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO communication.announcements
                (id, title, content, status, created_by, created_at, updated_at)
            VALUES
                ({announcementId}, 'Title', 'Content', 'DRAFT', {Guid.NewGuid()}, {now}, {now});");

        var invalidAudience = await Assert.ThrowsAsync<PostgresException>(() =>
            audienceContext.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO communication.announcement_audiences
                    (id, announcement_id, audience_type, role_id, building_id, created_at)
                VALUES
                    ({Guid.NewGuid()}, {announcementId}, 'ROLE', {Guid.NewGuid()}, {Guid.NewGuid()}, {now});"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalidAudience.SqlState);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Communication_ShouldHaveRequiredIndexesAndJsonColumn()
    {
        var options = new DbContextOptionsBuilder<CommunicationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new CommunicationDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT indexname
            FROM pg_indexes
            WHERE schemaname = 'communication'
              AND indexname IN (
                'IX_notifications_recipient_user_id_is_read',
                'IX_notifications_recipient_user_id_source_event_id',
                'IX_announcement_versions_announcement_id_version_no');

            SELECT data_type, udt_name
            FROM information_schema.columns
            WHERE table_schema = 'communication'
              AND table_name = 'announcement_versions'
              AND column_name = 'audience_snapshot';";

        var indexes = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            indexes.Add(reader.GetString(0));
        }

        Assert.Contains("IX_notifications_recipient_user_id_is_read", indexes);
        Assert.Contains("IX_notifications_recipient_user_id_source_event_id", indexes);
        Assert.Contains("IX_announcement_versions_announcement_id_version_no", indexes);

        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.Equal("jsonb", reader.GetString(0));
        Assert.Equal("jsonb", reader.GetString(1));
    }

    [Fact]
    public async Task Communication_ShouldHaveAudienceUniquenessPartialIndexes()
    {
        var options = new DbContextOptionsBuilder<CommunicationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new CommunicationDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT indexname, indexdef
            FROM pg_indexes
            WHERE schemaname = 'communication'
              AND tablename = 'announcement_audiences'
              AND indexname LIKE 'UX_announcement_audiences_%';";

        var indexes = new Dictionary<string, string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            indexes.Add(reader.GetString(0), reader.GetString(1));
        }

        Assert.Contains("UX_announcement_audiences_announcement_type_global", indexes.Keys);
        Assert.Contains("'ALL_USERS'", indexes["UX_announcement_audiences_announcement_type_global"]);
        Assert.Contains("'ALL_RESIDENTS'", indexes["UX_announcement_audiences_announcement_type_global"]);
        Assert.Contains("UX_announcement_audiences_announcement_role", indexes.Keys);
        Assert.Contains("'ROLE'::text", indexes["UX_announcement_audiences_announcement_role"]);
        Assert.Contains("role_id IS NOT NULL", indexes["UX_announcement_audiences_announcement_role"]);
        Assert.Contains("UX_announcement_audiences_announcement_building", indexes.Keys);
        Assert.Contains("'BUILDING'::text", indexes["UX_announcement_audiences_announcement_building"]);
        Assert.Contains("building_id IS NOT NULL", indexes["UX_announcement_audiences_announcement_building"]);
        Assert.Contains("UX_announcement_audiences_announcement_apartment", indexes.Keys);
        Assert.Contains("'APARTMENT'::text", indexes["UX_announcement_audiences_announcement_apartment"]);
        Assert.Contains("apartment_unit_id IS NOT NULL", indexes["UX_announcement_audiences_announcement_apartment"]);
        Assert.Contains("UX_announcement_audiences_announcement_resident", indexes.Keys);
        Assert.Contains("'RESIDENT'::text", indexes["UX_announcement_audiences_announcement_resident"]);
        Assert.Contains("resident_id IS NOT NULL", indexes["UX_announcement_audiences_announcement_resident"]);
    }

    [Fact]
    public async Task Communication_ShouldRejectDuplicateAnnouncementAudienceScopesInDatabase()
    {
        await AssertDuplicateAudienceRejected("ALL_USERS");
        await AssertDuplicateAudienceRejected("ALL_RESIDENTS");
        await AssertDuplicateAudienceRejected("ROLE", roleId: Guid.NewGuid());
        await AssertDuplicateAudienceRejected("BUILDING", buildingId: Guid.NewGuid());
        await AssertDuplicateAudienceRejected("APARTMENT", apartmentUnitId: Guid.NewGuid());
        await AssertDuplicateAudienceRejected("RESIDENT", residentId: Guid.NewGuid());
    }

    [Fact]
    public async Task Communication_ShouldAllowDifferentAudienceTargetsAndTypesInDatabase()
    {
        var options = new DbContextOptionsBuilder<CommunicationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new CommunicationDbContext(options);
        await using var transaction = await context.Database.BeginTransactionAsync();
        var now = DateTimeOffset.UtcNow;
        var announcementId = Guid.NewGuid();
        await InsertAnnouncementForCommunicationAudienceTest(context, announcementId, now);

        await InsertAnnouncementAudienceForUniquenessTest(context, announcementId, "ROLE", now, roleId: Guid.NewGuid());
        await InsertAnnouncementAudienceForUniquenessTest(context, announcementId, "ROLE", now, roleId: Guid.NewGuid());
        await InsertAnnouncementAudienceForUniquenessTest(context, announcementId, "BUILDING", now, buildingId: Guid.NewGuid());
        await InsertAnnouncementAudienceForUniquenessTest(context, announcementId, "BUILDING", now, buildingId: Guid.NewGuid());
        await InsertAnnouncementAudienceForUniquenessTest(context, announcementId, "APARTMENT", now, apartmentUnitId: Guid.NewGuid());
        await InsertAnnouncementAudienceForUniquenessTest(context, announcementId, "APARTMENT", now, apartmentUnitId: Guid.NewGuid());
        await InsertAnnouncementAudienceForUniquenessTest(context, announcementId, "RESIDENT", now, residentId: Guid.NewGuid());
        await InsertAnnouncementAudienceForUniquenessTest(context, announcementId, "RESIDENT", now, residentId: Guid.NewGuid());

        var sameGuidAcrossTypes = Guid.NewGuid();
        await InsertAnnouncementAudienceForUniquenessTest(context, announcementId, "ROLE", now, roleId: sameGuidAcrossTypes);
        await InsertAnnouncementAudienceForUniquenessTest(context, announcementId, "BUILDING", now, buildingId: sameGuidAcrossTypes);
        await InsertAnnouncementAudienceForUniquenessTest(context, announcementId, "APARTMENT", now, apartmentUnitId: sameGuidAcrossTypes);
        await InsertAnnouncementAudienceForUniquenessTest(context, announcementId, "RESIDENT", now, residentId: sameGuidAcrossTypes);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Maintenance_ShouldHaveInitialMigrationInHistory()
    {
        var options = new DbContextOptionsBuilder<MaintenanceDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new MaintenanceDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT ""MigrationId""
            FROM maintenance.""__EFMigrationsHistory""
            WHERE ""MigrationId"" LIKE '%InitialMaintenance';";

        var migrationId = (string?)await cmd.ExecuteScalarAsync();
        Assert.NotNull(migrationId);
    }

    [Fact]
    public async Task Maintenance_ShouldHavePartialUniqueIndexForActiveAssignment()
    {
        var options = new DbContextOptionsBuilder<MaintenanceDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new MaintenanceDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'maintenance'
              AND tablename = 'maintenance_assignments'
              AND indexname = 'IX_maintenance_assignments_active_maintenance_task_id'
              AND indexdef ILIKE '%UNIQUE%'
              AND indexdef ILIKE '%maintenance_task_id%'
              AND indexdef ILIKE '%status%ASSIGNED%'
              AND indexdef ILIKE '%IN_PROGRESS%';";

        var indexDefinition = (string?)await cmd.ExecuteScalarAsync();

        Assert.NotNull(indexDefinition);
    }

    [Fact]
    public async Task Maintenance_ShouldRejectTwoActiveAssignmentsForSameTask()
    {
        await using var context = new MaintenanceDbContext(
            new DbContextOptionsBuilder<MaintenanceDbContext>()
                .UseNpgsql(_connectionString)
                .Options);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var taskId = await InsertMaintenanceTaskForConstraintTest(context, DateTimeOffset.UtcNow);
        var now = DateTimeOffset.UtcNow;

        await context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO maintenance.maintenance_assignments
                (id, maintenance_task_id, staff_user_id, assigned_by, status, assigned_at)
            VALUES
                ({Guid.NewGuid()}, {taskId}, {Guid.NewGuid()}, {Guid.NewGuid()}, 'ASSIGNED', {now});");

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO maintenance.maintenance_assignments
                    (id, maintenance_task_id, staff_user_id, assigned_by, status, assigned_at)
                VALUES
                    ({Guid.NewGuid()}, {taskId}, {Guid.NewGuid()}, {Guid.NewGuid()}, 'IN_PROGRESS', {now.AddMinutes(1)});"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Maintenance_ShouldEnforceAttemptNoCheck()
    {
        await using var context = new MaintenanceDbContext(
            new DbContextOptionsBuilder<MaintenanceDbContext>()
                .UseNpgsql(_connectionString)
                .Options);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var taskId = await InsertMaintenanceTaskForConstraintTest(context, DateTimeOffset.UtcNow);
        var now = DateTimeOffset.UtcNow;

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO maintenance.maintenance_results
                    (id, maintenance_task_id, attempt_no, submitted_by, summary, result_status, submitted_at, updated_at)
                VALUES
                    ({Guid.NewGuid()}, {taskId}, {0}, {Guid.NewGuid()}, 'Invalid attempt', 'SUBMITTED', {now}, {now});"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Maintenance_ShouldNotContainDemoRows()
    {
        await using var context = new MaintenanceDbContext(
            new DbContextOptionsBuilder<MaintenanceDbContext>()
                .UseNpgsql(_connectionString)
                .Options);

        Assert.Empty(await context.MaintenanceSchedules.AsNoTracking().ToListAsync());
        Assert.Empty(await context.MaintenanceTasks.AsNoTracking().ToListAsync());
        Assert.Empty(await context.MaintenanceAssignments.AsNoTracking().ToListAsync());
        Assert.Empty(await context.MaintenanceTaskActivities.AsNoTracking().ToListAsync());
        Assert.Empty(await context.MaintenanceResults.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Maintenance_ShouldNotHaveCrossModulePhysicalForeignKeys()
    {
        var options = new DbContextOptionsBuilder<MaintenanceDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new MaintenanceDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT
                tc.table_schema,
                tc.table_name,
                ccu.table_schema AS foreign_table_schema,
                ccu.table_name AS foreign_table_name
            FROM information_schema.table_constraints AS tc
            JOIN information_schema.constraint_column_usage AS ccu
              ON ccu.constraint_name = tc.constraint_name
             AND ccu.table_schema = tc.table_schema
            WHERE tc.constraint_type = 'FOREIGN KEY'
              AND tc.table_schema = 'maintenance';";

        var crossFks = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var srcSchema = reader.GetString(0);
            var srcTable = reader.GetString(1);
            var foreignSchema = reader.GetString(2);
            var foreignTable = reader.GetString(3);

            if (srcSchema != foreignSchema)
            {
                crossFks.Add($"{srcSchema}.{srcTable} -> {foreignSchema}.{foreignTable}");
            }
        }

        Assert.Empty(crossFks);
    }

    [Fact]
    public async Task ServiceRequests_ShouldHavePartialUniqueIndexForActiveAssignment()
    {
        var options = new DbContextOptionsBuilder<ServiceRequestsDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new ServiceRequestsDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'service_requests'
              AND tablename = 'service_request_assignments'
              AND indexname = 'IX_service_request_assignments_active_service_request_id'
              AND indexdef ILIKE '%UNIQUE%'
              AND indexdef ILIKE '%service_request_id%'
              AND indexdef ILIKE '%status%ASSIGNED%'
              AND indexdef ILIKE '%IN_PROGRESS%';";

        var indexDefinition = (string?)await cmd.ExecuteScalarAsync();

        Assert.NotNull(indexDefinition);
    }

    [Fact]
    public async Task ServiceRequests_ShouldRejectTwoActiveAssignmentsForSameRequest()
    {
        await using var context = new ServiceRequestsDbContext(
            new DbContextOptionsBuilder<ServiceRequestsDbContext>()
                .UseNpgsql(_connectionString)
                .Options);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var requestId = await InsertServiceRequestForConstraintTest(context, DateTimeOffset.UtcNow);
        var now = DateTimeOffset.UtcNow;

        await context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO service_requests.service_request_assignments
                (id, service_request_id, staff_user_id, assigned_by, status, assigned_at)
            VALUES
                ({Guid.NewGuid()}, {requestId}, {Guid.NewGuid()}, {Guid.NewGuid()}, 'ASSIGNED', {now});");

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO service_requests.service_request_assignments
                    (id, service_request_id, staff_user_id, assigned_by, status, assigned_at)
                VALUES
                    ({Guid.NewGuid()}, {requestId}, {Guid.NewGuid()}, {Guid.NewGuid()}, 'IN_PROGRESS', {now.AddMinutes(1)});"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task ServiceRequests_ShouldAllowHistoricalReassignmentAfterPreviousAssignmentEnds()
    {
        await using var context = new ServiceRequestsDbContext(
            new DbContextOptionsBuilder<ServiceRequestsDbContext>()
                .UseNpgsql(_connectionString)
                .Options);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var requestId = await InsertServiceRequestForConstraintTest(context, DateTimeOffset.UtcNow);
        var now = DateTimeOffset.UtcNow;

        await context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO service_requests.service_request_assignments
                (id, service_request_id, staff_user_id, assigned_by, status, assigned_at, ended_at)
            VALUES
                ({Guid.NewGuid()}, {requestId}, {Guid.NewGuid()}, {Guid.NewGuid()}, 'REASSIGNED', {now}, {now.AddMinutes(5)});");

        await context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO service_requests.service_request_assignments
                (id, service_request_id, staff_user_id, assigned_by, status, assigned_at)
            VALUES
                ({Guid.NewGuid()}, {requestId}, {Guid.NewGuid()}, {Guid.NewGuid()}, 'ASSIGNED', {now.AddMinutes(10)});");

        var assignmentCount = await context.ServiceRequestAssignments.CountAsync(assignment => assignment.ServiceRequestId == requestId);
        Assert.Equal(2, assignmentCount);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Administration_ShouldContainExpectedEightTables()
    {
        var options = new DbContextOptionsBuilder<AdministrationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new AdministrationDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'administration'
              AND table_type = 'BASE TABLE'
              AND table_name <> '__EFMigrationsHistory'
            ORDER BY table_name;";

        var tables = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Equal(8, tables.Count);
        Assert.Contains("roles", tables);
        Assert.Contains("permissions", tables);
        Assert.Contains("role_permissions", tables);
        Assert.Contains("user_role_assignments", tables);
        Assert.Contains("user_building_accesses", tables);
        Assert.Contains("user_access_history", tables);
        Assert.Contains("system_configurations", tables);
        Assert.Contains("audit_logs", tables);
    }

    [Fact]
    public async Task Administration_ShouldContainExactlyFiveBaselineRoles()
    {
        await using var context = new AdministrationDbContext(
            new DbContextOptionsBuilder<AdministrationDbContext>()
                .UseNpgsql(_connectionString)
                .Options);

        var roles = await context.Roles
            .AsNoTracking()
            .OrderBy(r => r.Code)
            .Select(r => new { r.Id, r.Code, r.Name, r.IsActive })
            .ToListAsync();

        Assert.Equal(5, roles.Count);
        Assert.All(roles, role => Assert.True(role.IsActive));
        Assert.Contains(roles, role => role.Id == Guid.Parse("11111111-1111-4111-8111-111111111111") && role.Code == "RESIDENT" && role.Name == "Resident");
        Assert.Contains(roles, role => role.Id == Guid.Parse("22222222-2222-4222-8222-222222222222") && role.Code == "STAFF" && role.Name == "Staff");
        Assert.Contains(roles, role => role.Id == Guid.Parse("33333333-3333-4333-8333-333333333333") && role.Code == "ACCOUNTANT" && role.Name == "Accountant");
        Assert.Contains(roles, role => role.Id == Guid.Parse("44444444-4444-4444-8444-444444444444") && role.Code == "MANAGER" && role.Name == "Manager");
        Assert.Contains(roles, role => role.Id == Guid.Parse("55555555-5555-4555-8555-555555555555") && role.Code == "ADMIN" && role.Name == "Administrator");
    }

    [Fact]
    public async Task Administration_ShouldUseStableUniqueRoleCodes()
    {
        await using var context = new AdministrationDbContext(
            new DbContextOptionsBuilder<AdministrationDbContext>()
                .UseNpgsql(_connectionString)
                .Options);

        var roleCodes = await context.Roles
            .AsNoTracking()
            .Select(r => r.Code)
            .ToListAsync();

        Assert.Equal(roleCodes.Count, roleCodes.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(roleCodes.Count, roleCodes.Count(code => code == code.ToUpperInvariant()));
        Assert.DoesNotContain(roleCodes, code => code is not "RESIDENT" and not "STAFF" and not "ACCOUNTANT" and not "MANAGER" and not "ADMIN");
    }

    [Fact]
    public async Task Administration_ShouldHavePartialUniqueIndexForActiveUserBuildingAccess()
    {
        var options = new DbContextOptionsBuilder<AdministrationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new AdministrationDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'administration'
              AND tablename = 'user_building_accesses'
              AND indexdef ILIKE '%UNIQUE%'
              AND indexdef ILIKE '%user_id%'
              AND indexdef ILIKE '%building_id%'
              AND indexdef ILIKE '%revoked_at IS NULL%';";

        var indexDefinition = (string?)await cmd.ExecuteScalarAsync();

        Assert.NotNull(indexDefinition);
        Assert.Contains("WHERE (revoked_at IS NULL)", indexDefinition, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Administration_ShouldAllowRevokedAccessHistoryThenGrantAgain()
    {
        await using var context = new AdministrationDbContext(
            new DbContextOptionsBuilder<AdministrationDbContext>()
                .UseNpgsql(_connectionString)
                .Options);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var userId = Guid.NewGuid();
        var buildingId = Guid.NewGuid();
        var grantedAt = DateTimeOffset.UtcNow;
        var revokedAt = grantedAt.AddMinutes(5);

        await context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO administration.user_building_accesses
                (id, user_id, building_id, granted_at, revoked_at, created_at)
            VALUES
                ({Guid.NewGuid()}, {userId}, {buildingId}, {grantedAt}, {revokedAt}, {grantedAt});");

        await context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO administration.user_building_accesses
                (id, user_id, building_id, granted_at, created_at)
            VALUES
                ({Guid.NewGuid()}, {userId}, {buildingId}, {revokedAt.AddMinutes(1)}, {revokedAt.AddMinutes(1)});");

        var activeCount = await context.UserBuildingAccesses
            .CountAsync(access => access.UserId == userId && access.BuildingId == buildingId && access.RevokedAt == null);

        Assert.Equal(1, activeCount);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Administration_ShouldRejectTwoActiveAccessesForSameUserAndBuilding()
    {
        await using var context = new AdministrationDbContext(
            new DbContextOptionsBuilder<AdministrationDbContext>()
                .UseNpgsql(_connectionString)
                .Options);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var userId = Guid.NewGuid();
        var buildingId = Guid.NewGuid();
        var grantedAt = DateTimeOffset.UtcNow;

        await context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO administration.user_building_accesses
                (id, user_id, building_id, granted_at, created_at)
            VALUES
                ({Guid.NewGuid()}, {userId}, {buildingId}, {grantedAt}, {grantedAt});");

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO administration.user_building_accesses
                    (id, user_id, building_id, granted_at, created_at)
                VALUES
                    ({Guid.NewGuid()}, {userId}, {buildingId}, {grantedAt.AddMinutes(1)}, {grantedAt.AddMinutes(1)});"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Administration_ShouldAllowActiveAccessForDifferentUserOrBuilding()
    {
        await using var context = new AdministrationDbContext(
            new DbContextOptionsBuilder<AdministrationDbContext>()
                .UseNpgsql(_connectionString)
                .Options);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var buildingId = Guid.NewGuid();
        var otherBuildingId = Guid.NewGuid();
        var grantedAt = DateTimeOffset.UtcNow;

        await context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO administration.user_building_accesses
                (id, user_id, building_id, granted_at, created_at)
            VALUES
                ({Guid.NewGuid()}, {userId}, {buildingId}, {grantedAt}, {grantedAt});");

        await context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO administration.user_building_accesses
                (id, user_id, building_id, granted_at, created_at)
            VALUES
                ({Guid.NewGuid()}, {userId}, {otherBuildingId}, {grantedAt.AddMinutes(1)}, {grantedAt.AddMinutes(1)});");

        await context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO administration.user_building_accesses
                (id, user_id, building_id, granted_at, created_at)
            VALUES
                ({Guid.NewGuid()}, {otherUserId}, {buildingId}, {grantedAt.AddMinutes(2)}, {grantedAt.AddMinutes(2)});");

        var activeCount = await context.UserBuildingAccesses
            .CountAsync(access => access.RevokedAt == null &&
                (access.UserId == userId || access.UserId == otherUserId) &&
                (access.BuildingId == buildingId || access.BuildingId == otherBuildingId));

        Assert.Equal(3, activeCount);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Administration_ShouldNotContainSeededUsersPermissionsOrDemoAssignments()
    {
        await using var context = new AdministrationDbContext(
            new DbContextOptionsBuilder<AdministrationDbContext>()
                .UseNpgsql(_connectionString)
                .Options);

        Assert.Empty(await context.Permissions.AsNoTracking().ToListAsync());
        Assert.Empty(await context.RolePermissions.AsNoTracking().ToListAsync());
        Assert.Empty(await context.UserRoleAssignments.AsNoTracking().ToListAsync());
        Assert.Empty(await context.UserBuildingAccesses.AsNoTracking().ToListAsync());
        Assert.Empty(await context.UserAccessHistories.AsNoTracking().ToListAsync());
        Assert.Empty(await context.SystemConfigurations.AsNoTracking().ToListAsync());
        Assert.Empty(await context.AuditLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ServiceRequestsAndComplaints_ShouldNotContainDemoRows()
    {
        await using var serviceContext = new ServiceRequestsDbContext(
            new DbContextOptionsBuilder<ServiceRequestsDbContext>()
                .UseNpgsql(_connectionString)
                .Options);
        await using var complaintContext = new ComplaintsDbContext(
            new DbContextOptionsBuilder<ComplaintsDbContext>()
                .UseNpgsql(_connectionString)
                .Options);

        Assert.Empty(await serviceContext.ServiceRequestCategories.AsNoTracking().ToListAsync());
        Assert.Empty(await serviceContext.ServiceRequests.AsNoTracking().ToListAsync());
        Assert.Empty(await serviceContext.ServiceRequestAssignments.AsNoTracking().ToListAsync());
        Assert.Empty(await serviceContext.ServiceRequestActivities.AsNoTracking().ToListAsync());
        Assert.Empty(await complaintContext.Complaints.AsNoTracking().ToListAsync());
        Assert.Empty(await complaintContext.ComplaintFollowups.AsNoTracking().ToListAsync());
        Assert.Empty(await complaintContext.ComplaintActivities.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Administration_ShouldNotHaveCrossModulePhysicalForeignKeys()
    {
        var options = new DbContextOptionsBuilder<AdministrationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new AdministrationDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT 
                tc.table_schema, 
                tc.table_name, 
                ccu.table_schema AS foreign_table_schema,
                ccu.table_name AS foreign_table_name
            FROM information_schema.table_constraints AS tc
            JOIN information_schema.constraint_column_usage AS ccu
              ON ccu.constraint_name = tc.constraint_name
             AND ccu.table_schema = tc.table_schema
            WHERE tc.constraint_type = 'FOREIGN KEY'
              AND tc.table_schema = 'administration';";

        var crossFks = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var srcSchema = reader.GetString(0);
            var srcTable = reader.GetString(1);
            var foreignSchema = reader.GetString(2);
            var foreignTable = reader.GetString(3);

            if (srcSchema != foreignSchema)
            {
                crossFks.Add($"{srcSchema}.{srcTable} -> {foreignSchema}.{foreignTable}");
            }
        }

        Assert.Empty(crossFks);
    }

    [Fact]
    public async Task AuthenticationSchema_ShouldNotContainObsoletePhoneOrManualVerificationColumns()
    {
        var options = new DbContextOptionsBuilder<AuthenticationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new AuthenticationDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT table_name, column_name
            FROM information_schema.columns
            WHERE table_schema = 'auth'
              AND (
                    (table_name = 'users' AND column_name = 'phone_verified')
                 OR (table_name = 'resident_verifications' AND column_name IN ('method_code', 'reviewed_by', 'failure_reason', 'evidence_reference'))
              )
            ORDER BY table_name, column_name;";

        var obsoleteColumns = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            obsoleteColumns.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
        }

        Assert.Empty(obsoleteColumns);
    }

    [Fact]
    public async Task AuthenticationSchema_ShouldRequireEmailOtpChallengeFields()
    {
        var options = new DbContextOptionsBuilder<AuthenticationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new AuthenticationDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT column_name, is_nullable
            FROM information_schema.columns
            WHERE table_schema = 'auth'
              AND table_name = 'resident_verifications'
              AND column_name IN ('resident_id', 'verification_code_hash', 'expires_at');";

        var columns = new Dictionary<string, string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0), reader.GetString(1));
        }

        Assert.Equal("NO", columns["resident_id"]);
        Assert.Equal("NO", columns["verification_code_hash"]);
        Assert.Equal("NO", columns["expires_at"]);
    }

    [Fact]
    public async Task AuthenticationSchema_ShouldHaveCorrectiveMigrationInHistory()
    {
        var options = new DbContextOptionsBuilder<AuthenticationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new AuthenticationDbContext(options);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT ""MigrationId""
            FROM auth.""__EFMigrationsHistory""
            WHERE ""MigrationId"" LIKE '%CorrectAuthenticationEmailOtpVerification%';";

        var migrationId = (string?)await cmd.ExecuteScalarAsync();
        Assert.NotNull(migrationId);
    }

    [Fact]
    public void Api_DependencyInjection_ShouldResolveAllImplementedDbContexts()
    {
        using var scope = _factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<PropertyAssetsDbContext>());
        Assert.NotNull(scope.ServiceProvider.GetService<ApartmentsDbContext>());
        Assert.NotNull(scope.ServiceProvider.GetService<AuthenticationDbContext>());
        Assert.NotNull(scope.ServiceProvider.GetService<ResidentsDbContext>());
        Assert.NotNull(scope.ServiceProvider.GetService<AdministrationDbContext>());
        Assert.NotNull(scope.ServiceProvider.GetService<ServiceRequestsDbContext>());
        Assert.NotNull(scope.ServiceProvider.GetService<ComplaintsDbContext>());
        Assert.NotNull(scope.ServiceProvider.GetService<MaintenanceDbContext>());
        Assert.NotNull(scope.ServiceProvider.GetService<BillingDbContext>());
        Assert.NotNull(scope.ServiceProvider.GetService<PaymentsDbContext>());
        Assert.NotNull(scope.ServiceProvider.GetService<AiClassificationDbContext>());
        Assert.NotNull(scope.ServiceProvider.GetService<AiRecommendationDbContext>());
        Assert.NotNull(scope.ServiceProvider.GetService<CommunicationDbContext>());
    }

    [Fact]
    public async Task AuditData_ShouldNotHaveDuplicateActiveUserBuildingAccessRecords_InEitherDatabase()
    {
        var connStrTest = _connectionString;
        var connStrDev = _connectionString.Replace("Database=propflow_test", "Database=propflow", StringComparison.OrdinalIgnoreCase);

        foreach (var (dbName, connStr) in new[] { ("propflow_test", connStrTest), ("propflow", connStrDev) })
        {
            await using var conn = new Npgsql.NpgsqlConnection(connStr);
            await conn.OpenAsync();

            await using var cmdCheckTable = conn.CreateCommand();
            cmdCheckTable.CommandText = "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_schema = 'administration' AND table_name = 'user_building_accesses');";
            var tableExists = (bool)(await cmdCheckTable.ExecuteScalarAsync() ?? false);

            if (tableExists)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT user_id, building_id, COUNT(*) 
                    FROM administration.user_building_accesses 
                    WHERE revoked_at IS NULL 
                    GROUP BY user_id, building_id 
                    HAVING COUNT(*) > 1;";

                await using var reader = await cmd.ExecuteReaderAsync();
                var duplicates = new List<string>();
                while (await reader.ReadAsync())
                {
                    duplicates.Add($"[{dbName}] user_id: {reader[0]}, building_id: {reader[1]}, count: {reader[2]}");
                }

                Assert.Empty(duplicates);
            }
        }
    }

    private static async Task<Guid> InsertServiceRequestForConstraintTest(ServiceRequestsDbContext context, DateTimeOffset now)
    {
        var requestId = Guid.NewGuid();
        var requestNumber = $"SR-{Guid.NewGuid():N}"[..15];
        await context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO service_requests.service_requests
                (id, request_number, resident_id, resident_apartment_id, building_id, title, description, status, submitted_at, created_at, updated_at)
            VALUES
                ({requestId}, {requestNumber}, {Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()}, 'Constraint test', 'Constraint test request', 'ASSIGNED', {now}, {now}, {now});");

        return requestId;
    }

    private async Task AssertDuplicateAudienceRejected(
        string audienceType,
        Guid? roleId = null,
        Guid? buildingId = null,
        Guid? apartmentUnitId = null,
        Guid? residentId = null)
    {
        var options = new DbContextOptionsBuilder<CommunicationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var context = new CommunicationDbContext(options);
        await using var transaction = await context.Database.BeginTransactionAsync();
        var now = DateTimeOffset.UtcNow;
        var announcementId = Guid.NewGuid();
        await InsertAnnouncementForCommunicationAudienceTest(context, announcementId, now);
        await InsertAnnouncementAudienceForUniquenessTest(context, announcementId, audienceType, now, roleId, buildingId, apartmentUnitId, residentId);

        var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertAnnouncementAudienceForUniquenessTest(context, announcementId, audienceType, now, roleId, buildingId, apartmentUnitId, residentId));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
        await transaction.RollbackAsync();
    }

    private static Task InsertAnnouncementForCommunicationAudienceTest(
        CommunicationDbContext context,
        Guid announcementId,
        DateTimeOffset now)
    {
        return context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO communication.announcements
                (id, title, content, status, created_by, created_at, updated_at)
            VALUES
                ({announcementId}, 'Audience uniqueness test', 'Audience uniqueness test content', 'DRAFT', {Guid.NewGuid()}, {now}, {now});");
    }

    private static Task InsertAnnouncementAudienceForUniquenessTest(
        CommunicationDbContext context,
        Guid announcementId,
        string audienceType,
        DateTimeOffset now,
        Guid? roleId = null,
        Guid? buildingId = null,
        Guid? apartmentUnitId = null,
        Guid? residentId = null)
    {
        return context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO communication.announcement_audiences
                (id, announcement_id, audience_type, role_id, building_id, apartment_unit_id, resident_id, created_at)
            VALUES
                ({Guid.NewGuid()}, {announcementId}, {audienceType}, {roleId}, {buildingId}, {apartmentUnitId}, {residentId}, {now});");
    }

    private static async Task<Guid> InsertMaintenanceTaskForConstraintTest(MaintenanceDbContext context, DateTimeOffset now)
    {
        var taskId = Guid.NewGuid();
        var taskNumber = $"MT-{Guid.NewGuid():N}"[..15];
        await context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO maintenance.maintenance_tasks
                (id, task_number, building_id, title, status, created_by, created_at, updated_at)
            VALUES
                ({taskId}, {taskNumber}, {Guid.NewGuid()}, 'Constraint test', 'ASSIGNED', {Guid.NewGuid()}, {now}, {now});");

        return taskId;
    }

    private static async Task<Guid> InsertFeeTypeForConstraintTest(BillingDbContext context, DateTimeOffset now)
    {
        var feeTypeId = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO billing.fee_types
                (id, code, name, default_calculation_method_code, default_billing_frequency_code, status, created_by, created_at, updated_at)
            VALUES
                ({feeTypeId}, {$"FEE-{Guid.NewGuid():N}"[..20]}, 'Constraint fee', 'FIXED', 'MONTHLY', 'ACTIVE', {Guid.NewGuid()}, {now}, {now});");

        return feeTypeId;
    }
}

