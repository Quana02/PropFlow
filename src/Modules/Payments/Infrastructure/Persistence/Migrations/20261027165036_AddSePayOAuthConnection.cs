using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Payments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSePayOAuthConnection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sepay_oauth_connection",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Generation = table.Column<Guid>(type: "uuid", nullable: false),
                    ProtectedAccessToken = table.Column<string>(type: "text", nullable: true),
                    ProtectedRefreshToken = table.Column<string>(type: "text", nullable: true),
                    AccessExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RequiresReconnect = table.Column<bool>(type: "boolean", nullable: false),
                    ConnectedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ConnectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PendingStateHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PendingActor = table.Column<Guid>(type: "uuid", nullable: true),
                    PendingExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PendingRedirectUri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    PendingAttempt = table.Column<Guid>(type: "uuid", nullable: false),
                    WebhookId = table.Column<long>(type: "bigint", nullable: true),
                    WebhookDestinationId = table.Column<Guid>(type: "uuid", nullable: true),
                    WebhookConfiguredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sepay_oauth_connection", x => x.Id);
                    table.CheckConstraint("ck_sepay_oauth_singleton", "\"Id\" = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sepay_oauth_connection",
                schema: "payments");
        }
    }
}
