using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PropFlow.Modules.ServiceRequests.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedResidentServiceRequestCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "service_requests",
                table: "service_request_categories",
                columns: new[] { "id", "code", "created_at", "created_by", "description", "display_order", "is_active", "name", "updated_at", "updated_by" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-4000-8000-000000000501"), "REPAIR", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, 10, true, "Sửa chữa kỹ thuật", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("00000000-0000-4000-8000-000000000502"), "CLEANING", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, 20, true, "Dịch vụ vệ sinh", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("00000000-0000-4000-8000-000000000503"), "VISITOR", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, 30, true, "Đăng ký khách thăm", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("00000000-0000-4000-8000-000000000504"), "VEHICLE", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, 40, true, "Đăng ký thẻ xe", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("00000000-0000-4000-8000-000000000505"), "MOVING", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, 50, true, "Chuyển đồ / Chuyển nhà", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("00000000-0000-4000-8000-000000000506"), "CONSTRUCTION", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, 60, true, "Thi công nội thất", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("00000000-0000-4000-8000-000000000507"), "BILLING", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, 70, true, "Hỗ trợ hóa đơn", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("00000000-0000-4000-8000-000000000508"), "OTHER", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, 80, true, "Hỗ trợ khác", new DateTimeOffset(new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "service_requests",
                table: "service_request_categories",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000501"));

            migrationBuilder.DeleteData(
                schema: "service_requests",
                table: "service_request_categories",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000502"));

            migrationBuilder.DeleteData(
                schema: "service_requests",
                table: "service_request_categories",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000503"));

            migrationBuilder.DeleteData(
                schema: "service_requests",
                table: "service_request_categories",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000504"));

            migrationBuilder.DeleteData(
                schema: "service_requests",
                table: "service_request_categories",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000505"));

            migrationBuilder.DeleteData(
                schema: "service_requests",
                table: "service_request_categories",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000506"));

            migrationBuilder.DeleteData(
                schema: "service_requests",
                table: "service_request_categories",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000507"));

            migrationBuilder.DeleteData(
                schema: "service_requests",
                table: "service_request_categories",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000508"));
        }
    }
}
