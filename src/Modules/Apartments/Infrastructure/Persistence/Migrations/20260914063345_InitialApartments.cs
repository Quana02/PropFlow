using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Apartments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialApartments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "apartments");

            migrationBuilder.CreateTable(
                name: "apartment_units",
                schema: "apartments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    building_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    floor_number = table.Column<int>(type: "integer", nullable: false),
                    area_m2 = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    bedroom_count = table.Column<int>(type: "integer", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "ACTIVE"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apartment_units", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_apartment_units_building_id_unit_number",
                schema: "apartments",
                table: "apartment_units",
                columns: new[] { "building_id", "unit_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_apartment_units_floor_number",
                schema: "apartments",
                table: "apartment_units",
                column: "floor_number");

            migrationBuilder.CreateIndex(
                name: "IX_apartment_units_status",
                schema: "apartments",
                table: "apartment_units",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "apartment_units",
                schema: "apartments");
        }
    }
}
