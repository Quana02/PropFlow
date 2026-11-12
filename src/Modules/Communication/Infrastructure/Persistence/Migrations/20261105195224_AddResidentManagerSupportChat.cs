using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Communication.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResidentManagerSupportChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "support_conversations",
                schema: "communication",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ManagerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ManagerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_conversations", x => x.Id);
                    table.CheckConstraint("ck_support_state", "\"Status\" IN ('WAITING','ACTIVE','CLOSED') AND ((\"Status\" = 'CLOSED') = (\"ClosedAt\" IS NOT NULL)) AND (\"Status\" <> 'ACTIVE' OR \"ManagerUserId\" IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "support_manager_connections",
                schema: "communication",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ManagerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_manager_connections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "support_manager_presence",
                schema: "communication",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Available = table.Column<bool>(type: "boolean", nullable: false),
                    LastAssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_manager_presence", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "support_messages",
                schema: "communication",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SenderName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ClientMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_messages", x => x.Id);
                    table.CheckConstraint("ck_support_message", "length(btrim(\"Text\")) BETWEEN 1 AND 4000 AND \"Sequence\" > 0 AND \"Kind\" IN ('SYSTEM','RESIDENT','MANAGER') AND ((\"Kind\" = 'SYSTEM') = (\"SenderUserId\" IS NULL))");
                    table.ForeignKey(
                        name: "FK_support_messages_support_conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "communication",
                        principalTable: "support_conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_support_conversations_ManagerUserId",
                schema: "communication",
                table: "support_conversations",
                column: "ManagerUserId",
                unique: true,
                filter: "\"Status\" = 'ACTIVE'");

            migrationBuilder.CreateIndex(
                name: "IX_support_conversations_ManagerUserId_UpdatedAt",
                schema: "communication",
                table: "support_conversations",
                columns: new[] { "ManagerUserId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_support_conversations_ResidentUserId",
                schema: "communication",
                table: "support_conversations",
                column: "ResidentUserId",
                unique: true,
                filter: "\"Status\" IN ('WAITING','ACTIVE')");

            migrationBuilder.CreateIndex(
                name: "IX_support_conversations_Status_CreatedAt",
                schema: "communication",
                table: "support_conversations",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_support_manager_connections_ManagerUserId_ExpiresAt",
                schema: "communication",
                table: "support_manager_connections",
                columns: new[] { "ManagerUserId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_support_messages_ConversationId_Sequence",
                schema: "communication",
                table: "support_messages",
                columns: new[] { "ConversationId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_support_messages_SenderUserId_ClientMessageId",
                schema: "communication",
                table: "support_messages",
                columns: new[] { "SenderUserId", "ClientMessageId" },
                unique: true,
                filter: "\"SenderUserId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "support_manager_connections",
                schema: "communication");

            migrationBuilder.DropTable(
                name: "support_manager_presence",
                schema: "communication");

            migrationBuilder.DropTable(
                name: "support_messages",
                schema: "communication");

            migrationBuilder.DropTable(
                name: "support_conversations",
                schema: "communication");
        }
    }
}
