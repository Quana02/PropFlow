using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.AiClassification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CorrectAiClassificationDomainConsistency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ai_request_classifications_failed_fields",
                schema: "ai_classification",
                table: "ai_request_classifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ai_request_classifications_success_fields",
                schema: "ai_classification",
                table: "ai_request_classifications");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ai_request_classifications_failed_fields",
                schema: "ai_classification",
                table: "ai_request_classifications",
                sql: "(run_status <> 'FAILED') OR (error_message IS NOT NULL AND predicted_category_id IS NULL AND confidence_score IS NULL AND reasoning_summary IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ai_request_classifications_success_fields",
                schema: "ai_classification",
                table: "ai_request_classifications",
                sql: "(run_status <> 'SUCCESS') OR (predicted_category_id IS NOT NULL AND error_message IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ai_request_classifications_failed_fields",
                schema: "ai_classification",
                table: "ai_request_classifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ai_request_classifications_success_fields",
                schema: "ai_classification",
                table: "ai_request_classifications");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ai_request_classifications_failed_fields",
                schema: "ai_classification",
                table: "ai_request_classifications",
                sql: "(run_status <> 'FAILED') OR error_message IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ai_request_classifications_success_fields",
                schema: "ai_classification",
                table: "ai_request_classifications",
                sql: "(run_status <> 'SUCCESS') OR predicted_category_id IS NOT NULL");
        }
    }
}
