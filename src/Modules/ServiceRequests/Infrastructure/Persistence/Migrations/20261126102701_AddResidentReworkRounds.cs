using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.ServiceRequests.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResidentReworkRounds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "rework_count",
                schema: "service_requests",
                table: "service_requests",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "rework_count",
                schema: "service_requests",
                table: "service_requests");
        }
    }
}
