using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.AiRecommendation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFacilityRecommendationRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "facility_recommendation_runs",
                schema: "ai_recommendation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor = table.Column<Guid>(type: "uuid", nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    result_json = table.Column<string>(type: "jsonb", nullable: true),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    input_tokens = table.Column<int>(type: "integer", nullable: true),
                    output_tokens = table.Column<int>(type: "integer", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_facility_recommendation_runs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_facility_recommendation_runs_actor_created_at",
                schema: "ai_recommendation",
                table: "facility_recommendation_runs",
                columns: new[] { "actor", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_facility_recommendation_runs_actor_fingerprint",
                schema: "ai_recommendation",
                table: "facility_recommendation_runs",
                columns: new[] { "actor", "fingerprint" });

            migrationBuilder.CreateIndex(
                name: "IX_facility_recommendation_runs_created_at",
                schema: "ai_recommendation",
                table: "facility_recommendation_runs",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "facility_recommendation_runs",
                schema: "ai_recommendation");
        }
    }
}
