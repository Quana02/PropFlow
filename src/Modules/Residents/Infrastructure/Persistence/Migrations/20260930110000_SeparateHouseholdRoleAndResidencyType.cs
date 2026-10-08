using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PropFlow.Modules.Residents.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ResidentsDbContext))]
[Migration("20260930110000_SeparateHouseholdRoleAndResidencyType")]
public sealed class SeparateHouseholdRoleAndResidencyType : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Legacy rows do not contain sufficient evidence to infer whether they are owner-occupied,
        // tenant, or authorized occupancy. Stop safely instead of manufacturing ownership semantics.
        migrationBuilder.Sql("""
            DO $$ BEGIN
              IF EXISTS (SELECT 1 FROM residents.resident_apartments) THEN
                RAISE EXCEPTION 'Resident residency data requires manual residency_type migration; no legacy role is inferred automatically.';
              END IF;
            END $$;
            """);
        migrationBuilder.Sql("ALTER TABLE residents.resident_apartments DROP CONSTRAINT IF EXISTS ck_household_role_fields;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS residents.ux_active_household_head_per_apartment;");
        migrationBuilder.RenameColumn("residency_role", "resident_apartments", "household_role", schema: "residents");
        migrationBuilder.AddColumn<string>("residency_type", "resident_apartments", schema: "residents", type: "character varying(30)", maxLength: 30, nullable: false);
        migrationBuilder.Sql("ALTER TABLE residents.resident_apartments ADD CONSTRAINT ck_household_role_fields CHECK ((household_role = 'HOUSEHOLD_MEMBER' AND household_head_residency_id IS NOT NULL AND relationship_to_head IS NOT NULL) OR (household_role = 'HOUSEHOLD_HEAD' AND household_head_residency_id IS NULL AND relationship_to_head IS NULL));");
        migrationBuilder.Sql("CREATE UNIQUE INDEX ux_active_household_head_per_apartment ON residents.resident_apartments(apartment_unit_id) WHERE status = 'ACTIVE' AND end_date IS NULL AND household_role = 'HOUSEHOLD_HEAD';");
        migrationBuilder.Sql("CREATE INDEX IX_resident_apartments_apartment_unit_id_household_role_status ON residents.resident_apartments(apartment_unit_id, household_role, status);");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE residents.resident_apartments DROP CONSTRAINT IF EXISTS ck_household_role_fields;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS residents.ux_active_household_head_per_apartment; DROP INDEX IF EXISTS residents.\"IX_resident_apartments_apartment_unit_id_household_role_status\";");
        migrationBuilder.DropColumn("residency_type", "resident_apartments", schema: "residents");
        migrationBuilder.RenameColumn("household_role", "resident_apartments", "residency_role", schema: "residents");
    }
}
