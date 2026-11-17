using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.AiRecommendation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUtilityConsumptionInsights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "utility_ai_attempts",
                schema: "ai_recommendation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor = table.Column<Guid>(type: "uuid", nullable: false),
                    insight_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    input_tokens = table.Column<int>(type: "integer", nullable: true),
                    output_tokens = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_utility_ai_attempts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "utility_insights",
                schema: "ai_recommendation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    apartment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    unit_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    method_version = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    is_final = table.Column<bool>(type: "boolean", nullable: false),
                    recipients = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    analysis_json = table.Column<string>(type: "jsonb", nullable: false),
                    review = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ai_explanation = table.Column<string>(type: "character varying(900)", maxLength: 900, nullable: true),
                    ai_mode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_utility_insights", x => x.id);
                    table.CheckConstraint("ck_utility_review", "review IN ('PENDING','CONFIRMED','DISMISSED')");
                    table.CheckConstraint("ck_utility_revision", "revision > 0");
                });

            migrationBuilder.CreateTable(
                name: "utility_notification_outbox",
                schema: "ai_recommendation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
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
                    table.PrimaryKey("PK_utility_notification_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "utility_insight_reviews",
                schema: "ai_recommendation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    insight_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    actor = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_utility_insight_reviews", x => x.id);
                    table.ForeignKey(
                        name: "FK_utility_insight_reviews_utility_insights_insight_id",
                        column: x => x.insight_id,
                        principalSchema: "ai_recommendation",
                        principalTable: "utility_insights",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_utility_ai_attempts_actor_created_at",
                schema: "ai_recommendation",
                table: "utility_ai_attempts",
                columns: new[] { "actor", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_utility_ai_attempts_created_at",
                schema: "ai_recommendation",
                table: "utility_ai_attempts",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_utility_insight_reviews_insight_id_revision",
                schema: "ai_recommendation",
                table: "utility_insight_reviews",
                columns: new[] { "insight_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_utility_insights_apartment_id_month",
                schema: "ai_recommendation",
                table: "utility_insights",
                columns: new[] { "apartment_id", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_utility_insights_month_review",
                schema: "ai_recommendation",
                table: "utility_insights",
                columns: new[] { "month", "review" });

            migrationBuilder.CreateIndex(
                name: "IX_utility_notification_outbox_status_next_attempt_at",
                schema: "ai_recommendation",
                table: "utility_notification_outbox",
                columns: new[] { "status", "next_attempt_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "utility_ai_attempts",
                schema: "ai_recommendation");

            migrationBuilder.DropTable(
                name: "utility_insight_reviews",
                schema: "ai_recommendation");

            migrationBuilder.DropTable(
                name: "utility_notification_outbox",
                schema: "ai_recommendation");

            migrationBuilder.DropTable(
                name: "utility_insights",
                schema: "ai_recommendation");
        }
    }
}
