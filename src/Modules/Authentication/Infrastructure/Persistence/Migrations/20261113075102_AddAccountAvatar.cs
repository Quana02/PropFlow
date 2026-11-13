using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Authentication.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountAvatar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "avatar_storage_key",
                schema: "auth",
                table: "users",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "avatar_version",
                schema: "auth",
                table: "users",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "avatar_storage_key",
                schema: "auth",
                table: "users");

            migrationBuilder.DropColumn(
                name: "avatar_version",
                schema: "auth",
                table: "users");
        }
    }
}
