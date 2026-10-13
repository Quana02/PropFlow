using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Maintenance.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MaintenanceDbContext))]
[Migration("20261012002000_ExpandMaintenanceIdentifierCodes")]
public partial class ExpandMaintenanceIdentifierCodes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "schedule_code", schema: "maintenance", table: "maintenance_schedules",
            type: "character varying(512)", maxLength: 512, nullable: false,
            oldClrType: typeof(string), oldType: "character varying(30)", oldMaxLength: 30);

        migrationBuilder.AlterColumn<string>(
            name: "task_number", schema: "maintenance", table: "maintenance_tasks",
            type: "character varying(512)", maxLength: 512, nullable: false,
            oldClrType: typeof(string), oldType: "character varying(30)", oldMaxLength: 30);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "schedule_code", schema: "maintenance", table: "maintenance_schedules",
            type: "character varying(30)", maxLength: 30, nullable: false,
            oldClrType: typeof(string), oldType: "character varying(512)", oldMaxLength: 512);

        migrationBuilder.AlterColumn<string>(
            name: "task_number", schema: "maintenance", table: "maintenance_tasks",
            type: "character varying(30)", maxLength: 30, nullable: false,
            oldClrType: typeof(string), oldType: "character varying(512)", oldMaxLength: 512);
    }
}
