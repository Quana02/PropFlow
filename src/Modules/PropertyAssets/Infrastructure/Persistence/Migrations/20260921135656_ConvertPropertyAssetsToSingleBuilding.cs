using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConvertPropertyAssetsToSingleBuilding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_equipment_buildings_building_id",
                schema: "property_assets",
                table: "equipment");

            migrationBuilder.DropForeignKey(
                name: "FK_facilities_buildings_building_id",
                schema: "property_assets",
                table: "facilities");

            migrationBuilder.DropIndex(
                name: "IX_facilities_building_id",
                schema: "property_assets",
                table: "facilities");

            migrationBuilder.DropIndex(
                name: "IX_facilities_building_id_code",
                schema: "property_assets",
                table: "facilities");

            migrationBuilder.DropIndex(
                name: "IX_equipment_building_id_code",
                schema: "property_assets",
                table: "equipment");

            migrationBuilder.DropColumn(
                name: "building_id",
                schema: "property_assets",
                table: "facilities");

            migrationBuilder.DropColumn(
                name: "building_id",
                schema: "property_assets",
                table: "equipment");

            migrationBuilder.CreateIndex(
                name: "IX_facilities_code",
                schema: "property_assets",
                table: "facilities",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_equipment_code",
                schema: "property_assets",
                table: "equipment",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_facilities_code",
                schema: "property_assets",
                table: "facilities");

            migrationBuilder.DropIndex(
                name: "IX_equipment_code",
                schema: "property_assets",
                table: "equipment");

            // Add building_id as nullable first
            migrationBuilder.AddColumn<Guid>(
                name: "building_id",
                schema: "property_assets",
                table: "facilities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "building_id",
                schema: "property_assets",
                table: "equipment",
                type: "uuid",
                nullable: true);

            // Populate building_id with the singleton building
            // Fail if there is not exactly one building
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    building_count int;
                    singleton_building_id uuid;
                BEGIN
                    SELECT COUNT(*)
                    INTO building_count
                    FROM property_assets.buildings;
                    
                    IF building_count <> 1 THEN
                        RAISE EXCEPTION 'Cannot rollback: Expected exactly 1 building, found %', building_count;
                    END IF;
                    
                    SELECT id
                    INTO singleton_building_id
                    FROM property_assets.buildings
                    LIMIT 1;
                    
                    UPDATE property_assets.facilities SET building_id = singleton_building_id WHERE building_id IS NULL;
                    UPDATE property_assets.equipment SET building_id = singleton_building_id WHERE building_id IS NULL;
                END $$;
            ");

            // Now enforce NOT NULL
            migrationBuilder.AlterColumn<Guid>(
                name: "building_id",
                schema: "property_assets",
                table: "facilities",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "building_id",
                schema: "property_assets",
                table: "equipment",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_facilities_building_id",
                schema: "property_assets",
                table: "facilities",
                column: "building_id");

            migrationBuilder.CreateIndex(
                name: "IX_facilities_building_id_code",
                schema: "property_assets",
                table: "facilities",
                columns: new[] { "building_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_equipment_building_id_code",
                schema: "property_assets",
                table: "equipment",
                columns: new[] { "building_id", "code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_equipment_buildings_building_id",
                schema: "property_assets",
                table: "equipment",
                column: "building_id",
                principalSchema: "property_assets",
                principalTable: "buildings",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_facilities_buildings_building_id",
                schema: "property_assets",
                table: "facilities",
                column: "building_id",
                principalSchema: "property_assets",
                principalTable: "buildings",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
