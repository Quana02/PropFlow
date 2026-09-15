using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.ServiceRequests.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialServiceRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "service_requests");

            migrationBuilder.CreateTable(
                name: "service_request_categories",
                schema: "service_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    display_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_request_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "service_requests",
                schema: "service_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    resident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resident_apartment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    building_id = table.Column<Guid>(type: "uuid", nullable: false),
                    apartment_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    equipment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    final_priority_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "SUBMITTED"),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_requests", x => x.id);
                    table.ForeignKey(
                        name: "FK_service_requests_service_request_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "service_requests",
                        principalTable: "service_request_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "service_request_assignments",
                schema: "service_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_by = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "ASSIGNED"),
                    assignment_note = table.Column<string>(type: "text", nullable: true),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_request_assignments", x => x.id);
                    table.ForeignKey(
                        name: "FK_service_request_assignments_service_requests_service_reques~",
                        column: x => x.service_request_id,
                        principalSchema: "service_requests",
                        principalTable: "service_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "service_request_activities",
                schema: "service_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    activity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    from_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    to_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    detail = table.Column<string>(type: "text", nullable: true),
                    work_result = table.Column<string>(type: "text", nullable: true),
                    performed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_request_activities", x => x.id);
                    table.ForeignKey(
                        name: "FK_service_request_activities_service_request_assignments_assi~",
                        column: x => x.assignment_id,
                        principalSchema: "service_requests",
                        principalTable: "service_request_assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_request_activities_service_requests_service_request~",
                        column: x => x.service_request_id,
                        principalSchema: "service_requests",
                        principalTable: "service_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_service_request_activities_activity_type",
                schema: "service_requests",
                table: "service_request_activities",
                column: "activity_type");

            migrationBuilder.CreateIndex(
                name: "IX_service_request_activities_assignment_id",
                schema: "service_requests",
                table: "service_request_activities",
                column: "assignment_id");

            migrationBuilder.CreateIndex(
                name: "IX_service_request_activities_created_at",
                schema: "service_requests",
                table: "service_request_activities",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_service_request_activities_service_request_id",
                schema: "service_requests",
                table: "service_request_activities",
                column: "service_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_service_request_assignments_active_service_request_id",
                schema: "service_requests",
                table: "service_request_assignments",
                column: "service_request_id",
                unique: true,
                filter: "\"status\" IN ('ASSIGNED', 'IN_PROGRESS')");

            migrationBuilder.CreateIndex(
                name: "IX_service_request_assignments_assigned_at",
                schema: "service_requests",
                table: "service_request_assignments",
                column: "assigned_at");

            migrationBuilder.CreateIndex(
                name: "IX_service_request_assignments_staff_user_id",
                schema: "service_requests",
                table: "service_request_assignments",
                column: "staff_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_service_request_assignments_status",
                schema: "service_requests",
                table: "service_request_assignments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_service_request_categories_code",
                schema: "service_requests",
                table: "service_request_categories",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_requests_apartment_unit_id",
                schema: "service_requests",
                table: "service_requests",
                column: "apartment_unit_id");

            migrationBuilder.CreateIndex(
                name: "IX_service_requests_building_id",
                schema: "service_requests",
                table: "service_requests",
                column: "building_id");

            migrationBuilder.CreateIndex(
                name: "IX_service_requests_category_id",
                schema: "service_requests",
                table: "service_requests",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_service_requests_request_number",
                schema: "service_requests",
                table: "service_requests",
                column: "request_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_requests_resident_id",
                schema: "service_requests",
                table: "service_requests",
                column: "resident_id");

            migrationBuilder.CreateIndex(
                name: "IX_service_requests_status",
                schema: "service_requests",
                table: "service_requests",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_service_requests_status_final_priority_code",
                schema: "service_requests",
                table: "service_requests",
                columns: new[] { "status", "final_priority_code" });

            migrationBuilder.CreateIndex(
                name: "IX_service_requests_submitted_at",
                schema: "service_requests",
                table: "service_requests",
                column: "submitted_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "service_request_activities",
                schema: "service_requests");

            migrationBuilder.DropTable(
                name: "service_request_assignments",
                schema: "service_requests");

            migrationBuilder.DropTable(
                name: "service_requests",
                schema: "service_requests");

            migrationBuilder.DropTable(
                name: "service_request_categories",
                schema: "service_requests");
        }
    }
}
