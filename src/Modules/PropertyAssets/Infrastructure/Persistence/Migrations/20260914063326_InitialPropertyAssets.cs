using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPropertyAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "property_assets");

            migrationBuilder.CreateTable(
                name: "buildings",
                schema: "property_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    address = table.Column<string>(type: "text", nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "Asia/Ho_Chi_Minh"),
                    number_of_floors = table.Column<int>(type: "integer", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "ACTIVE"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_buildings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "facilities",
                schema: "property_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    building_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    facility_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    location_description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "ACTIVE"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_facilities", x => x.id);
                    table.ForeignKey(
                        name: "FK_facilities_buildings_building_id",
                        column: x => x.building_id,
                        principalSchema: "property_assets",
                        principalTable: "buildings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "equipment",
                schema: "property_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    building_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    equipment_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    manufacturer = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    serial_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    installation_date = table.Column<DateOnly>(type: "date", nullable: true),
                    warranty_expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    location_description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "ACTIVE"),
                    description = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_equipment", x => x.id);
                    table.ForeignKey(
                        name: "FK_equipment_buildings_building_id",
                        column: x => x.building_id,
                        principalSchema: "property_assets",
                        principalTable: "buildings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_equipment_facilities_facility_id",
                        column: x => x.facility_id,
                        principalSchema: "property_assets",
                        principalTable: "facilities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_buildings_code",
                schema: "property_assets",
                table: "buildings",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_buildings_name",
                schema: "property_assets",
                table: "buildings",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "IX_buildings_status",
                schema: "property_assets",
                table: "buildings",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_building_id_code",
                schema: "property_assets",
                table: "equipment",
                columns: new[] { "building_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_equipment_equipment_type",
                schema: "property_assets",
                table: "equipment",
                column: "equipment_type");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_facility_id",
                schema: "property_assets",
                table: "equipment",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_status",
                schema: "property_assets",
                table: "equipment",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_facilities_building_id",
                schema: "property_assets",
                table: "facilities",
                column: "building_id");

            migrationBuilder.CreateIndex(
                name: "IX_facilities_building_id_code",
                schema: "property_assets",
                table: "facilities",
                columns: new[] { "building_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_facilities_facility_type",
                schema: "property_assets",
                table: "facilities",
                column: "facility_type");

            migrationBuilder.CreateIndex(
                name: "IX_facilities_status",
                schema: "property_assets",
                table: "facilities",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "equipment",
                schema: "property_assets");

            migrationBuilder.DropTable(
                name: "facilities",
                schema: "property_assets");

            migrationBuilder.DropTable(
                name: "buildings",
                schema: "property_assets");
        }
    }
}
