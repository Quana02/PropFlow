using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.AiClassification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAiClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ai_classification");

            migrationBuilder.CreateTable(
                name: "ai_request_classifications",
                schema: "ai_classification",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_no = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    model_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    predicted_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    confidence_score = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
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
                    table.PrimaryKey("PK_ai_request_classifications", x => x.id);
                    table.CheckConstraint("CK_ai_request_classifications_attempt_no", "attempt_no >= 1");
                    table.CheckConstraint("CK_ai_request_classifications_confidence_score", "confidence_score IS NULL OR (confidence_score >= 0 AND confidence_score <= 1)");
                    table.CheckConstraint("CK_ai_request_classifications_failed_fields", "(run_status <> 'FAILED') OR error_message IS NOT NULL");
                    table.CheckConstraint("CK_ai_request_classifications_latency", "latency_ms IS NULL OR latency_ms >= 0");
                    table.CheckConstraint("CK_ai_request_classifications_success_fields", "(run_status <> 'SUCCESS') OR predicted_category_id IS NOT NULL");
                    table.CheckConstraint("CK_ai_request_classifications_tokens", "(input_tokens IS NULL OR input_tokens >= 0) AND (output_tokens IS NULL OR output_tokens >= 0) AND (total_tokens IS NULL OR total_tokens >= 0)");
                });

            migrationBuilder.CreateTable(
                name: "ai_classification_reviews",
                schema: "ai_classification",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    classification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    decision = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    final_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    review_note = table.Column<string>(type: "text", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_classification_reviews", x => x.id);
                    table.CheckConstraint("CK_ai_classification_reviews_final_category", "(decision IN ('CONFIRMED', 'CORRECTED', 'OVERRIDDEN') AND final_category_id IS NOT NULL) OR (decision = 'REJECTED' AND final_category_id IS NULL)");
                    table.ForeignKey(
                        name: "FK_ai_classification_reviews_ai_request_classifications_classi~",
                        column: x => x.classification_id,
                        principalSchema: "ai_classification",
                        principalTable: "ai_request_classifications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_classification_reviews_classification_id",
                schema: "ai_classification",
                table: "ai_classification_reviews",
                column: "classification_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ai_classification_reviews_decision",
                schema: "ai_classification",
                table: "ai_classification_reviews",
                column: "decision");

            migrationBuilder.CreateIndex(
                name: "IX_ai_classification_reviews_reviewed_by",
                schema: "ai_classification",
                table: "ai_classification_reviews",
                column: "reviewed_by");

            migrationBuilder.CreateIndex(
                name: "IX_ai_request_classifications_predicted_category_id",
                schema: "ai_classification",
                table: "ai_request_classifications",
                column: "predicted_category_id");

            migrationBuilder.CreateIndex(
                name: "IX_ai_request_classifications_run_status",
                schema: "ai_classification",
                table: "ai_request_classifications",
                column: "run_status");

            migrationBuilder.CreateIndex(
                name: "IX_ai_request_classifications_service_request_id",
                schema: "ai_classification",
                table: "ai_request_classifications",
                column: "service_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_ai_request_classifications_service_request_id_attempt_no",
                schema: "ai_classification",
                table: "ai_request_classifications",
                columns: new[] { "service_request_id", "attempt_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_classification_reviews",
                schema: "ai_classification");

            migrationBuilder.DropTable(
                name: "ai_request_classifications",
                schema: "ai_classification");
        }
    }
}
