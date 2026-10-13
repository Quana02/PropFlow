using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApartmentRentalPricingAndRecipientIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_invoice_recipients",
                schema: "billing",
                table: "invoice_recipients");

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                schema: "billing",
                table: "invoice_recipients",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "id",
                schema: "billing",
                table: "invoice_recipients",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<string>(
                name: "capacity",
                schema: "billing",
                table: "invoice_recipients",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "HOUSEHOLD_HEAD");

            migrationBuilder.AddColumn<Guid>(
                name: "resident_id",
                schema: "billing",
                table: "invoice_recipients",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "apartment_unit_id",
                schema: "billing",
                table: "fee_rate_rules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_invoice_recipients",
                schema: "billing",
                table: "invoice_recipients",
                column: "id");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_recipients_invoice_id_resident_id",
                schema: "billing",
                table: "invoice_recipients",
                columns: new[] { "invoice_id", "resident_id" },
                unique: true,
                filter: "resident_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_recipients_invoice_id_user_id",
                schema: "billing",
                table: "invoice_recipients",
                columns: new[] { "invoice_id", "user_id" },
                unique: true,
                filter: "user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_fee_rate_rules_apartment_unit_id_fee_type_id_effective_from",
                schema: "billing",
                table: "fee_rate_rules",
                columns: new[] { "apartment_unit_id", "fee_type_id", "effective_from" },
                unique: true,
                filter: "apartment_unit_id IS NOT NULL AND is_active = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_invoice_recipients",
                schema: "billing",
                table: "invoice_recipients");

            migrationBuilder.DropIndex(
                name: "IX_invoice_recipients_invoice_id_resident_id",
                schema: "billing",
                table: "invoice_recipients");

            migrationBuilder.DropIndex(
                name: "IX_invoice_recipients_invoice_id_user_id",
                schema: "billing",
                table: "invoice_recipients");

            migrationBuilder.DropIndex(
                name: "IX_fee_rate_rules_apartment_unit_id_fee_type_id_effective_from",
                schema: "billing",
                table: "fee_rate_rules");

            migrationBuilder.DropColumn(
                name: "id",
                schema: "billing",
                table: "invoice_recipients");

            migrationBuilder.DropColumn(
                name: "capacity",
                schema: "billing",
                table: "invoice_recipients");

            migrationBuilder.DropColumn(
                name: "resident_id",
                schema: "billing",
                table: "invoice_recipients");

            migrationBuilder.DropColumn(
                name: "apartment_unit_id",
                schema: "billing",
                table: "fee_rate_rules");

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                schema: "billing",
                table: "invoice_recipients",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_invoice_recipients",
                schema: "billing",
                table: "invoice_recipients",
                columns: new[] { "invoice_id", "user_id" });
        }
    }
}
