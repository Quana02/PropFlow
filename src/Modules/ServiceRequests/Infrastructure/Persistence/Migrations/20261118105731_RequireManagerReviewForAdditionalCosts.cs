using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.ServiceRequests.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RequireManagerReviewForAdditionalCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_service_request_cost_consent",
                schema: "service_requests",
                table: "service_requests");

            migrationBuilder.AddCheckConstraint(
                name: "ck_service_request_cost_consent",
                schema: "service_requests",
                table: "service_requests",
                sql: "additional_cost_consent_status IN ('NONE','AWAITING_MANAGER','PENDING','ACCEPTED','REJECTED') AND (additional_cost_consent_status = 'NONE' OR (additional_cost_proposal_id IS NOT NULL AND proposed_additional_cost > 0 AND additional_cost_proposed_at IS NOT NULL)) AND (additional_cost_consent_status NOT IN ('ACCEPTED','REJECTED') OR (additional_cost_responded_at IS NOT NULL AND additional_cost_responded_by IS NOT NULL)) AND (additional_cost_consent_status <> 'REJECTED' OR status = 'CANCELLED')");

            // Pending legacy quotes have not passed the new Manager checkpoint.
            // Preserve accepted decisions, completed requests and issued invoices.
            migrationBuilder.Sql("""
                UPDATE service_requests.service_requests
                SET additional_cost_consent_status = 'AWAITING_MANAGER', updated_at = now()
                WHERE additional_cost_consent_status = 'PENDING'
                  AND status IN ('ASSIGNED','IN_PROGRESS') AND service_invoice_id IS NULL;

                UPDATE service_requests.resident_notification_outbox o
                SET status = 'Delivered', delivered_at = now()
                WHERE o.event_name = 'ServiceRequestAdditionalCostProposed' AND o.status IN ('Pending','Failed')
                  AND EXISTS (SELECT 1 FROM service_requests.service_requests r
                    WHERE r.id = o.source_id AND r.additional_cost_consent_status = 'AWAITING_MANAGER');

                INSERT INTO service_requests.resident_notification_outbox
                    (id, correlation_id, source_id, event_name, version, payload, created_at, next_attempt_at, attempts, status)
                SELECT gen_random_uuid(), gen_random_uuid(), r.id, 'ServiceRequestAdditionalCostReviewRequested', 1,
                    jsonb_build_object('ProposalId', r.additional_cost_proposal_id, 'RequestNumber', r.request_number,
                        'StaffUserId', (SELECT a.staff_user_id FROM service_requests.service_request_assignments a
                            WHERE a.service_request_id = r.id AND a.status IN ('ASSIGNED','IN_PROGRESS')
                            ORDER BY a.assigned_at DESC LIMIT 1)), now(), now(), 0, 'Pending'
                FROM service_requests.service_requests r
                WHERE r.additional_cost_consent_status = 'AWAITING_MANAGER';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE service_requests.service_requests
                SET additional_cost_consent_status = 'PENDING', updated_at = now()
                WHERE additional_cost_consent_status = 'AWAITING_MANAGER';
                """);
            migrationBuilder.DropCheckConstraint(
                name: "ck_service_request_cost_consent",
                schema: "service_requests",
                table: "service_requests");

            migrationBuilder.AddCheckConstraint(
                name: "ck_service_request_cost_consent",
                schema: "service_requests",
                table: "service_requests",
                sql: "additional_cost_consent_status IN ('NONE','PENDING','ACCEPTED','REJECTED') AND (additional_cost_consent_status = 'NONE' OR (additional_cost_proposal_id IS NOT NULL AND proposed_additional_cost > 0 AND additional_cost_proposed_at IS NOT NULL)) AND (additional_cost_consent_status NOT IN ('ACCEPTED','REJECTED') OR (additional_cost_responded_at IS NOT NULL AND additional_cost_responded_by IS NOT NULL)) AND (additional_cost_consent_status <> 'REJECTED' OR status = 'CANCELLED')");
        }
    }
}
