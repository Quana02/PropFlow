using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PropFlow.Modules.Authentication.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AuthenticationDbContext))]
[Migration("20260916120000_EnforceOnboardingReferences")]
public sealed class EnforceOnboardingReferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Apply after the initial Residents and Apartments schemas. Existing orphans must be corrected explicitly.
        migrationBuilder.AddForeignKey("FK_verification_resident", "resident_verifications", "resident_id", "residents", principalSchema: "residents", schema: "auth", principalColumn: "id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey("FK_verification_apartment", "resident_verifications", "apartment_unit_id", "apartment_units", principalSchema: "apartments", schema: "auth", principalColumn: "id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.Sql("ALTER TABLE auth.resident_verifications ADD CONSTRAINT ck_verification_attempts CHECK (attempt_count >= 0);");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE auth.resident_verifications DROP CONSTRAINT ck_verification_attempts;");
        migrationBuilder.DropForeignKey("FK_verification_resident", "resident_verifications", "auth");
        migrationBuilder.DropForeignKey("FK_verification_apartment", "resident_verifications", "auth");
    }
}
