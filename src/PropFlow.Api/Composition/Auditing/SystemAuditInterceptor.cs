using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using PropFlow.Modules.Administration.Contracts;

namespace PropFlow.Api.Composition.Auditing;

// Runs only for website requests. Each source SaveChanges and its audit entries commit together.
public sealed class SystemAuditInterceptor(AuditCapture capture, IServiceProvider services) : SaveChangesInterceptor
{
    private sealed record Pending(IReadOnlyList<SystemAuditEvent> Events, IDbContextTransaction? OwnedTransaction);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<DbContext, Pending> pending = new();

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        var db = eventData.Context;
        if (db is null || capture.Http is null) return result;
        db.ChangeTracker.DetectChanges();
        var events = db.ChangeTracker.Entries().Select(capture.FromEntity).OfType<SystemAuditEvent>().ToArray();
        if (events.Length == 0) return result;
        var owned = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        pending[db] = new(events, owned);
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
    {
        var db = eventData.Context;
        if (db is null || !pending.TryRemove(db, out var batch)) return result;
        try
        {
            var writer = services.GetRequiredService<ISystemAuditWriter>();
            foreach (var entry in batch.Events)
                await writer.AppendInTransactionAsync(entry, db.Database.GetDbConnection(), db.Database.CurrentTransaction!.GetDbTransaction(), ct);
            if (batch.OwnedTransaction is not null) await batch.OwnedTransaction.CommitAsync(ct);
            capture.Record(batch.Events.Count);
        }
        finally { if (batch.OwnedTransaction is not null) await batch.OwnedTransaction.DisposeAsync(); }
        return result;
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken ct = default) => CleanupAsync(eventData.Context);
    public override Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken ct = default) => CleanupAsync(eventData.Context);
    private async Task CleanupAsync(DbContext? db)
    {
        if (db is not null && pending.TryRemove(db, out var batch) && batch.OwnedTransaction is not null)
            await batch.OwnedTransaction.DisposeAsync();
    }
}
