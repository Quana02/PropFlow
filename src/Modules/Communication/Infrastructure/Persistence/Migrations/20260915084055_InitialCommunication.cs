using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Communication.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCommunication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "communication");

            migrationBuilder.CreateTable(
                name: "announcements",
                schema: "communication",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "DRAFT"),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    withdrawal_reason = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_announcements", x => x.id);
                    table.CheckConstraint("CK_announcements_content_not_blank", "length(btrim(content)) > 0");
                    table.CheckConstraint("CK_announcements_published_state", "(status = 'DRAFT' AND published_at IS NULL AND withdrawn_at IS NULL AND withdrawal_reason IS NULL) OR (status = 'PUBLISHED' AND published_at IS NOT NULL AND withdrawn_at IS NULL AND withdrawal_reason IS NULL AND published_at >= created_at) OR (status = 'WITHDRAWN' AND published_at IS NOT NULL AND withdrawn_at IS NOT NULL AND withdrawal_reason IS NOT NULL AND published_at >= created_at AND withdrawn_at >= published_at)");
                    table.CheckConstraint("CK_announcements_title_not_blank", "length(btrim(title)) > 0");
                    table.CheckConstraint("CK_announcements_updated_at", "updated_at >= created_at");
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "communication",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, defaultValue: "SYSTEM"),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    source_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_read = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.id);
                    table.CheckConstraint("CK_notifications_message_not_blank", "length(btrim(message)) > 0");
                    table.CheckConstraint("CK_notifications_read_state", "(is_read = false AND read_at IS NULL) OR (is_read = true AND read_at IS NOT NULL AND read_at >= created_at)");
                    table.CheckConstraint("CK_notifications_source_reference", "(source_type IS NULL AND source_id IS NULL) OR (source_type IS NOT NULL AND source_id IS NOT NULL)");
                    table.CheckConstraint("CK_notifications_title_not_blank", "length(btrim(title)) > 0");
                });

            migrationBuilder.CreateTable(
                name: "announcement_audiences",
                schema: "communication",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    announcement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audience_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    building_id = table.Column<Guid>(type: "uuid", nullable: true),
                    apartment_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resident_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_announcement_audiences", x => x.id);
                    table.CheckConstraint("CK_announcement_audiences_target", "(audience_type IN ('ALL_USERS', 'ALL_RESIDENTS') AND role_id IS NULL AND building_id IS NULL AND apartment_unit_id IS NULL AND resident_id IS NULL) OR (audience_type = 'ROLE' AND role_id IS NOT NULL AND building_id IS NULL AND apartment_unit_id IS NULL AND resident_id IS NULL) OR (audience_type = 'BUILDING' AND role_id IS NULL AND building_id IS NOT NULL AND apartment_unit_id IS NULL AND resident_id IS NULL) OR (audience_type = 'APARTMENT' AND role_id IS NULL AND building_id IS NULL AND apartment_unit_id IS NOT NULL AND resident_id IS NULL) OR (audience_type = 'RESIDENT' AND role_id IS NULL AND building_id IS NULL AND apartment_unit_id IS NULL AND resident_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_announcement_audiences_announcements_announcement_id",
                        column: x => x.announcement_id,
                        principalSchema: "communication",
                        principalTable: "announcements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "announcement_versions",
                schema: "communication",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    announcement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    title_snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_snapshot = table.Column<string>(type: "text", nullable: false),
                    status_snapshot = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    audience_snapshot = table.Column<string>(type: "jsonb", nullable: true),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    change_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_announcement_versions", x => x.id);
                    table.CheckConstraint("CK_announcement_versions_content_not_blank", "length(btrim(content_snapshot)) > 0");
                    table.CheckConstraint("CK_announcement_versions_title_not_blank", "length(btrim(title_snapshot)) > 0");
                    table.CheckConstraint("CK_announcement_versions_version_no", "version_no >= 1");
                    table.ForeignKey(
                        name: "FK_announcement_versions_announcements_announcement_id",
                        column: x => x.announcement_id,
                        principalSchema: "communication",
                        principalTable: "announcements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_announcement_audiences_announcement_id",
                schema: "communication",
                table: "announcement_audiences",
                column: "announcement_id");

            migrationBuilder.CreateIndex(
                name: "IX_announcement_audiences_apartment_unit_id",
                schema: "communication",
                table: "announcement_audiences",
                column: "apartment_unit_id");

            migrationBuilder.CreateIndex(
                name: "IX_announcement_audiences_audience_type",
                schema: "communication",
                table: "announcement_audiences",
                column: "audience_type");

            migrationBuilder.CreateIndex(
                name: "IX_announcement_audiences_building_id",
                schema: "communication",
                table: "announcement_audiences",
                column: "building_id");

            migrationBuilder.CreateIndex(
                name: "IX_announcement_audiences_resident_id",
                schema: "communication",
                table: "announcement_audiences",
                column: "resident_id");

            migrationBuilder.CreateIndex(
                name: "IX_announcement_audiences_role_id",
                schema: "communication",
                table: "announcement_audiences",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "IX_announcement_versions_announcement_id_version_no",
                schema: "communication",
                table: "announcement_versions",
                columns: new[] { "announcement_id", "version_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_announcement_versions_changed_by",
                schema: "communication",
                table: "announcement_versions",
                column: "changed_by");

            migrationBuilder.CreateIndex(
                name: "IX_announcement_versions_created_at",
                schema: "communication",
                table: "announcement_versions",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_announcements_created_at",
                schema: "communication",
                table: "announcements",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_announcements_created_by",
                schema: "communication",
                table: "announcements",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_announcements_published_at",
                schema: "communication",
                table: "announcements",
                column: "published_at");

            migrationBuilder.CreateIndex(
                name: "IX_announcements_status",
                schema: "communication",
                table: "announcements",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_created_at",
                schema: "communication",
                table: "notifications",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_recipient_user_id",
                schema: "communication",
                table: "notifications",
                column: "recipient_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_recipient_user_id_is_read",
                schema: "communication",
                table: "notifications",
                columns: new[] { "recipient_user_id", "is_read" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_recipient_user_id_source_event_id",
                schema: "communication",
                table: "notifications",
                columns: new[] { "recipient_user_id", "source_event_id" },
                unique: true,
                filter: "source_event_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_source_type_source_id",
                schema: "communication",
                table: "notifications",
                columns: new[] { "source_type", "source_id" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_type",
                schema: "communication",
                table: "notifications",
                column: "type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "announcement_audiences",
                schema: "communication");

            migrationBuilder.DropTable(
                name: "announcement_versions",
                schema: "communication");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "communication");

            migrationBuilder.DropTable(
                name: "announcements",
                schema: "communication");
        }
    }
}
