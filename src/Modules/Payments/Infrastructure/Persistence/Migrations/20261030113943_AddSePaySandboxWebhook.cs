using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Payments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSePaySandboxWebhook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProtectedWebhookKey",
                schema: "payments",
                table: "sepay_sandbox_connection",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountLast4",
                schema: "payments",
                table: "sandbox_payment_simulations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountName",
                schema: "payments",
                table: "sandbox_payment_simulations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankCode",
                schema: "payments",
                table: "sandbox_payment_simulations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankName",
                schema: "payments",
                table: "sandbox_payment_simulations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GatewayAliases",
                schema: "payments",
                table: "sandbox_payment_simulations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProtectedAccountNumber",
                schema: "payments",
                table: "sandbox_payment_simulations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RequestedAmount",
                schema: "payments",
                table: "sandbox_payment_simulations",
                type: "numeric(18,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "TransferCode",
                schema: "payments",
                table: "sandbox_payment_simulations",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VerifiedAmount",
                schema: "payments",
                table: "sandbox_payment_simulations",
                type: "numeric(18,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "sandbox_sepay_receipts",
                schema: "payments",
                columns: table => new
                {
                    TransactionId = table.Column<long>(type: "bigint", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProtectedPayload = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,0)", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Outcome = table.Column<string>(type: "text", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sandbox_sepay_receipts", x => x.TransactionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sandbox_payment_simulations_TransferCode",
                schema: "payments",
                table: "sandbox_payment_simulations",
                column: "TransferCode",
                unique: true,
                filter: "\"TransferCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sandbox_sepay_receipts_InvoiceId",
                schema: "payments",
                table: "sandbox_sepay_receipts",
                column: "InvoiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sandbox_sepay_receipts",
                schema: "payments");

            migrationBuilder.DropIndex(
                name: "IX_sandbox_payment_simulations_TransferCode",
                schema: "payments",
                table: "sandbox_payment_simulations");

            migrationBuilder.DropColumn(
                name: "ProtectedWebhookKey",
                schema: "payments",
                table: "sepay_sandbox_connection");

            migrationBuilder.DropColumn(
                name: "AccountLast4",
                schema: "payments",
                table: "sandbox_payment_simulations");

            migrationBuilder.DropColumn(
                name: "AccountName",
                schema: "payments",
                table: "sandbox_payment_simulations");

            migrationBuilder.DropColumn(
                name: "BankCode",
                schema: "payments",
                table: "sandbox_payment_simulations");

            migrationBuilder.DropColumn(
                name: "BankName",
                schema: "payments",
                table: "sandbox_payment_simulations");

            migrationBuilder.DropColumn(
                name: "GatewayAliases",
                schema: "payments",
                table: "sandbox_payment_simulations");

            migrationBuilder.DropColumn(
                name: "ProtectedAccountNumber",
                schema: "payments",
                table: "sandbox_payment_simulations");

            migrationBuilder.DropColumn(
                name: "RequestedAmount",
                schema: "payments",
                table: "sandbox_payment_simulations");

            migrationBuilder.DropColumn(
                name: "TransferCode",
                schema: "payments",
                table: "sandbox_payment_simulations");

            migrationBuilder.DropColumn(
                name: "VerifiedAmount",
                schema: "payments",
                table: "sandbox_payment_simulations");
        }
    }
}
