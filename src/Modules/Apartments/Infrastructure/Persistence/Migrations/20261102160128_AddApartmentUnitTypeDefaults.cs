using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Apartments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApartmentUnitTypeDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "default_bathroom_count",
                schema: "apartments",
                table: "apartment_unit_types",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "default_bedroom_count",
                schema: "apartments",
                table: "apartment_unit_types",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "default_usable_area_m2",
                schema: "apartments",
                table: "apartment_unit_types",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_apartment_type_default_area",
                schema: "apartments",
                table: "apartment_unit_types",
                sql: "default_usable_area_m2 IS NULL OR default_usable_area_m2 >= 25");

            migrationBuilder.AddCheckConstraint(
                name: "ck_apartment_type_default_bathrooms",
                schema: "apartments",
                table: "apartment_unit_types",
                sql: "default_bathroom_count IS NULL OR default_bathroom_count >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_apartment_type_default_bedrooms",
                schema: "apartments",
                table: "apartment_unit_types",
                sql: "default_bedroom_count IS NULL OR default_bedroom_count >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_apartment_type_default_area",
                schema: "apartments",
                table: "apartment_unit_types");

            migrationBuilder.DropCheckConstraint(
                name: "ck_apartment_type_default_bathrooms",
                schema: "apartments",
                table: "apartment_unit_types");

            migrationBuilder.DropCheckConstraint(
                name: "ck_apartment_type_default_bedrooms",
                schema: "apartments",
                table: "apartment_unit_types");

            migrationBuilder.DropColumn(
                name: "default_bathroom_count",
                schema: "apartments",
                table: "apartment_unit_types");

            migrationBuilder.DropColumn(
                name: "default_bedroom_count",
                schema: "apartments",
                table: "apartment_unit_types");

            migrationBuilder.DropColumn(
                name: "default_usable_area_m2",
                schema: "apartments",
                table: "apartment_unit_types");
        }
    }
}
