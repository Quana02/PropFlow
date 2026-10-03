using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Apartments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeApartmentUnitTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "apartment_unit_types",
                schema: "apartments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apartment_unit_types", x => x.id);
                });

            migrationBuilder.AddColumn<Guid>(
                name: "apartment_unit_type_id",
                schema: "apartments",
                table: "apartment_units",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                INSERT INTO apartments.apartment_unit_types (id, name, created_at, updated_at)
                SELECT md5(lower(btrim(unit_type)))::uuid, min(btrim(unit_type)), now(), now()
                FROM apartments.apartment_units
                WHERE unit_type IS NOT NULL AND btrim(unit_type) <> ''
                GROUP BY lower(btrim(unit_type));

                UPDATE apartments.apartment_units unit
                SET apartment_unit_type_id = md5(lower(btrim(unit.unit_type)))::uuid;

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM apartments.apartment_units WHERE apartment_unit_type_id IS NULL) THEN
                        RAISE EXCEPTION 'Cannot migrate apartment unit without a non-empty unit_type';
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "apartment_unit_type_id",
                schema: "apartments",
                table: "apartment_units",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "unit_type",
                schema: "apartments",
                table: "apartment_units");

            migrationBuilder.CreateIndex(
                name: "IX_apartment_units_apartment_unit_type_id",
                schema: "apartments",
                table: "apartment_units",
                column: "apartment_unit_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_apartment_unit_types_name",
                schema: "apartments",
                table: "apartment_unit_types",
                column: "name",
                unique: true);

            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_apartment_unit_types_normalized_name ON apartments.apartment_unit_types (lower(btrim(name))); ");

            migrationBuilder.AddForeignKey(
                name: "FK_apartment_units_apartment_unit_types_apartment_unit_type_id",
                schema: "apartments",
                table: "apartment_units",
                column: "apartment_unit_type_id",
                principalSchema: "apartments",
                principalTable: "apartment_unit_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "unit_type",
                schema: "apartments",
                table: "apartment_units",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE apartments.apartment_units unit
                SET unit_type = type.name
                FROM apartments.apartment_unit_types type
                WHERE type.id = unit.apartment_unit_type_id;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "unit_type",
                schema: "apartments",
                table: "apartment_units",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldNullable: true);

            migrationBuilder.DropForeignKey(
                name: "FK_apartment_units_apartment_unit_types_apartment_unit_type_id",
                schema: "apartments",
                table: "apartment_units");

            migrationBuilder.DropTable(
                name: "apartment_unit_types",
                schema: "apartments");

            migrationBuilder.DropIndex(
                name: "IX_apartment_units_apartment_unit_type_id",
                schema: "apartments",
                table: "apartment_units");

            migrationBuilder.DropColumn(
                name: "apartment_unit_type_id",
                schema: "apartments",
                table: "apartment_units");

        }
    }
}
