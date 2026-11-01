using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.Migrations;

/// <summary>Expands PropertyAssets with Facility Type master data and preserves the legacy text column for staged cleanup.</summary>
[Migration("20261013110000_AddFacilityTypes")]
[DbContext(typeof(PropertyAssetsDbContext))]
public partial class AddFacilityTypes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "facility_types", schema: "property_assets",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            }, constraints: table => table.PrimaryKey("PK_facility_types", x => x.id));

        migrationBuilder.Sql("CREATE UNIQUE INDEX \"UX_facility_types_normalized_name\" ON property_assets.facility_types (lower(btrim(name))); ");
        migrationBuilder.CreateIndex(name: "IX_facility_types_is_active", schema: "property_assets", table: "facility_types", column: "is_active");
        migrationBuilder.AddColumn<Guid>(name: "facility_type_id", schema: "property_assets", table: "facilities", type: "uuid", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_facilities_facility_type_id", schema: "property_assets", table: "facilities", column: "facility_type_id");
        migrationBuilder.AddForeignKey(name: "FK_facilities_facility_types_facility_type_id", schema: "property_assets", table: "facilities", column: "facility_type_id", principalSchema: "property_assets", principalTable: "facility_types", principalColumn: "id", onDelete: ReferentialAction.Restrict);

        // NULL, empty and whitespace-only legacy values intentionally remain untyped.
        // lower(btrim(...)) groups differences in case and leading/trailing whitespace.
        migrationBuilder.Sql(@"
            INSERT INTO property_assets.facility_types (id, name, is_active, created_at, updated_at)
            SELECT gen_random_uuid(), min(btrim(f.facility_type)), true, now(), now()
            FROM property_assets.facilities f
            WHERE f.facility_type IS NOT NULL AND btrim(f.facility_type) <> ''
            GROUP BY lower(btrim(f.facility_type));

            UPDATE property_assets.facilities f
            SET facility_type_id = ft.id
            FROM property_assets.facility_types ft
            WHERE f.facility_type IS NOT NULL
              AND btrim(f.facility_type) <> ''
              AND lower(btrim(f.facility_type)) = lower(btrim(ft.name));
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_facilities_facility_types_facility_type_id", schema: "property_assets", table: "facilities");
        migrationBuilder.DropIndex(name: "IX_facilities_facility_type_id", schema: "property_assets", table: "facilities");
        migrationBuilder.DropColumn(name: "facility_type_id", schema: "property_assets", table: "facilities");
        migrationBuilder.DropTable(name: "facility_types", schema: "property_assets");
    }
}
