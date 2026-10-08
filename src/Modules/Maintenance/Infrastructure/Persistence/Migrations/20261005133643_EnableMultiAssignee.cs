using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Maintenance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnableMultiAssignee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_maintenance_assignments_active_maintenance_task_id",
                schema: "maintenance",
                table: "maintenance_assignments");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_assignments_active_task_staff",
                schema: "maintenance",
                table: "maintenance_assignments",
                columns: new[] { "maintenance_task_id", "staff_user_id" },
                unique: true,
                filter: "\"status\" IN ('ASSIGNED', 'IN_PROGRESS')");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_assignments_maintenance_task_id",
                schema: "maintenance",
                table: "maintenance_assignments",
                column: "maintenance_task_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_maintenance_assignments_active_task_staff",
                schema: "maintenance",
                table: "maintenance_assignments");

            migrationBuilder.DropIndex(
                name: "IX_maintenance_assignments_maintenance_task_id",
                schema: "maintenance",
                table: "maintenance_assignments");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_assignments_active_maintenance_task_id",
                schema: "maintenance",
                table: "maintenance_assignments",
                column: "maintenance_task_id",
                unique: true,
                filter: "\"status\" IN ('ASSIGNED', 'IN_PROGRESS')");
        }
    }
}
