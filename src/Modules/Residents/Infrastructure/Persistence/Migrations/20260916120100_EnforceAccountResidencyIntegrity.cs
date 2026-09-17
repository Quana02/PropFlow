using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PropFlow.Modules.Residents.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ResidentsDbContext))]
[Migration("20260916120100_EnforceAccountResidencyIntegrity")]
public sealed class EnforceAccountResidencyIntegrity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddForeignKey("FK_resident_account", "residents", "user_id", "users", principalSchema: "auth", schema: "residents", principalColumn: "id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey("FK_residency_apartment", "resident_apartments", "apartment_unit_id", "apartment_units", principalSchema: "apartments", schema: "residents", principalColumn: "id", onDelete: ReferentialAction.Restrict);
        foreach (var table in new[] { "residents", "resident_apartments" })
            foreach (var column in new[] { "created_by", "updated_by" })
                migrationBuilder.AddForeignKey($"FK_{table}_{column}", table, column, "users", principalSchema: "auth", schema: "residents", principalColumn: "id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.Sql("""
            CREATE EXTENSION IF NOT EXISTS btree_gist;
            ALTER TABLE residents.resident_apartments ADD CONSTRAINT ck_residency_dates CHECK (end_date IS NULL OR end_date >= start_date);
            ALTER TABLE residents.resident_apartments ADD CONSTRAINT ex_active_residency_overlap
            EXCLUDE USING gist (resident_id WITH =, apartment_unit_id WITH =, daterange(start_date, end_date, '[]') WITH &&)
            WHERE (status = 'ACTIVE');
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE residents.resident_apartments DROP CONSTRAINT ex_active_residency_overlap; ALTER TABLE residents.resident_apartments DROP CONSTRAINT ck_residency_dates;");
        foreach (var table in new[] { "residents", "resident_apartments" })
            foreach (var column in new[] { "created_by", "updated_by" }) migrationBuilder.DropForeignKey($"FK_{table}_{column}", table, "residents");
        migrationBuilder.DropForeignKey("FK_residency_apartment", "resident_apartments", "residents");
        migrationBuilder.DropForeignKey("FK_resident_account", "residents", "residents");
    }
}
