using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Communication.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmergencyVoiceCalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "emergency_calls",
                schema: "communication",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Apartment = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ResidentConnectionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ManagerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ManagerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ManagerConnectionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    ResidentMediaConnected = table.Column<bool>(type: "boolean", nullable: false),
                    ManagerMediaConnected = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConnectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_emergency_calls", x => x.Id);
                    table.CheckConstraint("ck_emergency_call_state", "\"Status\" IN ('WAITING','CONNECTING','CONNECTED','ENDED') AND ((\"Status\" = 'ENDED') = (\"EndedAt\" IS NOT NULL)) AND (\"Status\" NOT IN ('CONNECTING','CONNECTED') OR (\"ManagerUserId\" IS NOT NULL AND \"ManagerConnectionId\" IS NOT NULL)) AND \"Revision\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "emergency_connections",
                schema: "communication",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_emergency_connections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_emergency_calls_ManagerUserId",
                schema: "communication",
                table: "emergency_calls",
                column: "ManagerUserId",
                unique: true,
                filter: "\"Status\" IN ('CONNECTING','CONNECTED')");

            migrationBuilder.CreateIndex(
                name: "IX_emergency_calls_ResidentUserId",
                schema: "communication",
                table: "emergency_calls",
                column: "ResidentUserId",
                unique: true,
                filter: "\"Status\" <> 'ENDED'");

            migrationBuilder.CreateIndex(
                name: "IX_emergency_calls_ResidentUserId_ClientRequestId",
                schema: "communication",
                table: "emergency_calls",
                columns: new[] { "ResidentUserId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_emergency_calls_Status_CreatedAt",
                schema: "communication",
                table: "emergency_calls",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_emergency_connections_UserId_ExpiresAt",
                schema: "communication",
                table: "emergency_connections",
                columns: new[] { "UserId", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "emergency_calls",
                schema: "communication");

            migrationBuilder.DropTable(
                name: "emergency_connections",
                schema: "communication");
        }
    }
}
