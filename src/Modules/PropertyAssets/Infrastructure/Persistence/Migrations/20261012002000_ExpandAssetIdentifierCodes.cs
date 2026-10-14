using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PropertyAssetsDbContext))]
[Migration("20261012002000_ExpandAssetIdentifierCodes")]
public partial class ExpandAssetIdentifierCodes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "code", schema: "property_assets", table: "facilities",
            type: "character varying(512)", maxLength: 512, nullable: false,
            oldClrType: typeof(string), oldType: "character varying(30)", oldMaxLength: 30);

        migrationBuilder.AlterColumn<string>(
            name: "code", schema: "property_assets", table: "equipment",
            type: "character varying(512)", maxLength: 512, nullable: false,
            oldClrType: typeof(string), oldType: "character varying(50)", oldMaxLength: 50);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "code", schema: "property_assets", table: "facilities",
            type: "character varying(30)", maxLength: 30, nullable: false,
            oldClrType: typeof(string), oldType: "character varying(512)", oldMaxLength: 512);

        migrationBuilder.AlterColumn<string>(
            name: "code", schema: "property_assets", table: "equipment",
            type: "character varying(50)", maxLength: 50, nullable: false,
            oldClrType: typeof(string), oldType: "character varying(512)", oldMaxLength: 512);
    }
}
