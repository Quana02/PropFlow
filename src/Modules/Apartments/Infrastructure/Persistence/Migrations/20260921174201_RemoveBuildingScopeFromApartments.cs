using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Apartments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBuildingScopeFromApartments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_apartment_units_building_id_unit_number",
                schema: "apartments",
                table: "apartment_units");

            migrationBuilder.DropColumn(
                name: "building_id",
                schema: "apartments",
                table: "apartment_units");

            migrationBuilder.CreateIndex(
                name: "IX_apartment_units_unit_number",
                schema: "apartments",
                table: "apartment_units",
                column: "unit_number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_apartment_units_unit_number",
                schema: "apartments",
                table: "apartment_units");

            migrationBuilder.AddColumn<Guid>(
                name: "building_id",
                schema: "apartments",
                table: "apartment_units",
                type: "uuid",
                nullable: true);

            // Validate singleton building exists and get its ID
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    building_count integer;
                    singleton_building_id uuid;
                BEGIN
                    SELECT COUNT(*)
                    INTO building_count
                    FROM property_assets.buildings;

                    IF building_count <> 1 THEN
                        RAISE EXCEPTION
                            'Rollback requires exactly one building, found %',
                            building_count;
                    END IF;

                    SELECT id
                    INTO singleton_building_id
                    FROM property_assets.buildings
                    LIMIT 1;

                    UPDATE apartments.apartment_units
                    SET building_id = singleton_building_id
                    WHERE building_id IS NULL;

                    ALTER TABLE apartments.apartment_units
                    ALTER COLUMN building_id SET NOT NULL;
                END $$;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_apartment_units_building_id_unit_number",
                schema: "apartments",
                table: "apartment_units",
                columns: new[] { "building_id", "unit_number" },
                unique: true);
        }
    }
}
