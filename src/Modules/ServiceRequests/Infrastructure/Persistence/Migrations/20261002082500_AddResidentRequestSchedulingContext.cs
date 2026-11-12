using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.ServiceRequests.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResidentRequestSchedulingContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "preferred_date",
                schema: "service_requests",
                table: "service_requests",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "preferred_time_code",
                schema: "service_requests",
                table: "service_requests",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "service_area_code",
                schema: "service_requests",
                table: "service_requests",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "preferred_date",
                schema: "service_requests",
                table: "service_requests");

            migrationBuilder.DropColumn(
                name: "preferred_time_code",
                schema: "service_requests",
                table: "service_requests");

            migrationBuilder.DropColumn(
                name: "service_area_code",
                schema: "service_requests",
                table: "service_requests");
        }
    }
}
