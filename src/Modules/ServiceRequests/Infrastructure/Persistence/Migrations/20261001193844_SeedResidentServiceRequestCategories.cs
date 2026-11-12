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
            // Existing local databases may already contain these codes with referenced IDs.
            // Keep those rows intact and add only missing categories.
            migrationBuilder.Sql(@"
                INSERT INTO service_requests.service_request_categories
                    (id, code, created_at, created_by, description, display_order, is_active, name, updated_at, updated_by)
                SELECT v.id, v.code, TIMESTAMPTZ '2026-09-14 00:00:00+00', NULL, NULL, v.display_order, true, v.name,
                    TIMESTAMPTZ '2026-09-14 00:00:00+00', NULL
                FROM (VALUES
                    ('00000000-0000-4000-8000-000000000501'::uuid, 'REPAIR', 10, 'Sửa chữa kỹ thuật'),
                    ('00000000-0000-4000-8000-000000000502'::uuid, 'CLEANING', 20, 'Dịch vụ vệ sinh'),
                    ('00000000-0000-4000-8000-000000000503'::uuid, 'VISITOR', 30, 'Đăng ký khách thăm'),
                    ('00000000-0000-4000-8000-000000000504'::uuid, 'VEHICLE', 40, 'Đăng ký thẻ xe'),
                    ('00000000-0000-4000-8000-000000000505'::uuid, 'MOVING', 50, 'Chuyển đồ / Chuyển nhà'),
                    ('00000000-0000-4000-8000-000000000506'::uuid, 'CONSTRUCTION', 60, 'Thi công nội thất'),
                    ('00000000-0000-4000-8000-000000000507'::uuid, 'BILLING', 70, 'Hỗ trợ hóa đơn'),
                    ('00000000-0000-4000-8000-000000000508'::uuid, 'OTHER', 80, 'Hỗ trợ khác')
                ) AS v(id, code, display_order, name)
                ON CONFLICT (code) DO NOTHING;
            ");
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
