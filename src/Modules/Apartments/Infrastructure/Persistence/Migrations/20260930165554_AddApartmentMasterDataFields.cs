using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Apartments.Infrastructure.Persistence.Migrations;

/// <summary>Promotes apartment units to independent master records without losing legacy area data.</summary>
public partial class AddApartmentMasterDataFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "area_m2",
            schema: "apartments",
            table: "apartment_units",
            newName: "usable_area_m2");

        migrationBuilder.AlterColumn<decimal>(
            name: "usable_area_m2",
            schema: "apartments",
            table: "apartment_units",
            type: "numeric(10,2)",
            precision: 10,
            scale: 2,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(10,2)",
            oldPrecision: 10,
            oldScale: 2,
            oldNullable: true);

        migrationBuilder.AddColumn<string>(
            name: "unit_type",
            schema: "apartments",
            table: "apartment_units",
            type: "character varying(80)",
            maxLength: 80,
            nullable: false,
            defaultValue: "STANDARD");

        migrationBuilder.AddColumn<int>(name: "bathroom_count", schema: "apartments", table: "apartment_units", type: "integer", nullable: true);
        migrationBuilder.AddColumn<DateOnly>(name: "handover_date", schema: "apartments", table: "apartment_units", type: "date", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "bathroom_count", schema: "apartments", table: "apartment_units");
        migrationBuilder.DropColumn(name: "handover_date", schema: "apartments", table: "apartment_units");
        migrationBuilder.DropColumn(name: "unit_type", schema: "apartments", table: "apartment_units");
        migrationBuilder.RenameColumn(name: "usable_area_m2", schema: "apartments", table: "apartment_units", newName: "area_m2");
        migrationBuilder.AlterColumn<decimal>(name: "area_m2", schema: "apartments", table: "apartment_units", type: "numeric(10,2)", precision: 10, scale: 2, nullable: true, oldClrType: typeof(decimal), oldType: "numeric(10,2)", oldPrecision: 10, oldScale: 2);
    }
}
