using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUtilityMeterReadings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "current_electricity_reading",
                schema: "billing",
                table: "electricity_usages",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "current_water_reading",
                schema: "billing",
                table: "electricity_usages",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "previous_electricity_reading",
                schema: "billing",
                table: "electricity_usages",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "previous_water_reading",
                schema: "billing",
                table: "electricity_usages",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_utility_electricity_readings",
                schema: "billing",
                table: "electricity_usages",
                sql: "(previous_electricity_reading IS NULL AND current_electricity_reading IS NULL) OR (previous_electricity_reading IS NOT NULL AND current_electricity_reading IS NOT NULL AND kwh IS NOT NULL AND previous_electricity_reading >= 0 AND current_electricity_reading <= 1000000 AND kwh = current_electricity_reading - previous_electricity_reading)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_utility_water_readings",
                schema: "billing",
                table: "electricity_usages",
                sql: "(previous_water_reading IS NULL AND current_water_reading IS NULL) OR (previous_water_reading IS NOT NULL AND current_water_reading IS NOT NULL AND water_m3 IS NOT NULL AND previous_water_reading >= 0 AND current_water_reading <= 1000000 AND water_m3 = current_water_reading - previous_water_reading)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_utility_electricity_readings",
                schema: "billing",
                table: "electricity_usages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_utility_water_readings",
                schema: "billing",
                table: "electricity_usages");

            migrationBuilder.DropColumn(
                name: "current_electricity_reading",
                schema: "billing",
                table: "electricity_usages");

            migrationBuilder.DropColumn(
                name: "current_water_reading",
                schema: "billing",
                table: "electricity_usages");

            migrationBuilder.DropColumn(
                name: "previous_electricity_reading",
                schema: "billing",
                table: "electricity_usages");

            migrationBuilder.DropColumn(
                name: "previous_water_reading",
                schema: "billing",
                table: "electricity_usages");
        }
    }
}
