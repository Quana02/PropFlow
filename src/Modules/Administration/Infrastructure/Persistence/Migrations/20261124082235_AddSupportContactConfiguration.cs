using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Administration.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportContactConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "version",
                schema: "administration",
                table: "system_configurations",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.InsertData(
                schema: "administration",
                table: "permissions",
                columns: new[] { "id", "code", "created_at", "description", "is_active", "module", "name", "updated_at" },
                values: new object[] { new Guid("66666666-6666-4666-8666-666666666671"), "MANAGE_SYSTEM_CONFIGURATION", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Quản lý email và số điện thoại hỗ trợ được công khai.", true, "Administration", "Quản lý liên hệ hỗ trợ", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });

            migrationBuilder.InsertData(
                schema: "administration",
                table: "role_permissions",
                columns: new[] { "permission_id", "role_id", "created_at", "created_by" },
                values: new object[] { new Guid("66666666-6666-4666-8666-666666666671"), new Guid("55555555-5555-4555-8555-555555555555"), new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "administration",
                table: "role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("66666666-6666-4666-8666-666666666671"), new Guid("55555555-5555-4555-8555-555555555555") });

            migrationBuilder.DeleteData(
                schema: "administration",
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("66666666-6666-4666-8666-666666666671"));

            migrationBuilder.DropColumn(
                name: "version",
                schema: "administration",
                table: "system_configurations");
        }
    }
}
