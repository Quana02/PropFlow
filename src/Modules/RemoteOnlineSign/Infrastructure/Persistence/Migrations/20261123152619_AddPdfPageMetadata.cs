using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.RemoteOnlineSign.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPdfPageMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PageCount",
                schema: "remote_online_sign",
                table: "contract_documents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PageMetadata",
                schema: "remote_online_sign",
                table: "contract_documents",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PageCount",
                schema: "remote_online_sign",
                table: "contract_documents");

            migrationBuilder.DropColumn(
                name: "PageMetadata",
                schema: "remote_online_sign",
                table: "contract_documents");
        }
    }
}
