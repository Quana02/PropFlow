using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Communication.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CorrectCommunicationAudienceUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_announcement_audiences_announcement_apartment",
                schema: "communication",
                table: "announcement_audiences",
                columns: new[] { "announcement_id", "apartment_unit_id" },
                unique: true,
                filter: "audience_type = 'APARTMENT' AND apartment_unit_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_announcement_audiences_announcement_building",
                schema: "communication",
                table: "announcement_audiences",
                columns: new[] { "announcement_id", "building_id" },
                unique: true,
                filter: "audience_type = 'BUILDING' AND building_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_announcement_audiences_announcement_resident",
                schema: "communication",
                table: "announcement_audiences",
                columns: new[] { "announcement_id", "resident_id" },
                unique: true,
                filter: "audience_type = 'RESIDENT' AND resident_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_announcement_audiences_announcement_role",
                schema: "communication",
                table: "announcement_audiences",
                columns: new[] { "announcement_id", "role_id" },
                unique: true,
                filter: "audience_type = 'ROLE' AND role_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_announcement_audiences_announcement_type_global",
                schema: "communication",
                table: "announcement_audiences",
                columns: new[] { "announcement_id", "audience_type" },
                unique: true,
                filter: "audience_type IN ('ALL_USERS', 'ALL_RESIDENTS')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_announcement_audiences_announcement_apartment",
                schema: "communication",
                table: "announcement_audiences");

            migrationBuilder.DropIndex(
                name: "UX_announcement_audiences_announcement_building",
                schema: "communication",
                table: "announcement_audiences");

            migrationBuilder.DropIndex(
                name: "UX_announcement_audiences_announcement_resident",
                schema: "communication",
                table: "announcement_audiences");

            migrationBuilder.DropIndex(
                name: "UX_announcement_audiences_announcement_role",
                schema: "communication",
                table: "announcement_audiences");

            migrationBuilder.DropIndex(
                name: "UX_announcement_audiences_announcement_type_global",
                schema: "communication",
                table: "announcement_audiences");
        }
    }
}
