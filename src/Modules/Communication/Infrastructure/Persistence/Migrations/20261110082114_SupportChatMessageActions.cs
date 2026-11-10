using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Communication.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupportChatMessageActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeliveredAt",
                schema: "communication",
                table: "support_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ForwardedFromId",
                schema: "communication",
                table: "support_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HiddenForManager",
                schema: "communication",
                table: "support_messages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HiddenForResident",
                schema: "communication",
                table: "support_messages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RecalledAt",
                schema: "communication",
                table: "support_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplyToId",
                schema: "communication",
                table: "support_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SeenAt",
                schema: "communication",
                table: "support_messages",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveredAt",
                schema: "communication",
                table: "support_messages");

            migrationBuilder.DropColumn(
                name: "ForwardedFromId",
                schema: "communication",
                table: "support_messages");

            migrationBuilder.DropColumn(
                name: "HiddenForManager",
                schema: "communication",
                table: "support_messages");

            migrationBuilder.DropColumn(
                name: "HiddenForResident",
                schema: "communication",
                table: "support_messages");

            migrationBuilder.DropColumn(
                name: "RecalledAt",
                schema: "communication",
                table: "support_messages");

            migrationBuilder.DropColumn(
                name: "ReplyToId",
                schema: "communication",
                table: "support_messages");

            migrationBuilder.DropColumn(
                name: "SeenAt",
                schema: "communication",
                table: "support_messages");
        }
    }
}
