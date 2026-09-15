using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.AiRecommendation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAiRecommendation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ai_recommendation");

            migrationBuilder.CreateTable(
                name: "ai_request_recommendations",
                schema: "ai_recommendation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_no = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    model_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    suggested_priority_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    maintenance_recommended = table.Column<bool>(type: "boolean", nullable: true),
                    recommended_action = table.Column<string>(type: "text", nullable: true),
                    recommended_resource_summary = table.Column<string>(type: "text", nullable: true),
                    recommended_resources = table.Column<string>(type: "jsonb", nullable: true),
                    reasoning_summary = table.Column<string>(type: "text", nullable: true),
                    input_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    input_tokens = table.Column<int>(type: "integer", nullable: true),
                    output_tokens = table.Column<int>(type: "integer", nullable: true),
                    total_tokens = table.Column<int>(type: "integer", nullable: true),
                    run_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    latency_ms = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_request_recommendations", x => x.id);
                    table.CheckConstraint("CK_ai_request_recommendations_attempt_no", "attempt_no >= 1");
                    table.CheckConstraint("CK_ai_request_recommendations_failed_fields", "(run_status <> 'FAILED') OR (error_message IS NOT NULL AND suggested_priority_code IS NULL AND maintenance_recommended IS NULL AND recommended_action IS NULL AND recommended_resource_summary IS NULL AND recommended_resources IS NULL AND reasoning_summary IS NULL)");
                    table.CheckConstraint("CK_ai_request_recommendations_latency", "latency_ms IS NULL OR latency_ms >= 0");
                    table.CheckConstraint("CK_ai_request_recommendations_success_fields", "(run_status <> 'SUCCESS') OR (suggested_priority_code IS NOT NULL AND error_message IS NULL)");
                    table.CheckConstraint("CK_ai_request_recommendations_tokens", "(input_tokens IS NULL OR input_tokens >= 0) AND (output_tokens IS NULL OR output_tokens >= 0) AND (total_tokens IS NULL OR total_tokens >= 0)");
                });

            migrationBuilder.CreateTable(
                name: "ai_recommendation_reviews",
                schema: "ai_recommendation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recommendation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    decision = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    final_priority_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    final_maintenance_required = table.Column<bool>(type: "boolean", nullable: true),
                    final_action = table.Column<string>(type: "text", nullable: true),
                    final_resource_plan = table.Column<string>(type: "text", nullable: true),
                    review_note = table.Column<string>(type: "text", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_recommendation_reviews", x => x.id);
                    table.CheckConstraint("CK_ai_recommendation_reviews_corrected_note", "(decision <> 'CORRECTED') OR review_note IS NOT NULL");
                    table.CheckConstraint("CK_ai_recommendation_reviews_final_fields", "(decision IN ('CONFIRMED', 'CORRECTED', 'OVERRIDDEN') AND final_priority_code IS NOT NULL) OR (decision = 'REJECTED' AND final_priority_code IS NULL AND final_maintenance_required IS NULL AND final_action IS NULL AND final_resource_plan IS NULL)");
                    table.ForeignKey(
                        name: "FK_ai_recommendation_reviews_ai_request_recommendations_recomm~",
                        column: x => x.recommendation_id,
                        principalSchema: "ai_recommendation",
                        principalTable: "ai_request_recommendations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_recommendation_reviews_decision",
                schema: "ai_recommendation",
                table: "ai_recommendation_reviews",
                column: "decision");

            migrationBuilder.CreateIndex(
                name: "IX_ai_recommendation_reviews_recommendation_id",
                schema: "ai_recommendation",
                table: "ai_recommendation_reviews",
                column: "recommendation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ai_recommendation_reviews_reviewed_by",
                schema: "ai_recommendation",
                table: "ai_recommendation_reviews",
                column: "reviewed_by");

            migrationBuilder.CreateIndex(
                name: "IX_ai_request_recommendations_run_status",
                schema: "ai_recommendation",
                table: "ai_request_recommendations",
                column: "run_status");

            migrationBuilder.CreateIndex(
                name: "IX_ai_request_recommendations_service_request_id",
                schema: "ai_recommendation",
                table: "ai_request_recommendations",
                column: "service_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_ai_request_recommendations_service_request_id_attempt_no",
                schema: "ai_recommendation",
                table: "ai_request_recommendations",
                columns: new[] { "service_request_id", "attempt_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ai_request_recommendations_suggested_priority_code",
                schema: "ai_recommendation",
                table: "ai_request_recommendations",
                column: "suggested_priority_code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_recommendation_reviews",
                schema: "ai_recommendation");

            migrationBuilder.DropTable(
                name: "ai_request_recommendations",
                schema: "ai_recommendation");
        }
    }
}
