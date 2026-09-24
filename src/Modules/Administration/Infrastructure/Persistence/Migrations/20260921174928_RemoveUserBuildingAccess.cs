using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Administration.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUserBuildingAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_building_accesses",
                schema: "administration");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_building_accesses",
                schema: "administration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    building_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_building_accesses", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_building_accesses_building_id",
                schema: "administration",
                table: "user_building_accesses",
                column: "building_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_building_accesses_user_id",
                schema: "administration",
                table: "user_building_accesses",
                column: "user_id");

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
    }
}
