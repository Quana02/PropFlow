using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceRequestInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_invoices_apartment_unit_id_billing_period_start_billing_per~",
                schema: "billing",
                table: "invoices");

            migrationBuilder.AddColumn<Guid>(
                name: "source_service_request_id",
                schema: "billing",
                table: "invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoices_apartment_unit_id_billing_period_start_billing_per~",
                schema: "billing",
                table: "invoices",
                columns: new[] { "apartment_unit_id", "billing_period_start", "billing_period_end", "invoice_type" },
                unique: true,
                filter: "invoice_type <> 'SERVICE_REQUEST'");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_source_service_request_id",
                schema: "billing",
                table: "invoices",
                column: "source_service_request_id",
                unique: true,
                filter: "source_service_request_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_invoices_apartment_unit_id_billing_period_start_billing_per~",
                schema: "billing",
                table: "invoices");

            migrationBuilder.DropIndex(
                name: "IX_invoices_source_service_request_id",
                schema: "billing",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "source_service_request_id",
                schema: "billing",
                table: "invoices");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_apartment_unit_id_billing_period_start_billing_per~",
                schema: "billing",
                table: "invoices",
                columns: new[] { "apartment_unit_id", "billing_period_start", "billing_period_end", "invoice_type" },
                unique: true);
        }
    }
}
