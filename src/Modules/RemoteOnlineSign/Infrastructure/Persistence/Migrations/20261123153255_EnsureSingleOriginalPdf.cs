using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.RemoteOnlineSign.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnsureSingleOriginalPdf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_contract_documents_ContractId",
                schema: "remote_online_sign",
                table: "contract_documents",
                column: "ContractId",
                unique: true,
                filter: "\"IsFinal\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_contract_documents_ContractId",
                schema: "remote_online_sign",
                table: "contract_documents");
        }
    }
}
