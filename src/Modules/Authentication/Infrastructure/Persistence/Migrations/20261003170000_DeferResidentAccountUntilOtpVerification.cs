using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PropFlow.Modules.Authentication.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AuthenticationDbContext))]
[Migration("20261003170000_DeferResidentAccountUntilOtpVerification")]
public sealed class DeferResidentAccountUntilOtpVerification : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<Guid>(
            name: "user_id",
            schema: "auth",
            table: "resident_verifications",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.AddColumn<string>(name: "registration_username", schema: "auth", table: "resident_verifications", type: "character varying(50)", maxLength: 50, nullable: true);
        migrationBuilder.AddColumn<string>(name: "registration_display_name", schema: "auth", table: "resident_verifications", type: "character varying(150)", maxLength: 150, nullable: true);
        migrationBuilder.AddColumn<string>(name: "registration_email", schema: "auth", table: "resident_verifications", type: "character varying(255)", maxLength: 255, nullable: true);
        migrationBuilder.AddColumn<string>(name: "registration_password_hash", schema: "auth", table: "resident_verifications", type: "text", nullable: true);

        migrationBuilder.CreateIndex("IX_resident_verifications_registration_username", "resident_verifications", "registration_username", "auth");
        migrationBuilder.CreateIndex("IX_resident_verifications_registration_email", "resident_verifications", "registration_email", "auth");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_resident_verifications_registration_username", "resident_verifications", "auth");
        migrationBuilder.DropIndex("IX_resident_verifications_registration_email", "resident_verifications", "auth");
        migrationBuilder.DropColumn("registration_username", "resident_verifications", "auth");
        migrationBuilder.DropColumn("registration_display_name", "resident_verifications", "auth");
        migrationBuilder.DropColumn("registration_email", "resident_verifications", "auth");
        migrationBuilder.DropColumn("registration_password_hash", "resident_verifications", "auth");
        migrationBuilder.AlterColumn<Guid>(
            name: "user_id",
            schema: "auth",
            table: "resident_verifications",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);
    }
}
