using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMonthlyWaterConsumption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "kwh",
                schema: "billing",
                table: "electricity_usages",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,3)",
                oldPrecision: 18,
                oldScale: 3);

            migrationBuilder.AddColumn<decimal>(
                name: "water_m3",
                schema: "billing",
                table: "electricity_usages",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_utility_usage_water",
                schema: "billing",
                table: "electricity_usages",
                sql: "water_m3 >= 0 AND water_m3 <= 1000000");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_utility_usage_water",
                schema: "billing",
                table: "electricity_usages");

            migrationBuilder.DropColumn(
                name: "water_m3",
                schema: "billing",
                table: "electricity_usages");

            migrationBuilder.AlterColumn<decimal>(
                name: "kwh",
                schema: "billing",
                table: "electricity_usages",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,3)",
                oldPrecision: 18,
                oldScale: 3,
                oldNullable: true);
        }
    }
}
