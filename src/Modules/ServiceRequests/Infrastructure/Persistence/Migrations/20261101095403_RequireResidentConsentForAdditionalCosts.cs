using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.ServiceRequests.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RequireResidentConsentForAdditionalCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "additional_cost_consent_status",
                schema: "service_requests",
                table: "service_requests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NONE");

            migrationBuilder.AddColumn<Guid>(
                name: "additional_cost_proposal_id",
                schema: "service_requests",
                table: "service_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "additional_cost_proposed_at",
                schema: "service_requests",
                table: "service_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "additional_cost_responded_at",
                schema: "service_requests",
                table: "service_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "additional_cost_responded_by",
                schema: "service_requests",
                table: "service_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "resident_notification_outbox",
                schema: "service_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_error_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resident_notification_outbox", x => x.id);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_service_request_cost_consent",
                schema: "service_requests",
                table: "service_requests",
                sql: "additional_cost_consent_status IN ('NONE','PENDING','ACCEPTED','REJECTED') AND (additional_cost_consent_status = 'NONE' OR (additional_cost_proposal_id IS NOT NULL AND proposed_additional_cost > 0 AND additional_cost_proposed_at IS NOT NULL)) AND (additional_cost_consent_status NOT IN ('ACCEPTED','REJECTED') OR (additional_cost_responded_at IS NOT NULL AND additional_cost_responded_by IS NOT NULL)) AND (additional_cost_consent_status <> 'REJECTED' OR status = 'CANCELLED')");

            migrationBuilder.CreateIndex(
                name: "IX_resident_notification_outbox_status_next_attempt_at",
                schema: "service_requests",
                table: "resident_notification_outbox",
                columns: new[] { "status", "next_attempt_at" });

            // Existing unfinished proposals also need explicit consent; issued/resolved work is historical.
            migrationBuilder.Sql("""
                UPDATE service_requests.service_requests
                SET additional_cost_proposal_id = gen_random_uuid(),
                    additional_cost_consent_status = 'PENDING',
                    additional_cost_proposed_at = updated_at,
                    additional_cost_note = COALESCE(NULLIF(btrim(additional_cost_note), ''), 'Chi phí phát sinh đã được đề xuất trước khi cập nhật luồng xác nhận.')
                WHERE proposed_additional_cost > 0 AND status IN ('ASSIGNED', 'IN_PROGRESS') AND service_invoice_id IS NULL;

                INSERT INTO service_requests.resident_notification_outbox
                    (id, correlation_id, source_id, event_name, version, payload, created_at, next_attempt_at, attempts, status)
                SELECT additional_cost_proposal_id, additional_cost_proposal_id, id, 'ServiceRequestAdditionalCostProposed', 1,
                    jsonb_build_object('ResidentId', resident_id, 'RequestNumber', request_number,
                        'Amount', proposed_additional_cost, 'Note', additional_cost_note, 'ProposalId', additional_cost_proposal_id),
                    now(), now(), 0, 'Pending'
                FROM service_requests.service_requests
                WHERE additional_cost_consent_status = 'PENDING';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "resident_notification_outbox",
                schema: "service_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_service_request_cost_consent",
                schema: "service_requests",
                table: "service_requests");

            migrationBuilder.DropColumn(
                name: "additional_cost_consent_status",
                schema: "service_requests",
                table: "service_requests");

            migrationBuilder.DropColumn(
                name: "additional_cost_proposal_id",
                schema: "service_requests",
                table: "service_requests");

            migrationBuilder.DropColumn(
                name: "additional_cost_proposed_at",
                schema: "service_requests",
                table: "service_requests");

            migrationBuilder.DropColumn(
                name: "additional_cost_responded_at",
                schema: "service_requests",
                table: "service_requests");

            migrationBuilder.DropColumn(
                name: "additional_cost_responded_by",
                schema: "service_requests",
                table: "service_requests");
        }
    }
}
