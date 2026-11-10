using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Communication.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationSenderAndPagingIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "sender_user_id",
                schema: "communication",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_recipient_user_id_created_at_id",
                schema: "communication",
                table: "notifications",
                columns: new[] { "recipient_user_id", "created_at", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notifications_recipient_user_id_created_at_id",
                schema: "communication",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "sender_user_id",
                schema: "communication",
                table: "notifications");
        }
    }
}
