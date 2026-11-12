using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApartmentTypeRentalPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "apartment_type_id",
                schema: "billing",
                table: "fee_rate_rules",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_fee_rate_rules_apartment_type_id_fee_type_id_billing_freque~",
                schema: "billing",
                table: "fee_rate_rules",
                columns: new[] { "apartment_type_id", "fee_type_id", "billing_frequency_code", "effective_from" },
                unique: true,
                filter: "apartment_type_id IS NOT NULL AND is_active = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fee_rate_rules_apartment_type_id_fee_type_id_billing_freque~",
                schema: "billing",
                table: "fee_rate_rules");

            migrationBuilder.DropColumn(
                name: "apartment_type_id",
                schema: "billing",
                table: "fee_rate_rules");
        }
    }
}
