using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PropFlow.Modules.Administration.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedFixedRbacPermissions : Migration
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
                    { new Guid("66666666-6666-4666-8666-666666666661"), "USE_RESIDENT_SERVICES", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Truy cập các chức năng tự phục vụ dành cho cư dân.", true, "Residents", "Sử dụng dịch vụ cư dân", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("66666666-6666-4666-8666-666666666662"), "PERFORM_ASSIGNED_OPERATIONS", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Xử lý các công việc vận hành được phân công.", true, "Operations", "Thực hiện công việc được giao", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("66666666-6666-4666-8666-666666666663"), "MANAGE_FINANCE", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Thực hiện nghiệp vụ hóa đơn và thanh toán.", true, "Finance", "Quản lý tài chính", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("66666666-6666-4666-8666-666666666664"), "MANAGE_OPERATIONS", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Quản lý dữ liệu và quy trình vận hành chung cư.", true, "Operations", "Quản lý vận hành", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("66666666-6666-4666-8666-666666666665"), "MANAGE_INTERNAL_ACCOUNTS", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Tạo và quản lý vai trò, trạng thái tài khoản Ban quản lý.", true, "Administration", "Quản lý tài khoản nội bộ", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("66666666-6666-4666-8666-666666666666"), "VIEW_ADMINISTRATION_ACTIVITY", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Xem lịch sử thay đổi tài khoản nội bộ.", true, "Administration", "Xem nhật ký quản trị", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("66666666-6666-4666-8666-666666666667"), "VIEW_SYSTEM_OVERVIEW", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Xem chỉ số tổng hợp toàn hệ thống ở chế độ chỉ đọc.", true, "Reporting", "Xem tổng quan hệ thống", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.InsertData(
                schema: "administration",
                table: "role_permissions",
                columns: new[] { "permission_id", "role_id", "created_at", "created_by" },
                values: new object[,]
                {
                    { new Guid("66666666-6666-4666-8666-666666666661"), new Guid("11111111-1111-4111-8111-111111111111"), new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("66666666-6666-4666-8666-666666666662"), new Guid("22222222-2222-4222-8222-222222222222"), new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("66666666-6666-4666-8666-666666666663"), new Guid("33333333-3333-4333-8333-333333333333"), new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("66666666-6666-4666-8666-666666666664"), new Guid("44444444-4444-4444-8444-444444444444"), new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("66666666-6666-4666-8666-666666666665"), new Guid("55555555-5555-4555-8555-555555555555"), new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("66666666-6666-4666-8666-666666666666"), new Guid("55555555-5555-4555-8555-555555555555"), new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("66666666-6666-4666-8666-666666666667"), new Guid("55555555-5555-4555-8555-555555555555"), new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "administration",
                table: "role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("66666666-6666-4666-8666-666666666661"), new Guid("11111111-1111-4111-8111-111111111111") });

            migrationBuilder.DeleteData(
                schema: "administration",
                table: "role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("66666666-6666-4666-8666-666666666662"), new Guid("22222222-2222-4222-8222-222222222222") });

            migrationBuilder.DeleteData(
                schema: "administration",
                table: "role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("66666666-6666-4666-8666-666666666663"), new Guid("33333333-3333-4333-8333-333333333333") });

            migrationBuilder.DeleteData(
                schema: "administration",
                table: "role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("66666666-6666-4666-8666-666666666664"), new Guid("44444444-4444-4444-8444-444444444444") });

            migrationBuilder.DeleteData(
                schema: "administration",
                table: "role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("66666666-6666-4666-8666-666666666665"), new Guid("55555555-5555-4555-8555-555555555555") });

            migrationBuilder.DeleteData(
                schema: "administration",
                table: "role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("66666666-6666-4666-8666-666666666666"), new Guid("55555555-5555-4555-8555-555555555555") });

            migrationBuilder.DeleteData(
                schema: "administration",
                table: "role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("66666666-6666-4666-8666-666666666667"), new Guid("55555555-5555-4555-8555-555555555555") });

            migrationBuilder.DeleteData(
                schema: "administration",
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("66666666-6666-4666-8666-666666666661"));

            migrationBuilder.DeleteData(
                schema: "administration",
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("66666666-6666-4666-8666-666666666662"));

            migrationBuilder.DeleteData(
                schema: "administration",
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("66666666-6666-4666-8666-666666666663"));

            migrationBuilder.DeleteData(
                schema: "administration",
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("66666666-6666-4666-8666-666666666664"));

            migrationBuilder.DeleteData(
                schema: "administration",
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("66666666-6666-4666-8666-666666666665"));

            migrationBuilder.DeleteData(
                schema: "administration",
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("66666666-6666-4666-8666-666666666666"));

            migrationBuilder.DeleteData(
                schema: "administration",
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("66666666-6666-4666-8666-666666666667"));
        }
    }
}
