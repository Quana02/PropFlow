using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Communication.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBuildingAudienceFromCommunication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_announcement_audiences_building_id",
                schema: "communication",
                table: "announcement_audiences");

            migrationBuilder.DropIndex(
                name: "UX_announcement_audiences_announcement_building",
                schema: "communication",
                table: "announcement_audiences");

            migrationBuilder.DropCheckConstraint(
                name: "CK_announcement_audiences_target",
                schema: "communication",
                table: "announcement_audiences");

            migrationBuilder.DropColumn(
                name: "building_id",
                schema: "communication",
                table: "announcement_audiences");

            migrationBuilder.AddCheckConstraint(
                name: "CK_announcement_audiences_target",
                schema: "communication",
                table: "announcement_audiences",
                sql: "(audience_type IN ('ALL_USERS', 'ALL_RESIDENTS') AND role_id IS NULL AND apartment_unit_id IS NULL AND resident_id IS NULL) OR (audience_type = 'ROLE' AND role_id IS NOT NULL AND apartment_unit_id IS NULL AND resident_id IS NULL) OR (audience_type = 'APARTMENT' AND role_id IS NULL AND apartment_unit_id IS NOT NULL AND resident_id IS NULL) OR (audience_type = 'RESIDENT' AND role_id IS NULL AND apartment_unit_id IS NULL AND resident_id IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_announcement_audiences_target",
                schema: "communication",
                table: "announcement_audiences");

            migrationBuilder.AddColumn<Guid>(
                name: "building_id",
                schema: "communication",
                table: "announcement_audiences",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_announcement_audiences_building_id",
                schema: "communication",
                table: "announcement_audiences",
                column: "building_id");

            migrationBuilder.CreateIndex(
                name: "UX_announcement_audiences_announcement_building",
                schema: "communication",
                table: "announcement_audiences",
                columns: new[] { "announcement_id", "building_id" },
                unique: true,
                filter: "audience_type = 'BUILDING' AND building_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_announcement_audiences_target",
                schema: "communication",
                table: "announcement_audiences",
                sql: "(audience_type IN ('ALL_USERS', 'ALL_RESIDENTS') AND role_id IS NULL AND building_id IS NULL AND apartment_unit_id IS NULL AND resident_id IS NULL) OR (audience_type = 'ROLE' AND role_id IS NOT NULL AND building_id IS NULL AND apartment_unit_id IS NULL AND resident_id IS NULL) OR (audience_type = 'BUILDING' AND role_id IS NULL AND building_id IS NOT NULL AND apartment_unit_id IS NULL AND resident_id IS NULL) OR (audience_type = 'APARTMENT' AND role_id IS NULL AND building_id IS NULL AND apartment_unit_id IS NOT NULL AND resident_id IS NULL) OR (audience_type = 'RESIDENT' AND role_id IS NULL AND building_id IS NULL AND apartment_unit_id IS NULL AND resident_id IS NOT NULL)");
        }
    }
}
