using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Complaints.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialComplaints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "complaints");

            migrationBuilder.CreateTable(
                name: "complaints",
                schema: "complaints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    complaint_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    resident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resident_apartment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    building_id = table.Column<Guid>(type: "uuid", nullable: false),
                    apartment_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    related_service_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    equipment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "SUBMITTED"),
                    official_response = table.Column<string>(type: "text", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_complaints", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "complaint_followups",
                schema: "complaints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    complaint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_by = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    instruction = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "ASSIGNED"),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    result = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_complaint_followups", x => x.id);
                    table.ForeignKey(
                        name: "FK_complaint_followups_complaints_complaint_id",
                        column: x => x.complaint_id,
                        principalSchema: "complaints",
                        principalTable: "complaints",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "complaint_activities",
                schema: "complaints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    complaint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    followup_id = table.Column<Guid>(type: "uuid", nullable: true),
                    activity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    from_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    to_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    detail = table.Column<string>(type: "text", nullable: true),
                    performed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_complaint_activities", x => x.id);
                    table.ForeignKey(
                        name: "FK_complaint_activities_complaint_followups_followup_id",
                        column: x => x.followup_id,
                        principalSchema: "complaints",
                        principalTable: "complaint_followups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_complaint_activities_complaints_complaint_id",
                        column: x => x.complaint_id,
                        principalSchema: "complaints",
                        principalTable: "complaints",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_complaint_activities_activity_type",
                schema: "complaints",
                table: "complaint_activities",
                column: "activity_type");

            migrationBuilder.CreateIndex(
                name: "IX_complaint_activities_complaint_id",
                schema: "complaints",
                table: "complaint_activities",
                column: "complaint_id");

            migrationBuilder.CreateIndex(
                name: "IX_complaint_activities_created_at",
                schema: "complaints",
                table: "complaint_activities",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_complaint_activities_followup_id",
                schema: "complaints",
                table: "complaint_activities",
                column: "followup_id");

            migrationBuilder.CreateIndex(
                name: "IX_complaint_followups_complaint_id",
                schema: "complaints",
                table: "complaint_followups",
                column: "complaint_id");

            migrationBuilder.CreateIndex(
                name: "IX_complaint_followups_due_at",
                schema: "complaints",
                table: "complaint_followups",
                column: "due_at");

            migrationBuilder.CreateIndex(
                name: "IX_complaint_followups_staff_user_id",
                schema: "complaints",
                table: "complaint_followups",
                column: "staff_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_complaint_followups_status",
                schema: "complaints",
                table: "complaint_followups",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_complaints_apartment_unit_id",
                schema: "complaints",
                table: "complaints",
                column: "apartment_unit_id");

            migrationBuilder.CreateIndex(
                name: "IX_complaints_building_id",
                schema: "complaints",
                table: "complaints",
                column: "building_id");

            migrationBuilder.CreateIndex(
                name: "IX_complaints_complaint_number",
                schema: "complaints",
                table: "complaints",
                column: "complaint_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_complaints_related_service_request_id",
                schema: "complaints",
                table: "complaints",
                column: "related_service_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_complaints_resident_id",
                schema: "complaints",
                table: "complaints",
                column: "resident_id");

            migrationBuilder.CreateIndex(
                name: "IX_complaints_status",
                schema: "complaints",
                table: "complaints",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_complaints_submitted_at",
                schema: "complaints",
                table: "complaints",
                column: "submitted_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "complaint_activities",
                schema: "complaints");

            migrationBuilder.DropTable(
                name: "complaint_followups",
                schema: "complaints");

            migrationBuilder.DropTable(
                name: "complaints",
                schema: "complaints");
        }
    }
}
