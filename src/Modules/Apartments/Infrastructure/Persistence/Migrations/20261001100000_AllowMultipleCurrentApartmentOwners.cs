using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PropFlow.Modules.Apartments.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ApartmentsDbContext))]
[Migration("20261001100000_AllowMultipleCurrentApartmentOwners")]
public sealed class AllowMultipleCurrentApartmentOwners : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS apartments.ux_apartment_ownership_current;");
        migrationBuilder.DropIndex("IX_apartment_ownerships_apartment_unit_id_start_date", "apartment_ownerships", schema: "apartments");
        migrationBuilder.CreateIndex("IX_apartment_ownerships_apartment_unit_id_start_date", "apartment_ownerships", new[] { "apartment_unit_id", "start_date" }, schema: "apartments");
        migrationBuilder.Sql("""
            DO $$ BEGIN
              IF EXISTS (SELECT 1 FROM apartments.apartment_ownerships WHERE end_date IS NULL GROUP BY apartment_unit_id, owner_resident_id HAVING COUNT(*) > 1) THEN
                RAISE EXCEPTION 'Duplicate current apartment ownership rows require data cleanup before migration.';
              END IF;
            END $$;
            """);
        migrationBuilder.Sql("CREATE UNIQUE INDEX ux_apartment_ownership_current_owner ON apartments.apartment_ownerships(apartment_unit_id, owner_resident_id) WHERE end_date IS NULL;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS apartments.ux_apartment_ownership_current_owner;");
        migrationBuilder.Sql("CREATE UNIQUE INDEX ux_apartment_ownership_current ON apartments.apartment_ownerships(apartment_unit_id) WHERE end_date IS NULL;");
        migrationBuilder.DropIndex("IX_apartment_ownerships_apartment_unit_id_start_date", "apartment_ownerships", schema: "apartments");
        migrationBuilder.CreateIndex("IX_apartment_ownerships_apartment_unit_id_start_date", "apartment_ownerships", new[] { "apartment_unit_id", "start_date" }, schema: "apartments", unique: true);
    }
}
