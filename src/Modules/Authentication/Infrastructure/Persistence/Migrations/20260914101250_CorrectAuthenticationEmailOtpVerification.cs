using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Authentication.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CorrectAuthenticationEmailOtpVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_resident_verifications_users_reviewed_by",
                schema: "auth",
                table: "resident_verifications");

            migrationBuilder.DropIndex(
                name: "IX_resident_verifications_reviewed_by",
                schema: "auth",
                table: "resident_verifications");

            migrationBuilder.DropColumn(
                name: "phone_verified",
                schema: "auth",
                table: "users");

            migrationBuilder.DropColumn(
                name: "evidence_reference",
                schema: "auth",
                table: "resident_verifications");

            migrationBuilder.DropColumn(
                name: "failure_reason",
                schema: "auth",
                table: "resident_verifications");

            migrationBuilder.DropColumn(
                name: "method_code",
                schema: "auth",
                table: "resident_verifications");

            migrationBuilder.DropColumn(
                name: "reviewed_by",
                schema: "auth",
                table: "resident_verifications");

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM auth.resident_verifications
                        WHERE resident_id IS NULL
                           OR verification_code_hash IS NULL
                           OR btrim(verification_code_hash) = ''
                           OR expires_at IS NULL
                    ) THEN
                        RAISE EXCEPTION 'Cannot migrate auth.resident_verifications to email OTP model because existing rows lack resident_id, verification_code_hash, or expires_at.';
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "verification_code_hash",
                schema: "auth",
                table: "resident_verifications",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "resident_id",
                schema: "auth",
                table: "resident_verifications",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "expires_at",
                schema: "auth",
                table: "resident_verifications",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "phone_verified",
                schema: "auth",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "verification_code_hash",
                schema: "auth",
                table: "resident_verifications",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<Guid>(
                name: "resident_id",
                schema: "auth",
                table: "resident_verifications",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "expires_at",
                schema: "auth",
                table: "resident_verifications",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<string>(
                name: "evidence_reference",
                schema: "auth",
                table: "resident_verifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "failure_reason",
                schema: "auth",
                table: "resident_verifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "method_code",
                schema: "auth",
                table: "resident_verifications",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "reviewed_by",
                schema: "auth",
                table: "resident_verifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_resident_verifications_reviewed_by",
                schema: "auth",
                table: "resident_verifications",
                column: "reviewed_by");

            migrationBuilder.AddForeignKey(
                name: "FK_resident_verifications_users_reviewed_by",
                schema: "auth",
                table: "resident_verifications",
                column: "reviewed_by",
                principalSchema: "auth",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
