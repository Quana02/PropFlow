using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PropFlow.Modules.Residents.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ResidentsDbContext))]
[Migration("20261001090000_AddResidentProfileFields")]
public sealed class AddResidentProfileFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Staged rollout: existing business records remain untouched. New-record requirements are enforced by FE-02 validation.
        migrationBuilder.AddColumn<DateOnly>("date_of_birth", "residents", schema: "residents", type: "date", nullable: true);
        migrationBuilder.AddColumn<string>("gender", "residents", schema: "residents", type: "character varying(30)", maxLength: 30, nullable: true);
        migrationBuilder.AddColumn<string>("nationality", "residents", schema: "residents", type: "character varying(80)", maxLength: 80, nullable: true);
        migrationBuilder.AddColumn<string>("identity_type", "residents", schema: "residents", type: "character varying(40)", maxLength: 40, nullable: true);
        migrationBuilder.AddColumn<string>("identity_number", "residents", schema: "residents", type: "character varying(80)", maxLength: 80, nullable: true);
        migrationBuilder.AddColumn<DateOnly>("identity_issued_date", "residents", schema: "residents", type: "date", nullable: true);
        migrationBuilder.AddColumn<DateOnly>("identity_expiry_date", "residents", schema: "residents", type: "date", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("date_of_birth", "residents", schema: "residents");
        migrationBuilder.DropColumn("gender", "residents", schema: "residents");
        migrationBuilder.DropColumn("nationality", "residents", schema: "residents");
        migrationBuilder.DropColumn("identity_type", "residents", schema: "residents");
        migrationBuilder.DropColumn("identity_number", "residents", schema: "residents");
        migrationBuilder.DropColumn("identity_issued_date", "residents", schema: "residents");
        migrationBuilder.DropColumn("identity_expiry_date", "residents", schema: "residents");
    }
}
