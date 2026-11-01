using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.ServiceRequests.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffServiceRequestEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "service_request_evidence_files",
                schema: "service_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_request_evidence_files", x => x.id);
                    table.ForeignKey(
                        name: "FK_service_request_evidence_files_service_request_activities_a~",
                        column: x => x.activity_id,
                        principalSchema: "service_requests",
                        principalTable: "service_request_activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_request_evidence_files_service_requests_service_req~",
                        column: x => x.service_request_id,
                        principalSchema: "service_requests",
                        principalTable: "service_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_service_request_evidence_files_activity_id",
                schema: "service_requests",
                table: "service_request_evidence_files",
                column: "activity_id");

            migrationBuilder.CreateIndex(
                name: "IX_service_request_evidence_files_service_request_id",
                schema: "service_requests",
                table: "service_request_evidence_files",
                column: "service_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_service_request_evidence_files_storage_key",
                schema: "service_requests",
                table: "service_request_evidence_files",
                column: "storage_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "service_request_evidence_files",
                schema: "service_requests");
        }
    }
}
