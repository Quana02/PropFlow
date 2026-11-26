using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceRequestInvoiceRounds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_invoices_source_service_request_id",
                schema: "billing",
                table: "invoices");

            migrationBuilder.AddColumn<int>(
                name: "source_service_request_round",
                schema: "billing",
                table: "invoices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_invoices_source_service_request_id_source_service_request_r~",
                schema: "billing",
                table: "invoices",
                columns: new[] { "source_service_request_id", "source_service_request_round" },
                unique: true,
                filter: "source_service_request_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_invoices_source_service_request_id_source_service_request_r~",
                schema: "billing",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "source_service_request_round",
                schema: "billing",
                table: "invoices");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_source_service_request_id",
                schema: "billing",
                table: "invoices",
                column: "source_service_request_id",
                unique: true,
                filter: "source_service_request_id IS NOT NULL");
        }
    }
}
