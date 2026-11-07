using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PropFlow.Modules.Authentication.Contracts;

public interface IAuditActorLookup
{
    Task<IReadOnlyList<Guid>> FindIdsAsync(string search, CancellationToken ct);
}
