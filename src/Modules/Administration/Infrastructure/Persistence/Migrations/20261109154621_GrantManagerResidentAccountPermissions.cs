using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PropFlow.Modules.Administration.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GrantManagerResidentAccountPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "administration",
                table: "permissions",
                columns: new[] { "id", "code", "created_at", "description", "is_active", "module", "name", "updated_at" },
                values: new object[,]
                {
                    { new Guid("66666666-6666-4666-8666-666666666669"), "READ_RESIDENT_ACCOUNTS", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Xem tài khoản gắn với hồ sơ cư dân trong deployment.", true, "Authentication", "Xem tài khoản cư dân", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("66666666-6666-4666-8666-666666666670"), "REQUEST_RESIDENT_EMAIL_CHANGE", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Khởi tạo yêu cầu sau khi đối chiếu danh tính cư dân trực tiếp.", true, "Authentication", "Yêu cầu đổi email tài khoản cư dân", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.InsertData(
                schema: "administration",
                table: "role_permissions",
                columns: new[] { "permission_id", "role_id", "created_at", "created_by" },
                values: new object[,]
                {
                    { new Guid("66666666-6666-4666-8666-666666666669"), new Guid("44444444-4444-4444-8444-444444444444"), new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("66666666-6666-4666-8666-666666666670"), new Guid("44444444-4444-4444-8444-444444444444"), new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "administration",
                table: "role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("66666666-6666-4666-8666-666666666669"), new Guid("44444444-4444-4444-8444-444444444444") });

            migrationBuilder.DeleteData(
                schema: "administration",
                table: "role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("66666666-6666-4666-8666-666666666670"), new Guid("44444444-4444-4444-8444-444444444444") });

            migrationBuilder.DeleteData(
                schema: "administration",
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("66666666-6666-4666-8666-666666666669"));

            migrationBuilder.DeleteData(
                schema: "administration",
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("66666666-6666-4666-8666-666666666670"));
        }
    }
}
