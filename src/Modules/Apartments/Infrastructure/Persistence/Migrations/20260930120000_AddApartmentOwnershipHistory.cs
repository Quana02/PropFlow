using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PropFlow.Modules.Apartments.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ApartmentsDbContext))]
[Migration("20260930120000_AddApartmentOwnershipHistory")]
public sealed class AddApartmentOwnershipHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "apartment_ownerships", schema: "apartments",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                apartment_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                owner_resident_id = table.Column<Guid>(type: "uuid", nullable: false),
                start_date = table.Column<DateOnly>(type: "date", nullable: false),
                end_date = table.Column<DateOnly>(type: "date", nullable: true),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_apartment_ownerships", x => x.id);
                table.ForeignKey(
                    name: "FK_apartment_ownerships_apartment_units_apartment_unit_id",
                    column: x => x.apartment_unit_id,
                    principalTable: "apartment_units",
                    principalColumn: "id",
                    principalSchema: "apartments",
                    onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_apartment_ownerships_apartment_unit_id_start_date", "apartment_ownerships", new[] { "apartment_unit_id", "start_date" }, schema: "apartments", unique: true);
        migrationBuilder.CreateIndex("IX_apartment_ownerships_owner_resident_id", "apartment_ownerships", "owner_resident_id", schema: "apartments");
        migrationBuilder.Sql("CREATE UNIQUE INDEX ux_apartment_ownership_current ON apartments.apartment_ownerships(apartment_unit_id) WHERE end_date IS NULL;");
        migrationBuilder.Sql("ALTER TABLE apartments.apartment_ownerships ADD CONSTRAINT ck_apartment_ownership_dates CHECK (end_date IS NULL OR end_date >= start_date);");
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("apartment_ownerships", "apartments");
}
