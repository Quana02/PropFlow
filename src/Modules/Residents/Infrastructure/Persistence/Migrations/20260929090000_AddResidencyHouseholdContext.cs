using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PropFlow.Modules.Residents.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ResidentsDbContext))]
[Migration("20260929090000_AddResidencyHouseholdContext")]
public sealed class AddResidencyHouseholdContext : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn("relationship_type_code", "resident_apartments", "residency_role", schema: "residents");
        migrationBuilder.AlterColumn<string>("residency_role", "resident_apartments", schema: "residents", type: "character varying(30)", maxLength: 30, nullable: false, oldClrType: typeof(string), oldType: "character varying(50)", oldMaxLength: 50);
        migrationBuilder.Sql("UPDATE residents.resident_apartments SET residency_role = CASE residency_role WHEN 'OWNER' THEN 'HOUSEHOLD_HEAD' ELSE 'TENANT' END;");
        migrationBuilder.DropColumn("is_primary", "resident_apartments", schema: "residents");
        migrationBuilder.AddColumn<Guid>("household_head_residency_id", "resident_apartments", schema: "residents", nullable: true);
        migrationBuilder.AddColumn<string>("relationship_to_head", "resident_apartments", schema: "residents", type: "character varying(30)", maxLength: 30, nullable: true);
        migrationBuilder.AddForeignKey("FK_resident_apartment_household_head", "resident_apartments", "household_head_residency_id", "resident_apartments", principalSchema: "residents", schema: "residents", principalColumn: "id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.CreateIndex("IX_resident_apartments_household_head_residency_id", "resident_apartments", "household_head_residency_id", schema: "residents");
        migrationBuilder.CreateIndex("IX_resident_apartments_apartment_unit_id_residency_role_status", "resident_apartments", new[] { "apartment_unit_id", "residency_role", "status" }, schema: "residents");
        migrationBuilder.Sql("CREATE UNIQUE INDEX ux_active_household_head_per_apartment ON residents.resident_apartments(apartment_unit_id) WHERE status = 'ACTIVE' AND end_date IS NULL AND residency_role = 'HOUSEHOLD_HEAD';");
        migrationBuilder.Sql("ALTER TABLE residents.resident_apartments ADD CONSTRAINT ck_household_role_fields CHECK ((residency_role = 'HOUSEHOLD_MEMBER' AND household_head_residency_id IS NOT NULL AND relationship_to_head IS NOT NULL) OR (residency_role <> 'HOUSEHOLD_MEMBER' AND household_head_residency_id IS NULL AND relationship_to_head IS NULL));");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE residents.resident_apartments DROP CONSTRAINT ck_household_role_fields; DROP INDEX residents.ux_active_household_head_per_apartment;");
        migrationBuilder.DropForeignKey("FK_resident_apartment_household_head", "resident_apartments", schema: "residents");
        migrationBuilder.DropIndex("IX_resident_apartments_household_head_residency_id", "resident_apartments", schema: "residents");
        migrationBuilder.DropIndex("IX_resident_apartments_apartment_unit_id_residency_role_status", "resident_apartments", schema: "residents");
        migrationBuilder.DropColumn("household_head_residency_id", "resident_apartments", schema: "residents"); migrationBuilder.DropColumn("relationship_to_head", "resident_apartments", schema: "residents");
        migrationBuilder.AddColumn<bool>("is_primary", "resident_apartments", schema: "residents", nullable: false, defaultValue: false);
        migrationBuilder.RenameColumn("residency_role", "resident_apartments", "relationship_type_code", schema: "residents");
    }
}
