using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParkingWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parking_areas",
                schema: "property_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    version = table.Column<Guid>(type: "uuid", nullable: false),
                    draft_json = table.Column<string>(type: "jsonb", nullable: false),
                    published_json = table.Column<string>(type: "jsonb", nullable: true),
                    draft_source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parking_areas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "parking_actions",
                schema: "property_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parking_actions", x => x.id);
                    table.ForeignKey(
                        name: "FK_parking_actions_parking_areas_area_id",
                        column: x => x.area_id,
                        principalSchema: "property_assets",
                        principalTable: "parking_areas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parking_revisions",
                schema: "property_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    drawing_json = table.Column<string>(type: "jsonb", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_by = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parking_revisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_parking_revisions_parking_areas_area_id",
                        column: x => x.area_id,
                        principalSchema: "property_assets",
                        principalTable: "parking_areas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parking_sources",
                schema: "property_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    content_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    page = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parking_sources", x => x.id);
                    table.CheckConstraint("ck_parking_source_page", "page BETWEEN 1 AND 1000");
                    table.CheckConstraint("ck_parking_source_size", "octet_length(content) BETWEEN 1 AND 8388608");
                    table.CheckConstraint("ck_parking_source_type", "content_type IN ('image/png','image/jpeg','application/pdf')");
                    table.ForeignKey(
                        name: "FK_parking_sources_parking_areas_area_id",
                        column: x => x.area_id,
                        principalSchema: "property_assets",
                        principalTable: "parking_areas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parking_spaces",
                schema: "property_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    archived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parking_spaces", x => x.id);
                    table.CheckConstraint("ck_parking_space_status", "status IN ('AVAILABLE','IN_USE','MAINTENANCE')");
                    table.ForeignKey(
                        name: "FK_parking_spaces_parking_areas_area_id",
                        column: x => x.area_id,
                        principalSchema: "property_assets",
                        principalTable: "parking_areas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_parking_actions_area_id_at",
                schema: "property_assets",
                table: "parking_actions",
                columns: new[] { "area_id", "at" });

            migrationBuilder.CreateIndex(
                name: "IX_parking_revisions_area_id_published_at",
                schema: "property_assets",
                table: "parking_revisions",
                columns: new[] { "area_id", "published_at" });

            migrationBuilder.CreateIndex(
                name: "IX_parking_sources_area_id",
                schema: "property_assets",
                table: "parking_sources",
                column: "area_id");

            migrationBuilder.CreateIndex(
                name: "IX_parking_spaces_area_id_archived",
                schema: "property_assets",
                table: "parking_spaces",
                columns: new[] { "area_id", "archived" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parking_actions",
                schema: "property_assets");

            migrationBuilder.DropTable(
                name: "parking_revisions",
                schema: "property_assets");

            migrationBuilder.DropTable(
                name: "parking_sources",
                schema: "property_assets");

            migrationBuilder.DropTable(
                name: "parking_spaces",
                schema: "property_assets");

            migrationBuilder.DropTable(
                name: "parking_areas",
                schema: "property_assets");
        }
    }
}
