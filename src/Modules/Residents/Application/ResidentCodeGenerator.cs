using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Modules.Residents.Infrastructure.Persistence;

namespace PropFlow.Modules.Residents.Application;

public sealed class ResidentCodeGenerator(ResidentsDbContext db)
{
    public async Task<string> NextAsync(CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(ct);
        try
        {
            await using var command = new NpgsqlCommand("SELECT nextval('residents.resident_code_sequence')", connection);
            var value = Convert.ToInt64(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
            return $"RES-{value:D6}";
        }
        finally { if (openedHere) await connection.CloseAsync(); }
    }
}
