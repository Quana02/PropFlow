using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace PropFlow.Modules.Administration.Contracts;

public sealed record AuditFieldChange(string Field, string? Before, string? After);
public sealed record SystemAuditEvent(string Module, string Action, string Label, string EntityType,
    Guid? EntityId, Guid? ActorUserId, string Outcome, DateTimeOffset Timestamp,
    string? CorrelationId, IReadOnlyList<AuditFieldChange> Changes, int? HttpStatus = null);

// Source transactions retain ownership of their data; Administration only appends audit records.
public interface ISystemAuditWriter
{
    Task AppendAsync(SystemAuditEvent entry, CancellationToken ct);
    Task AppendInTransactionAsync(SystemAuditEvent entry, DbConnection connection, DbTransaction transaction, CancellationToken ct);
}
