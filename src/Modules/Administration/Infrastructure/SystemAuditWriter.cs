using System.Data.Common;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PropFlow.Modules.Administration.Contracts;

namespace PropFlow.Modules.Administration.Infrastructure;

public sealed class SystemAuditWriter(IConfiguration configuration) : ISystemAuditWriter
{
    public async Task AppendAsync(SystemAuditEvent entry, CancellationToken ct)
    {
        // Read the configured value, not an opened connection's sanitized ConnectionString.
        await using var connection = new Npgsql.NpgsqlConnection(configuration.GetConnectionString("PropFlowDatabase"));
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await AppendInTransactionAsync(entry, connection, transaction, ct);
        await transaction.CommitAsync(ct);
    }

    public async Task AppendInTransactionAsync(SystemAuditEvent entry, DbConnection connection, DbTransaction transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO administration.audit_logs (id, action, entity_type, entity_id, actor_user_id, new_values, correlation_id, created_at)
            VALUES (@id, @action, @entity, @entityId, @actor, CAST(@metadata AS jsonb), @trace, @time)
            """;
        void Add(string name, object? value) { var p = command.CreateParameter(); p.ParameterName = name; p.Value = value ?? DBNull.Value; command.Parameters.Add(p); }
        Add("id", Guid.NewGuid()); Add("action", entry.Action); Add("entity", entry.EntityType);
        Add("entityId", entry.EntityId); Add("actor", entry.ActorUserId);
        Add("metadata", JsonSerializer.Serialize(new { entry.Module, entry.Label, entry.Outcome, entry.Changes, entry.HttpStatus }));
        Add("trace", entry.CorrelationId); Add("time", entry.Timestamp.ToUniversalTime());
        await command.ExecuteNonQueryAsync(ct);
    }
}
