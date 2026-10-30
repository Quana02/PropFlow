using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.ServiceRequests.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceRequestCostsAndInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "additional_cost_note",
                schema: "service_requests",
                table: "service_requests",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "approved_additional_cost",
                schema: "service_requests",
                table: "service_requests",
                type: "numeric(18,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "base_service_cost",
                schema: "service_requests",
                table: "service_requests",
                type: "numeric(18,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "proposed_additional_cost",
                schema: "service_requests",
                table: "service_requests",
                type: "numeric(18,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "service_invoice_id",
                schema: "service_requests",
                table: "service_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "submitted_by_user_id",
                schema: "service_requests",
                table: "service_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_requests_service_invoice_id",
                schema: "service_requests",
                table: "service_requests",
                column: "service_invoice_id",
                unique: true,
                filter: "service_invoice_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_service_requests_service_invoice_id",
                schema: "service_requests",
                table: "service_requests");

            migrationBuilder.DropColumn(
                name: "additional_cost_note",
                schema: "service_requests",
                table: "service_requests");

            migrationBuilder.DropColumn(
                name: "approved_additional_cost",
                schema: "service_requests",
                table: "service_requests");

            migrationBuilder.DropColumn(
                name: "base_service_cost",
                schema: "service_requests",
                table: "service_requests");

            migrationBuilder.DropColumn(
                name: "proposed_additional_cost",
                schema: "service_requests",
                table: "service_requests");

            migrationBuilder.DropColumn(
                name: "service_invoice_id",
                schema: "service_requests",
                table: "service_requests");

            migrationBuilder.DropColumn(
                name: "submitted_by_user_id",
                schema: "service_requests",
                table: "service_requests");
        }
    }
}
