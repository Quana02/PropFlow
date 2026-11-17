using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PropFlow.Modules.Authentication.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AuthenticationDbContext))]
[Migration("20261115110000_BindAccessTokensToSessions")]
public sealed class BindAccessTokensToSessions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>("session_id", "refresh_tokens", "uuid", schema: "auth", nullable: true);
        migrationBuilder.AddColumn<string>("access_fingerprint", "refresh_tokens", "character varying(64)",
            maxLength: 64, schema: "auth", nullable: true);

        // Legacy credentials have no signed binding to a refresh family.
        migrationBuilder.Sql("""
            UPDATE auth.refresh_tokens
            SET revoked_at = now(), revoked_reason = 'session_binding_upgrade'
            WHERE revoked_at IS NULL
            """);

        migrationBuilder.CreateIndex("IX_refresh_tokens_user_id_session_id", "refresh_tokens",
            new[] { "user_id", "session_id" }, schema: "auth",
            filter: "revoked_at IS NULL AND session_id IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Keep the upgrade revocations and prevent an older binary from restoring bound sessions.
        migrationBuilder.Sql("""
            UPDATE auth.refresh_tokens
            SET revoked_at = now(), revoked_reason = 'session_binding_downgrade'
            WHERE revoked_at IS NULL AND session_id IS NOT NULL
            """);
        migrationBuilder.DropIndex("IX_refresh_tokens_user_id_session_id", "refresh_tokens", schema: "auth");
        migrationBuilder.DropColumn("access_fingerprint", "refresh_tokens", schema: "auth");
        migrationBuilder.DropColumn("session_id", "refresh_tokens", schema: "auth");
    }
}
