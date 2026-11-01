using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Maintenance.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MaintenanceDbContext))]
[Migration("20261012150000_AddMaintenanceResultAttachments")]
public partial class AddMaintenanceResultAttachments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "maintenance_result_attachments", schema: "maintenance",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                maintenance_result_id = table.Column<Guid>(type: "uuid", nullable: false),
                storage_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                size_bytes = table.Column<long>(type: "bigint", nullable: false),
                uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_maintenance_result_attachments", x => x.id);
                table.ForeignKey("FK_maintenance_result_attachments_maintenance_results_maintenance_result_id", x => x.maintenance_result_id,
                    principalSchema: "maintenance", principalTable: "maintenance_results", principalColumn: "id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(name: "IX_maintenance_result_attachments_maintenance_result_id", schema: "maintenance", table: "maintenance_result_attachments", column: "maintenance_result_id");
        migrationBuilder.CreateIndex(name: "IX_maintenance_result_attachments_storage_key", schema: "maintenance", table: "maintenance_result_attachments", column: "storage_key", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("maintenance_result_attachments", "maintenance");
}
