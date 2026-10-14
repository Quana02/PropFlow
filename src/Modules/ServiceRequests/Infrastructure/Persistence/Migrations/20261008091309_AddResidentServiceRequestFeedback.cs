using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.ServiceRequests.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResidentServiceRequestFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "service_request_feedbacks",
                schema: "service_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_request_feedbacks", x => x.id);
                    table.CheckConstraint("CK_service_request_feedbacks_rating", "rating BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_service_request_feedbacks_service_requests_service_request_~",
                        column: x => x.service_request_id,
                        principalSchema: "service_requests",
                        principalTable: "service_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_service_request_feedbacks_resident_id",
                schema: "service_requests",
                table: "service_request_feedbacks",
                column: "resident_id");

            migrationBuilder.CreateIndex(
                name: "IX_service_request_feedbacks_service_request_id",
                schema: "service_requests",
                table: "service_request_feedbacks",
                column: "service_request_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "service_request_feedbacks",
                schema: "service_requests");
        }
    }
}
