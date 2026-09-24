using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PropertyAssetsDbContext))]
[Migration("20260924130000_EnforceCurrentBuildingSingleton")]
public sealed class EnforceCurrentBuildingSingleton : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            DO $$
            DECLARE building_count int;
            BEGIN
                SELECT COUNT(*) INTO building_count FROM property_assets.buildings;
                IF building_count > 1 THEN
                    RAISE EXCEPTION 'Cannot enforce current-condominium deployment: expected at most 1 building, found %', building_count;
                END IF;
            END $$;");

        migrationBuilder.Sql(
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_buildings_singleton\" ON property_assets.buildings ((true));");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS property_assets.\"IX_buildings_singleton\";");
    }
}
