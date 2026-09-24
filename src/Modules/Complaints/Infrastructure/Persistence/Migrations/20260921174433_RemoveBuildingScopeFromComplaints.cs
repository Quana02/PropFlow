using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Complaints.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBuildingScopeFromComplaints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_complaints_building_id",
                schema: "complaints",
                table: "complaints");

            migrationBuilder.DropColumn(
                name: "building_id",
                schema: "complaints",
                table: "complaints");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "building_id",
                schema: "complaints",
                table: "complaints",
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

                    UPDATE complaints.complaints
                    SET building_id = singleton_building_id
                    WHERE building_id IS NULL;

                    ALTER TABLE complaints.complaints
                    ALTER COLUMN building_id SET NOT NULL;
                END $$;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_complaints_building_id",
                schema: "complaints",
                table: "complaints",
                column: "building_id");
        }
    }
}
