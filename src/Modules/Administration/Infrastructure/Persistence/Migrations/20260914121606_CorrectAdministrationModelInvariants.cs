using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Administration.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CorrectAdministrationModelInvariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_building_accesses_user_id_building_id_granted_at",
                schema: "administration",
                table: "user_building_accesses");

            migrationBuilder.CreateIndex(
                name: "IX_user_building_accesses_user_id_building_id",
                schema: "administration",
                table: "user_building_accesses",
                columns: new[] { "user_id", "building_id" },
                unique: true,
                filter: "\"revoked_at\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_user_building_accesses_user_id_building_id_granted_at",
                schema: "administration",
                table: "user_building_accesses",
                columns: new[] { "user_id", "building_id", "granted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_building_accesses_user_id_building_id",
                schema: "administration",
                table: "user_building_accesses");

            migrationBuilder.DropIndex(
                name: "IX_user_building_accesses_user_id_building_id_granted_at",
                schema: "administration",
                table: "user_building_accesses");

            migrationBuilder.CreateIndex(
                name: "IX_user_building_accesses_user_id_building_id_granted_at",
                schema: "administration",
                table: "user_building_accesses",
                columns: new[] { "user_id", "building_id", "granted_at" },
                unique: true);
        }
    }
}
