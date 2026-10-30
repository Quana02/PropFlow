using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Payments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSePayTransferInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bank_transfers",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<long>(type: "bigint", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    protected_payload = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,0)", nullable: false),
                    gateway = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    account_last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    transfer_code = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    bank_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    outcome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    allocated_amount = table.Column<decimal>(type: "numeric(18,0)", nullable: false),
                    assigned_invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_amount = table.Column<decimal>(type: "numeric(18,0)", nullable: true),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    review_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    review_history = table.Column<string>(type: "text", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_transfers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bank_transfers_invoice_id",
                schema: "payments",
                table: "bank_transfers",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "IX_bank_transfers_status_next_attempt_at",
                schema: "payments",
                table: "bank_transfers",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_transfers_transaction_id",
                schema: "payments",
                table: "bank_transfers",
                column: "transaction_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_transfers",
                schema: "payments");
        }
    }
}
