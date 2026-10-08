using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PropFlow.Modules.Residents.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ResidentsDbContext))]
[Migration("20261003103000_EnforceResidentCanonicalUniqueness")]
public sealed class EnforceResidentCanonicalUniqueness : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $preflight$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM residents.residents
                    WHERE identity_type IS NOT NULL
                      AND btrim(identity_type) <> ''
                      AND identity_number IS NOT NULL
                      AND regexp_replace(identity_number, '[^0-9]', '', 'g') <> ''
                    GROUP BY upper(btrim(identity_type)), regexp_replace(identity_number, '[^0-9]', '', 'g')
                    HAVING count(*) > 1
                ) THEN
                    RAISE EXCEPTION 'Cannot enforce resident identity uniqueness: canonical duplicates still exist';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM residents.residents
                    WHERE email IS NOT NULL AND btrim(email) <> ''
                    GROUP BY lower(btrim(email))
                    HAVING count(*) > 1
                ) THEN
                    RAISE EXCEPTION 'Cannot enforce resident email uniqueness: normalized duplicates still exist';
                END IF;
            END
            $preflight$;

            CREATE UNIQUE INDEX ux_residents_identity_canonical
                ON residents.residents (
                    upper(btrim(identity_type)),
                    regexp_replace(identity_number, '[^0-9]', '', 'g')
                )
                WHERE identity_type IS NOT NULL
                  AND btrim(identity_type) <> ''
                  AND identity_number IS NOT NULL
                  AND regexp_replace(identity_number, '[^0-9]', '', 'g') <> '';

            CREATE UNIQUE INDEX ux_residents_email_canonical
                ON residents.residents (lower(btrim(email)))
                WHERE email IS NOT NULL AND btrim(email) <> '';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS residents.ux_residents_email_canonical;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS residents.ux_residents_identity_canonical;");
    }
}
