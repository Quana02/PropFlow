using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Payments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentDestinations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_destinations",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    bank_bin = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    bank_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    bank_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    protected_account_number = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    account_number_last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    account_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_destinations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payment_destinations_is_active",
                schema: "payments",
                table: "payment_destinations",
                column: "is_active",
                unique: true,
                filter: "is_active = true");

            migrationBuilder.CreateIndex(
                name: "IX_payment_destinations_version",
                schema: "payments",
                table: "payment_destinations",
                column: "version",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_destinations",
                schema: "payments");
        }
    }
}
