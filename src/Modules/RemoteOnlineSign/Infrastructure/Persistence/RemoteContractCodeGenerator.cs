using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
namespace PropFlow.Modules.RemoteOnlineSign.Infrastructure.Persistence;
public interface IRemoteContractCodeGenerator { Task<string> NextAsync(DateTimeOffset createdAt, CancellationToken ct); }
public sealed class RemoteContractCodeGenerator(RemoteOnlineSignDbContext db) : IRemoteContractCodeGenerator
{
 public async Task<string> NextAsync(DateTimeOffset createdAt, CancellationToken ct)
 {
  var zone=TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows()?"SE Asia Standard Time":"Asia/Ho_Chi_Minh"); var local=TimeZoneInfo.ConvertTime(createdAt,zone); var date=DateOnly.FromDateTime(local.DateTime); var year=date.Year; var c=(NpgsqlConnection)db.Database.GetDbConnection(); var opened=c.State!=System.Data.ConnectionState.Open;if(opened)await c.OpenAsync(ct);try{await using var cmd=new NpgsqlCommand("INSERT INTO remote_online_sign.annual_contract_code_counters (business_year,last_number) VALUES (@year,1) ON CONFLICT (business_year) DO UPDATE SET last_number=remote_online_sign.annual_contract_code_counters.last_number+1 RETURNING last_number",c);if(db.Database.CurrentTransaction is not null)cmd.Transaction=(NpgsqlTransaction)db.Database.CurrentTransaction.GetDbTransaction();cmd.Parameters.AddWithValue("year",year);var n=Convert.ToInt32(await cmd.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture);if(n>999999)throw new InvalidOperationException("Đã vượt giới hạn 999999 hợp đồng trong năm.");return $"HD-BDX-{date:ddMMyyyy}-{n:D6}";}finally{if(opened)await c.CloseAsync();}
 }
}
