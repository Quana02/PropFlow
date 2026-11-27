using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParkingSpaceListing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "listing_status",
                schema: "property_assets",
                table: "parking_spaces",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "AVAILABLE");

            migrationBuilder.AddColumn<decimal>(
                name: "price_vnd",
                schema: "property_assets",
                table: "parking_spaces",
                type: "numeric(18,0)",
                precision: 18,
                scale: 0,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_parking_space_listing_status",
                schema: "property_assets",
                table: "parking_spaces",
                sql: "listing_status IN ('AVAILABLE','PROCESSING','SOLD')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_parking_space_price",
                schema: "property_assets",
                table: "parking_spaces",
                sql: "price_vnd IS NULL OR price_vnd >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_parking_space_listing_status",
                schema: "property_assets",
                table: "parking_spaces");

            migrationBuilder.DropCheckConstraint(
                name: "ck_parking_space_price",
                schema: "property_assets",
                table: "parking_spaces");

            migrationBuilder.DropColumn(
                name: "listing_status",
                schema: "property_assets",
                table: "parking_spaces");

            migrationBuilder.DropColumn(
                name: "price_vnd",
                schema: "property_assets",
                table: "parking_spaces");
        }
    }
}
