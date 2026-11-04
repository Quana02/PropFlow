using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Payments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSePayDestinationIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "sepay_account_id",
                schema: "payments",
                table: "payment_destinations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_destinations_sepay_account_id",
                schema: "payments",
                table: "payment_destinations",
                column: "sepay_account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payment_destinations_sepay_account_id",
                schema: "payments",
                table: "payment_destinations");

            migrationBuilder.DropColumn(
                name: "sepay_account_id",
                schema: "payments",
                table: "payment_destinations");
        }
    }
}
