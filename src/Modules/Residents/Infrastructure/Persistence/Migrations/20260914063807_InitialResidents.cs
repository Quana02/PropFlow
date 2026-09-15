using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Residents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialResidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "residents");

            migrationBuilder.CreateTable(
                name: "residents",
                schema: "residents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resident_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    full_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "ACTIVE"),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_residents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "resident_apartments",
                schema: "residents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    resident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    apartment_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    relationship_type_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "ACTIVE"),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resident_apartments", x => x.id);
                    table.ForeignKey(
                        name: "FK_resident_apartments_residents_resident_id",
                        column: x => x.resident_id,
                        principalSchema: "residents",
                        principalTable: "residents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_resident_apartments_apartment_unit_id",
                schema: "residents",
                table: "resident_apartments",
                column: "apartment_unit_id");

            migrationBuilder.CreateIndex(
                name: "IX_resident_apartments_resident_id",
                schema: "residents",
                table: "resident_apartments",
                column: "resident_id");

            migrationBuilder.CreateIndex(
                name: "IX_resident_apartments_resident_id_apartment_unit_id_start_date",
                schema: "residents",
                table: "resident_apartments",
                columns: new[] { "resident_id", "apartment_unit_id", "start_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_resident_apartments_status",
                schema: "residents",
                table: "resident_apartments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_residents_full_name",
                schema: "residents",
                table: "residents",
                column: "full_name");

            migrationBuilder.CreateIndex(
                name: "IX_residents_phone_number",
                schema: "residents",
                table: "residents",
                column: "phone_number");

            migrationBuilder.CreateIndex(
                name: "IX_residents_resident_code",
                schema: "residents",
                table: "residents",
                column: "resident_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_residents_status",
                schema: "residents",
                table: "residents",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_residents_user_id",
                schema: "residents",
                table: "residents",
                column: "user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "resident_apartments",
                schema: "residents");

            migrationBuilder.DropTable(
                name: "residents",
                schema: "residents");
        }
    }
}
