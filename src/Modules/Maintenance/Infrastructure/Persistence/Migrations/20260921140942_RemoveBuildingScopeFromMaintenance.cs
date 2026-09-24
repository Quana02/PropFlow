using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Maintenance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBuildingScopeFromMaintenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_maintenance_tasks_building_id",
                schema: "maintenance",
                table: "maintenance_tasks");

            migrationBuilder.DropIndex(
                name: "IX_maintenance_schedules_building_id",
                schema: "maintenance",
                table: "maintenance_schedules");

            migrationBuilder.DropColumn(
                name: "building_id",
                schema: "maintenance",
                table: "maintenance_tasks");

            migrationBuilder.DropColumn(
                name: "building_id",
                schema: "maintenance",
                table: "maintenance_schedules");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Add building_id as nullable first
            migrationBuilder.AddColumn<Guid>(
                name: "building_id",
                schema: "maintenance",
                table: "maintenance_tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "building_id",
                schema: "maintenance",
                table: "maintenance_schedules",
                type: "uuid",
                nullable: true);

            // Populate building_id with the singleton building from property_assets
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
                    
                    UPDATE maintenance.maintenance_tasks SET building_id = singleton_building_id WHERE building_id IS NULL;
                    UPDATE maintenance.maintenance_schedules SET building_id = singleton_building_id WHERE building_id IS NULL;
                END $$;
            ");

            // Now enforce NOT NULL
            migrationBuilder.AlterColumn<Guid>(
                name: "building_id",
                schema: "maintenance",
                table: "maintenance_tasks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "building_id",
                schema: "maintenance",
                table: "maintenance_schedules",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_tasks_building_id",
                schema: "maintenance",
                table: "maintenance_tasks",
                column: "building_id");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_schedules_building_id",
                schema: "maintenance",
                table: "maintenance_schedules",
                column: "building_id");
        }
    }
}
