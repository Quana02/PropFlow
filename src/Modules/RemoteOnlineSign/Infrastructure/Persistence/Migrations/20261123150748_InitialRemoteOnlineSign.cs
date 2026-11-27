using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.RemoteOnlineSign.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialRemoteOnlineSign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "remote_online_sign");

            migrationBuilder.CreateTable(
                name: "contract_documents",
                schema: "remote_online_sign",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageKey = table.Column<string>(type: "text", nullable: false),
                    Sha256 = table.Column<string>(type: "text", nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    MimeType = table.Column<string>(type: "text", nullable: false),
                    IsFinal = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contract_documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "contract_events",
                schema: "remote_online_sign",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Metadata = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contract_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "contract_retention_policies",
                schema: "remote_online_sign",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    MinimumYears = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contract_retention_policies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "contract_signature_fields",
                schema: "remote_online_sign",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    Page = table.Column<int>(type: "integer", nullable: false),
                    X = table.Column<decimal>(type: "numeric", nullable: false),
                    Y = table.Column<decimal>(type: "numeric", nullable: false),
                    Width = table.Column<decimal>(type: "numeric", nullable: false),
                    Height = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contract_signature_fields", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "contract_signing_sessions",
                schema: "remote_online_sign",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    SignerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    OtpHash = table.Column<string>(type: "text", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    Resends = table.Column<int>(type: "integer", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contract_signing_sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "contracts",
                schema: "remote_online_sign",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    SigningMode = table.Column<string>(type: "text", nullable: false),
                    Deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contracts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_contract_documents_ContractId_IsFinal",
                schema: "remote_online_sign",
                table: "contract_documents",
                columns: new[] { "ContractId", "IsFinal" },
                unique: true,
                filter: "\"IsFinal\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_contract_documents_StorageKey",
                schema: "remote_online_sign",
                table: "contract_documents",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contract_events_ContractId_OccurredAt",
                schema: "remote_online_sign",
                table: "contract_events",
                columns: new[] { "ContractId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_contract_retention_policies_Version",
                schema: "remote_online_sign",
                table: "contract_retention_policies",
                column: "Version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contract_signature_fields_ContractId_Role",
                schema: "remote_online_sign",
                table: "contract_signature_fields",
                columns: new[] { "ContractId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contract_signing_sessions_ContractId_Role",
                schema: "remote_online_sign",
                table: "contract_signing_sessions",
                columns: new[] { "ContractId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contracts_ResidentId_Status",
                schema: "remote_online_sign",
                table: "contracts",
                columns: new[] { "ResidentId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contract_documents",
                schema: "remote_online_sign");

            migrationBuilder.DropTable(
                name: "contract_events",
                schema: "remote_online_sign");

            migrationBuilder.DropTable(
                name: "contract_retention_policies",
                schema: "remote_online_sign");

            migrationBuilder.DropTable(
                name: "contract_signature_fields",
                schema: "remote_online_sign");

            migrationBuilder.DropTable(
                name: "contract_signing_sessions",
                schema: "remote_online_sign");

            migrationBuilder.DropTable(
                name: "contracts",
                schema: "remote_online_sign");
        }
    }
}
