using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeparateInvoiceTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_invoices_apartment_unit_id_billing_period_start_billing_per~",
                schema: "billing",
                table: "invoices");

            migrationBuilder.AddColumn<string>(
                name: "invoice_type",
                schema: "billing",
                table: "invoices",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            // Classify only unambiguous historical documents; do not alter amounts or split legacy invoices.
            migrationBuilder.Sql("""
                UPDATE billing.invoices i SET invoice_type = CASE
                  WHEN NOT EXISTS (SELECT 1 FROM billing.invoice_items x WHERE x.invoice_id = i.id AND x.fee_code_snapshot = 'APARTMENT_RENT') THEN 'SERVICE_FEE'
                  WHEN NOT EXISTS (SELECT 1 FROM billing.invoice_items x WHERE x.invoice_id = i.id AND x.fee_code_snapshot <> 'APARTMENT_RENT') THEN 'MONTHLY_RENTAL'
                  ELSE NULL END
                WHERE EXISTS (SELECT 1 FROM billing.invoice_items x WHERE x.invoice_id = i.id);
                """);
            migrationBuilder.CreateIndex(
                name: "IX_invoices_apartment_unit_id_billing_period_start_billing_per~",
                schema: "billing",
                table: "invoices",
                columns: new[] { "apartment_unit_id", "billing_period_start", "billing_period_end", "invoice_type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_invoices_apartment_unit_id_billing_period_start_billing_per~",
                schema: "billing",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "invoice_type",
                schema: "billing",
                table: "invoices");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_apartment_unit_id_billing_period_start_billing_per~",
                schema: "billing",
                table: "invoices",
                columns: new[] { "apartment_unit_id", "billing_period_start", "billing_period_end" },
                unique: true);
        }
    }
}

