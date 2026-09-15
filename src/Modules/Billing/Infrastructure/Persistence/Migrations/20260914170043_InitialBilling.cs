using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "billing");

            migrationBuilder.CreateTable(
                name: "fee_types",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    default_calculation_method_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    default_billing_frequency_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "ACTIVE"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fee_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    apartment_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    billing_period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    billing_period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    subtotal = table.Column<decimal>(type: "numeric(18,0)", nullable: false, defaultValue: 0m),
                    total_amount = table.Column<decimal>(type: "numeric(18,0)", nullable: false, defaultValue: 0m),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "DRAFT"),
                    note = table.Column<string>(type: "text", nullable: true),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    issued_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancellation_reason = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoices", x => x.id);
                    table.CheckConstraint("CK_invoices_billing_period", "billing_period_end >= billing_period_start");
                    table.CheckConstraint("CK_invoices_cancelled_fields", "(status <> 'CANCELLED') OR (cancelled_at IS NOT NULL AND cancelled_by IS NOT NULL AND cancellation_reason IS NOT NULL)");
                    table.CheckConstraint("CK_invoices_due_date", "issue_date IS NULL OR due_date IS NULL OR due_date >= issue_date");
                    table.CheckConstraint("CK_invoices_issued_fields", "(status <> 'ISSUED') OR (issue_date IS NOT NULL AND issued_at IS NOT NULL AND issued_by IS NOT NULL AND cancelled_at IS NULL AND cancelled_by IS NULL)");
                    table.CheckConstraint("CK_invoices_subtotal_non_negative", "subtotal >= 0");
                    table.CheckConstraint("CK_invoices_total_amount_non_negative", "total_amount >= 0");
                    table.CheckConstraint("CK_invoices_total_matches_subtotal", "total_amount = subtotal");
                });

            migrationBuilder.CreateTable(
                name: "fee_rate_rules",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fee_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    building_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rule_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    calculation_method_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    billing_frequency_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    unit_rate = table.Column<decimal>(type: "numeric(18,0)", nullable: false),
                    unit_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    minimum_amount = table.Column<decimal>(type: "numeric(18,0)", nullable: true),
                    maximum_amount = table.Column<decimal>(type: "numeric(18,0)", nullable: true),
                    rule_config = table.Column<string>(type: "jsonb", nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fee_rate_rules", x => x.id);
                    table.CheckConstraint("CK_fee_rate_rules_amount_range", "minimum_amount IS NULL OR maximum_amount IS NULL OR minimum_amount <= maximum_amount");
                    table.CheckConstraint("CK_fee_rate_rules_effective_dates", "effective_to IS NULL OR effective_to >= effective_from");
                    table.CheckConstraint("CK_fee_rate_rules_maximum_amount_non_negative", "maximum_amount IS NULL OR maximum_amount >= 0");
                    table.CheckConstraint("CK_fee_rate_rules_minimum_amount_non_negative", "minimum_amount IS NULL OR minimum_amount >= 0");
                    table.CheckConstraint("CK_fee_rate_rules_unit_rate_non_negative", "unit_rate >= 0");
                    table.ForeignKey(
                        name: "FK_fee_rate_rules_fee_types_fee_type_id",
                        column: x => x.fee_type_id,
                        principalSchema: "billing",
                        principalTable: "fee_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoice_status_history",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    to_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_status_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_invoice_status_history_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "billing",
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoice_items",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fee_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fee_rate_rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fee_code_snapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    fee_name_snapshot = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    calculation_method_code_snapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false, defaultValue: 1m),
                    unit_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    unit_rate = table.Column<decimal>(type: "numeric(18,0)", nullable: false),
                    line_amount = table.Column<decimal>(type: "numeric(18,0)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_items", x => x.id);
                    table.CheckConstraint("CK_invoice_items_line_amount_non_negative", "line_amount >= 0");
                    table.CheckConstraint("CK_invoice_items_quantity_positive", "quantity > 0");
                    table.CheckConstraint("CK_invoice_items_unit_rate_non_negative", "unit_rate >= 0");
                    table.ForeignKey(
                        name: "FK_invoice_items_fee_rate_rules_fee_rate_rule_id",
                        column: x => x.fee_rate_rule_id,
                        principalSchema: "billing",
                        principalTable: "fee_rate_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_invoice_items_fee_types_fee_type_id",
                        column: x => x.fee_type_id,
                        principalSchema: "billing",
                        principalTable: "fee_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_invoice_items_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "billing",
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fee_rate_rules_building_id",
                schema: "billing",
                table: "fee_rate_rules",
                column: "building_id");

            migrationBuilder.CreateIndex(
                name: "IX_fee_rate_rules_fee_type_id",
                schema: "billing",
                table: "fee_rate_rules",
                column: "fee_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_fee_rate_rules_fee_type_id_building_id_effective_from",
                schema: "billing",
                table: "fee_rate_rules",
                columns: new[] { "fee_type_id", "building_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "IX_fee_rate_rules_is_active",
                schema: "billing",
                table: "fee_rate_rules",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "IX_fee_types_code",
                schema: "billing",
                table: "fee_types",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoice_items_fee_rate_rule_id",
                schema: "billing",
                table: "invoice_items",
                column: "fee_rate_rule_id");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_items_fee_type_id",
                schema: "billing",
                table: "invoice_items",
                column: "fee_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_items_invoice_id",
                schema: "billing",
                table: "invoice_items",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_status_history_changed_at",
                schema: "billing",
                table: "invoice_status_history",
                column: "changed_at");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_status_history_invoice_id",
                schema: "billing",
                table: "invoice_status_history",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_apartment_unit_id",
                schema: "billing",
                table: "invoices",
                column: "apartment_unit_id");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_apartment_unit_id_billing_period_start_billing_per~",
                schema: "billing",
                table: "invoices",
                columns: new[] { "apartment_unit_id", "billing_period_start", "billing_period_end" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoices_due_date",
                schema: "billing",
                table: "invoices",
                column: "due_date");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_invoice_number",
                schema: "billing",
                table: "invoices",
                column: "invoice_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoices_status",
                schema: "billing",
                table: "invoices",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoice_items",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "invoice_status_history",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "fee_rate_rules",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "invoices",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "fee_types",
                schema: "billing");
        }
    }
}
