using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PropFlow.Modules.Residents.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ResidentsDbContext))]
[Migration("20260929100000_AddResidentCodeSequence")]
public partial class AddResidentCodeSequence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE SEQUENCE residents.resident_code_sequence START WITH 1 INCREMENT BY 1;");
        migrationBuilder.Sql("SELECT setval('residents.resident_code_sequence', COALESCE((SELECT MAX(NULLIF(substring(resident_code from '^RES-([0-9]+)$'), ''))::bigint FROM residents.residents), 0) + 1, false);");
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("DROP SEQUENCE residents.resident_code_sequence;");
}
