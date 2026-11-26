using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.RemoteOnlineSign.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSigningWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WorkingSha256",
                schema: "remote_online_sign",
                table: "contracts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkingStorageKey",
                schema: "remote_online_sign",
                table: "contracts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AuthChallengeId",
                schema: "remote_online_sign",
                table: "contract_signing_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BindingHash",
                schema: "remote_online_sign",
                table: "contract_signing_sessions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FinalizationStartedAt",
                schema: "remote_online_sign",
                table: "contract_signing_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FinalizedAt",
                schema: "remote_online_sign",
                table: "contract_signing_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Method",
                schema: "remote_online_sign",
                table: "contract_signing_sessions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OtpVerifiedAt",
                schema: "remote_online_sign",
                table: "contract_signing_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "SignatureImage",
                schema: "remote_online_sign",
                table: "contract_signing_sessions",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatureSha256",
                schema: "remote_online_sign",
                table: "contract_signing_sessions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TypedSignatureFont",
                schema: "remote_online_sign",
                table: "contract_signing_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TypedSignatureText",
                schema: "remote_online_sign",
                table: "contract_signing_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_contract_signing_sessions_AuthChallengeId",
                schema: "remote_online_sign",
                table: "contract_signing_sessions",
                column: "AuthChallengeId",
                unique: true,
                filter: "\"AuthChallengeId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_contract_signing_sessions_AuthChallengeId",
                schema: "remote_online_sign",
                table: "contract_signing_sessions");

            migrationBuilder.DropColumn(
                name: "WorkingSha256",
                schema: "remote_online_sign",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "WorkingStorageKey",
                schema: "remote_online_sign",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "AuthChallengeId",
                schema: "remote_online_sign",
                table: "contract_signing_sessions");

            migrationBuilder.DropColumn(
                name: "BindingHash",
                schema: "remote_online_sign",
                table: "contract_signing_sessions");

            migrationBuilder.DropColumn(
                name: "FinalizationStartedAt",
                schema: "remote_online_sign",
                table: "contract_signing_sessions");

            migrationBuilder.DropColumn(
                name: "FinalizedAt",
                schema: "remote_online_sign",
                table: "contract_signing_sessions");

            migrationBuilder.DropColumn(
                name: "Method",
                schema: "remote_online_sign",
                table: "contract_signing_sessions");

            migrationBuilder.DropColumn(
                name: "OtpVerifiedAt",
                schema: "remote_online_sign",
                table: "contract_signing_sessions");

            migrationBuilder.DropColumn(
                name: "SignatureImage",
                schema: "remote_online_sign",
                table: "contract_signing_sessions");

            migrationBuilder.DropColumn(
                name: "SignatureSha256",
                schema: "remote_online_sign",
                table: "contract_signing_sessions");

            migrationBuilder.DropColumn(
                name: "TypedSignatureFont",
                schema: "remote_online_sign",
                table: "contract_signing_sessions");

            migrationBuilder.DropColumn(
                name: "TypedSignatureText",
                schema: "remote_online_sign",
                table: "contract_signing_sessions");
        }
    }
}
