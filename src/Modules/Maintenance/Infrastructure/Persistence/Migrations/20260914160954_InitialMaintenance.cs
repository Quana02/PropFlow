using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Maintenance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialMaintenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "maintenance");

            migrationBuilder.CreateTable(
                name: "maintenance_schedules",
                schema: "maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    building_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    equipment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    planned_start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    planned_end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "ACTIVE"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_schedules", x => x.id);
                    table.CheckConstraint("CK_maintenance_schedules_planned_end_at", "planned_end_at IS NULL OR planned_end_at >= planned_start_at");
                });

            migrationBuilder.CreateTable(
                name: "maintenance_tasks",
                schema: "maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_service_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_complaint_id = table.Column<Guid>(type: "uuid", nullable: true),
                    building_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    equipment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    priority_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "OPEN"),
                    planned_start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_tasks", x => x.id);
                    table.CheckConstraint("CK_maintenance_tasks_closed_at", "completed_at IS NULL OR closed_at IS NULL OR closed_at >= completed_at");
                    table.CheckConstraint("CK_maintenance_tasks_completed_at", "started_at IS NULL OR completed_at IS NULL OR completed_at >= started_at");
                    table.CheckConstraint("CK_maintenance_tasks_due_at", "planned_start_at IS NULL OR due_at IS NULL OR due_at >= planned_start_at");
                    table.ForeignKey(
                        name: "FK_maintenance_tasks_maintenance_schedules_schedule_id",
                        column: x => x.schedule_id,
                        principalSchema: "maintenance",
                        principalTable: "maintenance_schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_assignments",
                schema: "maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    maintenance_task_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_maintenance_assignments", x => x.id);
                    table.CheckConstraint("CK_maintenance_assignments_completed_at", "started_at IS NULL OR completed_at IS NULL OR completed_at >= started_at");
                    table.CheckConstraint("CK_maintenance_assignments_ended_at", "ended_at IS NULL OR ended_at >= assigned_at");
                    table.CheckConstraint("CK_maintenance_assignments_started_at", "started_at IS NULL OR started_at >= assigned_at");
                    table.ForeignKey(
                        name: "FK_maintenance_assignments_maintenance_tasks_maintenance_task_~",
                        column: x => x.maintenance_task_id,
                        principalSchema: "maintenance",
                        principalTable: "maintenance_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_results",
                schema: "maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    maintenance_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_no = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    submitted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: false),
                    work_performed = table.Column<string>(type: "text", nullable: true),
                    issue_found = table.Column<string>(type: "text", nullable: true),
                    parts_or_resources_used = table.Column<string>(type: "text", nullable: true),
                    recommendation = table.Column<string>(type: "text", nullable: true),
                    result_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "SUBMITTED"),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    review_note = table.Column<string>(type: "text", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_results", x => x.id);
                    table.CheckConstraint("CK_maintenance_results_attempt_no", "attempt_no >= 1");
                    table.CheckConstraint("CK_maintenance_results_reviewed_at", "reviewed_at IS NULL OR reviewed_at >= submitted_at");
                    table.ForeignKey(
                        name: "FK_maintenance_results_maintenance_tasks_maintenance_task_id",
                        column: x => x.maintenance_task_id,
                        principalSchema: "maintenance",
                        principalTable: "maintenance_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_task_activities",
                schema: "maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    maintenance_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    activity_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    from_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    to_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    detail = table.Column<string>(type: "text", nullable: true),
                    performed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_task_activities", x => x.id);
                    table.ForeignKey(
                        name: "FK_maintenance_task_activities_maintenance_assignments_assignm~",
                        column: x => x.assignment_id,
                        principalSchema: "maintenance",
                        principalTable: "maintenance_assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_maintenance_task_activities_maintenance_tasks_maintenance_t~",
                        column: x => x.maintenance_task_id,
                        principalSchema: "maintenance",
                        principalTable: "maintenance_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_assignments_active_maintenance_task_id",
                schema: "maintenance",
                table: "maintenance_assignments",
                column: "maintenance_task_id",
                unique: true,
                filter: "\"status\" IN ('ASSIGNED', 'IN_PROGRESS')");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_assignments_assigned_at",
                schema: "maintenance",
                table: "maintenance_assignments",
                column: "assigned_at");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_assignments_staff_user_id",
                schema: "maintenance",
                table: "maintenance_assignments",
                column: "staff_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_assignments_status",
                schema: "maintenance",
                table: "maintenance_assignments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_results_maintenance_task_id_attempt_no",
                schema: "maintenance",
                table: "maintenance_results",
                columns: new[] { "maintenance_task_id", "attempt_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_results_result_status",
                schema: "maintenance",
                table: "maintenance_results",
                column: "result_status");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_results_reviewed_by",
                schema: "maintenance",
                table: "maintenance_results",
                column: "reviewed_by");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_schedules_building_id",
                schema: "maintenance",
                table: "maintenance_schedules",
                column: "building_id");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_schedules_equipment_id",
                schema: "maintenance",
                table: "maintenance_schedules",
                column: "equipment_id");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_schedules_facility_id",
                schema: "maintenance",
                table: "maintenance_schedules",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_schedules_planned_start_at",
                schema: "maintenance",
                table: "maintenance_schedules",
                column: "planned_start_at");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_schedules_schedule_code",
                schema: "maintenance",
                table: "maintenance_schedules",
                column: "schedule_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_schedules_status",
                schema: "maintenance",
                table: "maintenance_schedules",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_task_activities_activity_type",
                schema: "maintenance",
                table: "maintenance_task_activities",
                column: "activity_type");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_task_activities_assignment_id",
                schema: "maintenance",
                table: "maintenance_task_activities",
                column: "assignment_id");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_task_activities_created_at",
                schema: "maintenance",
                table: "maintenance_task_activities",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_task_activities_maintenance_task_id",
                schema: "maintenance",
                table: "maintenance_task_activities",
                column: "maintenance_task_id");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_tasks_building_id",
                schema: "maintenance",
                table: "maintenance_tasks",
                column: "building_id");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_tasks_due_at",
                schema: "maintenance",
                table: "maintenance_tasks",
                column: "due_at");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_tasks_equipment_id",
                schema: "maintenance",
                table: "maintenance_tasks",
                column: "equipment_id");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_tasks_priority_code",
                schema: "maintenance",
                table: "maintenance_tasks",
                column: "priority_code");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_tasks_schedule_id",
                schema: "maintenance",
                table: "maintenance_tasks",
                column: "schedule_id");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_tasks_source_complaint_id",
                schema: "maintenance",
                table: "maintenance_tasks",
                column: "source_complaint_id");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_tasks_source_service_request_id",
                schema: "maintenance",
                table: "maintenance_tasks",
                column: "source_service_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_tasks_status",
                schema: "maintenance",
                table: "maintenance_tasks",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_tasks_task_number",
                schema: "maintenance",
                table: "maintenance_tasks",
                column: "task_number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "maintenance_results",
                schema: "maintenance");

            migrationBuilder.DropTable(
                name: "maintenance_task_activities",
                schema: "maintenance");

            migrationBuilder.DropTable(
                name: "maintenance_assignments",
                schema: "maintenance");

            migrationBuilder.DropTable(
                name: "maintenance_tasks",
                schema: "maintenance");

            migrationBuilder.DropTable(
                name: "maintenance_schedules",
                schema: "maintenance");
        }
    }
}
