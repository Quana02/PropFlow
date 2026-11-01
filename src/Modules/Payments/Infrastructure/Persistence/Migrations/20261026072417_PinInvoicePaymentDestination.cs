using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Payments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PinInvoicePaymentDestination : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_instructions",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_destination_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_instructions", x => x.id);
                    table.ForeignKey(
                        name: "FK_payment_instructions_payment_destinations_payment_destinati~",
                        column: x => x.payment_destination_id,
                        principalSchema: "payments",
                        principalTable: "payment_destinations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payment_instructions_invoice_id",
                schema: "payments",
                table: "payment_instructions",
                column: "invoice_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_instructions_payment_destination_id",
                schema: "payments",
                table: "payment_instructions",
                column: "payment_destination_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_instructions",
                schema: "payments");
        }
    }
}
