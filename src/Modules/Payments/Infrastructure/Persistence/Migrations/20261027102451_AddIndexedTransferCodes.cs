using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Payments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexedTransferCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "transfer_code",
                schema: "payments",
                table: "payment_instructions",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "");

            // Preserve exactly the code previously derived in memory for every pinned invoice.
            migrationBuilder.Sql("UPDATE payments.payment_instructions SET transfer_code = 'PF' || upper(substr(replace(id::text, '-', ''), 1, 22)) WHERE transfer_code = '';");

            migrationBuilder.CreateIndex(
                name: "IX_payment_instructions_transfer_code",
                schema: "payments",
                table: "payment_instructions",
                column: "transfer_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payment_instructions_transfer_code",
                schema: "payments",
                table: "payment_instructions");

            migrationBuilder.DropColumn(
                name: "transfer_code",
                schema: "payments",
                table: "payment_instructions");
        }
    }
}
