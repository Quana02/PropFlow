using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedFacilityUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "facility_occupancies",
                schema: "property_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    starts_at = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    ends_at = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    peak_people = table.Column<int>(type: "integer", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    token = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_facility_occupancies", x => x.id);
                    table.CheckConstraint("ck_facility_occupancy_hours", "starts_at < ends_at");
                    table.CheckConstraint("ck_facility_occupancy_people", "peak_people BETWEEN 0 AND 100000");
                    table.ForeignKey(
                        name: "FK_facility_occupancies_facilities_facility_id",
                        column: x => x.facility_id,
                        principalSchema: "property_assets",
                        principalTable: "facilities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "facility_usage_settings",
                schema: "property_assets",
                columns: table => new
                {
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    published = table.Column<bool>(type: "boolean", nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    slot_minutes = table.Column<int>(type: "integer", nullable: false),
                    rules = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    hours_json = table.Column<string>(type: "jsonb", nullable: false),
                    closed_dates = table.Column<DateOnly[]>(type: "date[]", nullable: false),
                    assigned_staff_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    token = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_facility_usage_settings", x => x.facility_id);
                    table.CheckConstraint("ck_facility_usage_capacity", "capacity BETWEEN 1 AND 10000");
                    table.CheckConstraint("ck_facility_usage_slot", "slot_minutes IN (30,60,120)");
                    table.ForeignKey(
                        name: "FK_facility_usage_settings_facilities_facility_id",
                        column: x => x.facility_id,
                        principalSchema: "property_assets",
                        principalTable: "facilities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_facility_occupancies_date",
                schema: "property_assets",
                table: "facility_occupancies",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "IX_facility_occupancies_facility_id_date_starts_at",
                schema: "property_assets",
                table: "facility_occupancies",
                columns: new[] { "facility_id", "date", "starts_at" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "facility_occupancies",
                schema: "property_assets");

            migrationBuilder.DropTable(
                name: "facility_usage_settings",
                schema: "property_assets");
        }
    }
}
