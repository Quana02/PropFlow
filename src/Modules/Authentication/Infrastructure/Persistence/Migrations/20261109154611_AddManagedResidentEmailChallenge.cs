using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Modules.Authentication.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedResidentEmailChallenge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "outcome",
                schema: "auth",
                table: "privileged_action_challenges",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reason",
                schema: "auth",
                table: "privileged_action_challenges",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "requested_by",
                schema: "auth",
                table: "privileged_action_challenges",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "resident_id",
                schema: "auth",
                table: "privileged_action_challenges",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "outcome",
                schema: "auth",
                table: "privileged_action_challenges");

            migrationBuilder.DropColumn(
                name: "reason",
                schema: "auth",
                table: "privileged_action_challenges");

            migrationBuilder.DropColumn(
                name: "requested_by",
                schema: "auth",
                table: "privileged_action_challenges");

            migrationBuilder.DropColumn(
                name: "resident_id",
                schema: "auth",
                table: "privileged_action_challenges");
        }
    }
}
