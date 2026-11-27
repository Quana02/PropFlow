using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PropFlow.Modules.RemoteOnlineSign.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAnnualContractCodeAndMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "ContractName", schema: "remote_online_sign", table: "contracts", type: "character varying(200)", maxLength: 200, nullable: true);
            migrationBuilder.AddColumn<DateTimeOffset>(name: "MetadataUpdatedAt", schema: "remote_online_sign", table: "contracts", type: "timestamp with time zone", nullable: true);

            migrationBuilder.CreateTable(
                name: "annual_contract_code_counters",
                schema: "remote_online_sign",
                columns: table => new
                {
                    business_year = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    last_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_annual_contract_code_counters", x => x.business_year);
                });

            migrationBuilder.Sql("""
                UPDATE remote_online_sign.contracts
                SET "ContractName" = "ContractCode",
                    "MetadataUpdatedAt" = "CreatedAt"
                WHERE "ContractName" IS NULL OR "MetadataUpdatedAt" IS NULL;
                """);
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM remote_online_sign.contracts WHERE "ContractName" IS NULL OR btrim("ContractName") = '' OR length("ContractName") > 200 OR "MetadataUpdatedAt" IS NULL) THEN
                        RAISE EXCEPTION 'Remote contract metadata backfill failed';
                    END IF;
                END $$;
                """);
            migrationBuilder.Sql("""
                WITH code_seed AS (
                    SELECT EXTRACT(YEAR FROM ("CreatedAt" AT TIME ZONE 'Asia/Ho_Chi_Minh'))::integer AS business_year,
                           max(substring("ContractCode" from '([0-9]{6})$')::integer) AS last_number
                    FROM remote_online_sign.contracts
                    GROUP BY 1
                ), daily_seed AS (
                    SELECT EXTRACT(YEAR FROM business_date)::integer AS business_year, max(last_number) AS last_number
                    FROM remote_online_sign.contract_code_counters
                    GROUP BY 1
                ), annual_seed AS (
                    SELECT business_year, max(last_number) AS last_number
                    FROM (SELECT * FROM code_seed UNION ALL SELECT * FROM daily_seed) seed
                    GROUP BY business_year
                )
                INSERT INTO remote_online_sign.annual_contract_code_counters (business_year, last_number)
                SELECT business_year, last_number FROM annual_seed;
                """);
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM remote_online_sign.annual_contract_code_counters WHERE last_number < 1 OR last_number > 999999) THEN
                        RAISE EXCEPTION 'Annual contract code counter is out of range';
                    END IF;
                END $$;
                """);
            migrationBuilder.AlterColumn<string>(name: "ContractName", schema: "remote_online_sign", table: "contracts", type: "character varying(200)", maxLength: 200, nullable: false, oldClrType: typeof(string), oldType: "character varying(200)", oldMaxLength: 200, oldNullable: true);
            migrationBuilder.AlterColumn<DateTimeOffset>(name: "MetadataUpdatedAt", schema: "remote_online_sign", table: "contracts", type: "timestamp with time zone", nullable: false, oldClrType: typeof(DateTimeOffset), oldType: "timestamp with time zone", oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "annual_contract_code_counters",
                schema: "remote_online_sign");

            migrationBuilder.DropColumn(
                name: "ContractName",
                schema: "remote_online_sign",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "MetadataUpdatedAt",
                schema: "remote_online_sign",
                table: "contracts");
        }
    }
}
