using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.AiRecommendation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFacilityWeekendNotices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "facility_weekend_notice_runs",
                schema: "ai_recommendation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    local_date = table.Column<DateOnly>(type: "date", nullable: false),
                    slot = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    weekend_start = table.Column<DateOnly>(type: "date", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lease_token = table.Column<Guid>(type: "uuid", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    recipients = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_facility_weekend_notice_runs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_facility_weekend_notice_runs_local_date_slot",
                schema: "ai_recommendation",
                table: "facility_weekend_notice_runs",
                columns: new[] { "local_date", "slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_facility_weekend_notice_runs_weekend_start_facility_id",
                schema: "ai_recommendation",
                table: "facility_weekend_notice_runs",
                columns: new[] { "weekend_start", "facility_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "facility_weekend_notice_runs",
                schema: "ai_recommendation");
        }
    }
}
