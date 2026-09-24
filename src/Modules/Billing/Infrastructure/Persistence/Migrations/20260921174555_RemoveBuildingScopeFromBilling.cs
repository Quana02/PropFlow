using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBuildingScopeFromBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fee_rate_rules_building_id",
                schema: "billing",
                table: "fee_rate_rules");

            migrationBuilder.DropIndex(
                name: "IX_fee_rate_rules_fee_type_id_building_id_effective_from",
                schema: "billing",
                table: "fee_rate_rules");

            migrationBuilder.DropColumn(
                name: "building_id",
                schema: "billing",
                table: "fee_rate_rules");

            migrationBuilder.CreateIndex(
                name: "IX_fee_rate_rules_fee_type_id_effective_from",
                schema: "billing",
                table: "fee_rate_rules",
                columns: new[] { "fee_type_id", "effective_from" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fee_rate_rules_fee_type_id_effective_from",
                schema: "billing",
                table: "fee_rate_rules");

            migrationBuilder.AddColumn<Guid>(
                name: "building_id",
                schema: "billing",
                table: "fee_rate_rules",
                type: "uuid",
                nullable: true);

            // Validate singleton building exists and get its ID
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    building_count integer;
                    singleton_building_id uuid;
                BEGIN
                    SELECT COUNT(*)
                    INTO building_count
                    FROM property_assets.buildings;

                    IF building_count <> 1 THEN
                        RAISE EXCEPTION
                            'Rollback requires exactly one building, found %',
                            building_count;
                    END IF;

                    SELECT id
                    INTO singleton_building_id
                    FROM property_assets.buildings
                    LIMIT 1;

                    UPDATE billing.fee_rate_rules
                    SET building_id = singleton_building_id
                    WHERE building_id IS NULL;
                END $$;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_fee_rate_rules_building_id",
                schema: "billing",
                table: "fee_rate_rules",
                column: "building_id");

            migrationBuilder.CreateIndex(
                name: "IX_fee_rate_rules_fee_type_id_building_id_effective_from",
                schema: "billing",
                table: "fee_rate_rules",
                columns: new[] { "fee_type_id", "building_id", "effective_from" });
        }
    }
}
