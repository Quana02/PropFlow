using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.Migrations;

/// <summary>Removes the retired building identifier from the singleton building profile.</summary>
public partial class RemoveBuildingCode : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_buildings_code",
            schema: "property_assets",
            table: "buildings");

        migrationBuilder.DropColumn(
            name: "code",
            schema: "property_assets",
            table: "buildings");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "code",
            schema: "property_assets",
            table: "buildings",
            type: "character varying(30)",
            maxLength: 30,
            nullable: false,
            defaultValue: string.Empty);

        migrationBuilder.CreateIndex(
            name: "IX_buildings_code",
            schema: "property_assets",
            table: "buildings",
            column: "code",
            unique: true);
    }
}
