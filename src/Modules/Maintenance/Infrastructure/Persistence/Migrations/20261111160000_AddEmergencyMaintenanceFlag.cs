using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Maintenance.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MaintenanceDbContext))]
[Migration("20261111160000_AddEmergencyMaintenanceFlag")]
public partial class AddEmergencyMaintenanceFlag : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "is_emergency",
            schema: "maintenance",
            table: "maintenance_tasks",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "is_emergency",
            schema: "maintenance",
            table: "maintenance_tasks");
    }
}
