using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace PropFlow.Modules.RemoteOnlineSign.Infrastructure.Persistence.Migrations;
public partial class AddRemoteContractCode : Migration
{
 protected override void Up(MigrationBuilder m)
 {
  m.AddColumn<string>(name:"ContractCode",schema:"remote_online_sign",table:"contracts",type:"character varying(25)",maxLength:25,nullable:true);
  m.CreateTable(name:"contract_code_counters",schema:"remote_online_sign",columns:t=>new{business_date=t.Column<DateOnly>(type:"date",nullable:false),last_number=t.Column<int>(type:"integer",nullable:false)},constraints:t=>t.PrimaryKey("PK_contract_code_counters",x=>x.business_date));
  m.Sql("""DO $$ BEGIN IF EXISTS (SELECT 1 FROM (SELECT row_number() OVER (PARTITION BY (("CreatedAt" AT TIME ZONE 'Asia/Ho_Chi_Minh')::date)) n FROM remote_online_sign.contracts) x WHERE n > 999999) THEN RAISE EXCEPTION 'ContractCode daily limit exceeded'; END IF; END $$;""");
  m.Sql("""WITH ranked AS (SELECT "Id", (("CreatedAt" AT TIME ZONE 'Asia/Ho_Chi_Minh')::date) d, row_number() OVER (PARTITION BY (("CreatedAt" AT TIME ZONE 'Asia/Ho_Chi_Minh')::date) ORDER BY "CreatedAt", "Id") n FROM remote_online_sign.contracts) UPDATE remote_online_sign.contracts c SET "ContractCode"='HD-BDX-'||to_char(r.d,'DDMMYYYY')||'-'||lpad(r.n::text,6,'0') FROM ranked r WHERE c."Id"=r."Id";""");
  m.Sql("""INSERT INTO remote_online_sign.contract_code_counters (business_date,last_number) SELECT (("CreatedAt" AT TIME ZONE 'Asia/Ho_Chi_Minh')::date), max(substring("ContractCode" from '([0-9]{6})$')::integer) FROM remote_online_sign.contracts GROUP BY 1;""");
  m.AlterColumn<string>(name:"ContractCode",schema:"remote_online_sign",table:"contracts",type:"character varying(25)",maxLength:25,nullable:false,oldClrType:typeof(string),oldType:"character varying(25)",oldMaxLength:25,oldNullable:true);m.CreateIndex(name:"IX_contracts_ContractCode",schema:"remote_online_sign",table:"contracts",column:"ContractCode",unique:true);
 }
 protected override void Down(MigrationBuilder m){m.DropIndex(name:"IX_contracts_ContractCode",schema:"remote_online_sign",table:"contracts");m.DropTable(name:"contract_code_counters",schema:"remote_online_sign");m.DropColumn(name:"ContractCode",schema:"remote_online_sign",table:"contracts");}
}
