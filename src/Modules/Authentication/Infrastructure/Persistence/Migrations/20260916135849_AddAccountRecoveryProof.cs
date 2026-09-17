using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Authentication.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountRecoveryProof : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "attempt_count",
                schema: "auth",
                table: "password_reset_tokens",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "proof_expires_at",
                schema: "auth",
                table: "password_reset_tokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "proof_hash",
                schema: "auth",
                table: "password_reset_tokens",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "verified_at",
                schema: "auth",
                table: "password_reset_tokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_created_by",
                schema: "auth",
                table: "users",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_users_updated_by",
                schema: "auth",
                table: "users",
                column: "updated_by");

            migrationBuilder.AddForeignKey(
                name: "FK_users_users_created_by",
                schema: "auth",
                table: "users",
                column: "created_by",
                principalSchema: "auth",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_users_users_updated_by",
                schema: "auth",
                table: "users",
                column: "updated_by",
                principalSchema: "auth",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_users_users_created_by",
                schema: "auth",
                table: "users");

            migrationBuilder.DropForeignKey(
                name: "FK_users_users_updated_by",
                schema: "auth",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_created_by",
                schema: "auth",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_updated_by",
                schema: "auth",
                table: "users");

            migrationBuilder.DropColumn(
                name: "attempt_count",
                schema: "auth",
                table: "password_reset_tokens");

            migrationBuilder.DropColumn(
                name: "proof_expires_at",
                schema: "auth",
                table: "password_reset_tokens");

            migrationBuilder.DropColumn(
                name: "proof_hash",
                schema: "auth",
                table: "password_reset_tokens");

            migrationBuilder.DropColumn(
                name: "verified_at",
                schema: "auth",
                table: "password_reset_tokens");
        }
    }
}
