using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Communication.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowFinanceEmailWithoutAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                schema: "communication",
                table: "finance_emails",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "IX_finance_emails_event_id_address",
                schema: "communication",
                table: "finance_emails",
                columns: new[] { "event_id", "address" },
                unique: true,
                filter: "user_id IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_finance_emails_event_id_address",
                schema: "communication",
                table: "finance_emails");

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                schema: "communication",
                table: "finance_emails",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
