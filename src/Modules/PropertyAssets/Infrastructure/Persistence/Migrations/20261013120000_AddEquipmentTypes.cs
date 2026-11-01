using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.Migrations;

/// <summary>Expands PropertyAssets with Equipment Type master data and preserves the legacy text column for staged cleanup.</summary>
[Migration("20261013120000_AddEquipmentTypes")]
[DbContext(typeof(PropertyAssetsDbContext))]
public partial class AddEquipmentTypes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "equipment_types", schema: "property_assets",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            }, constraints: table => table.PrimaryKey("PK_equipment_types", x => x.id));

        migrationBuilder.Sql("CREATE UNIQUE INDEX \"UX_equipment_types_normalized_name\" ON property_assets.equipment_types (lower(btrim(name))); ");
        migrationBuilder.CreateIndex(name: "IX_equipment_types_is_active", schema: "property_assets", table: "equipment_types", column: "is_active");
        migrationBuilder.AddColumn<Guid>(name: "equipment_type_id", schema: "property_assets", table: "equipment", type: "uuid", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_equipment_equipment_type_id", schema: "property_assets", table: "equipment", column: "equipment_type_id");
        migrationBuilder.AddForeignKey(name: "FK_equipment_equipment_types_equipment_type_id", schema: "property_assets", table: "equipment", column: "equipment_type_id", principalSchema: "property_assets", principalTable: "equipment_types", principalColumn: "id", onDelete: ReferentialAction.Restrict);

        // NULL, empty and whitespace-only legacy values intentionally remain untyped.
        // lower(btrim(...)) groups differences in case and leading/trailing whitespace.
        migrationBuilder.Sql(@"
            INSERT INTO property_assets.equipment_types (id, name, is_active, created_at, updated_at)
            SELECT gen_random_uuid(), min(btrim(e.equipment_type)), true, now(), now()
            FROM property_assets.equipment e
            WHERE e.equipment_type IS NOT NULL AND btrim(e.equipment_type) <> ''
            GROUP BY lower(btrim(e.equipment_type));

            UPDATE property_assets.equipment e
            SET equipment_type_id = et.id
            FROM property_assets.equipment_types et
            WHERE e.equipment_type IS NOT NULL
              AND btrim(e.equipment_type) <> ''
              AND lower(btrim(e.equipment_type)) = lower(btrim(et.name));
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_equipment_equipment_types_equipment_type_id", schema: "property_assets", table: "equipment");
        migrationBuilder.DropIndex(name: "IX_equipment_equipment_type_id", schema: "property_assets", table: "equipment");
        migrationBuilder.DropColumn(name: "equipment_type_id", schema: "property_assets", table: "equipment");
        migrationBuilder.DropTable(name: "equipment_types", schema: "property_assets");
    }
}
