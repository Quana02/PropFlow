using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.Apartments.Application;
using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed class ApartmentOwnershipConcurrencyTests(PropFlowApiFactory factory) : IClassFixture<PropFlowApiFactory>
{
    [Fact]
    public async Task Concurrent_add_of_different_owners_commits_both_rows()
    {
        var (apartment, first, second) = await SeedAsync();
        using var gate = new Barrier(2);

        var results = await Task.WhenAll(
            Task.Run(() => AddInSeparateScope(apartment, first, gate)),
            Task.Run(() => AddInSeparateScope(apartment, second, gate)));

        Assert.All(results, Assert.Null);
        await using var verify = NewScope();
        var rows = await verify.Context.ApartmentOwnerships.Where(x => x.ApartmentUnitId == apartment && x.EndDate == null).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, x => x.OwnerResidentId == first);
        Assert.Contains(rows, x => x.OwnerResidentId == second);
    }

    [Fact]
    public async Task Concurrent_add_of_same_owner_returns_one_safe_conflict()
    {
        var (apartment, owner, _) = await SeedAsync();
        using var gate = new Barrier(2);
        var results = await Task.WhenAll(Task.Run(() => AddInSeparateScope(apartment, owner, gate)), Task.Run(() => AddInSeparateScope(apartment, owner, gate)));

        Assert.Equal(1, results.Count(x => x is null));
        var conflict = Assert.Single(results.Where(x => x is not null));
        Assert.Equal(409, conflict!.StatusCode);
        Assert.Equal("apartment_owner_already_current", conflict.Code);
        await using var verify = NewScope();
        Assert.Equal(1, await verify.Context.ApartmentOwnerships.CountAsync(x => x.ApartmentUnitId == apartment && x.OwnerResidentId == owner && x.EndDate == null));
    }

    [Fact]
    public async Task Concurrent_end_of_same_ownership_has_one_transition_and_one_safe_conflict()
    {
        var (apartment, owner, _) = await SeedAsync();
        Guid ownershipId;
        await using (var seed = NewScope())
        {
            var row = new ApartmentOwnership(apartment, owner, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), DateTimeOffset.UtcNow, null);
            seed.Context.ApartmentOwnerships.Add(row);
            await seed.Context.SaveChangesAsync();
            ownershipId = row.Id;
        }
        using var gate = new Barrier(2);
        var results = await Task.WhenAll(Task.Run(() => EndInSeparateScope(apartment, ownershipId, gate)), Task.Run(() => EndInSeparateScope(apartment, ownershipId, gate)));

        Assert.Equal(1, results.Count(x => x is null));
        Assert.Equal(409, Assert.Single(results.Where(x => x is not null))!.StatusCode);
        await using var verify = NewScope();
        var rows = await verify.Context.ApartmentOwnerships.Where(x => x.Id == ownershipId).ToListAsync();
        Assert.Single(rows);
        Assert.NotNull(rows[0].EndDate);
    }

    [Fact]
    public async Task Concurrent_add_owner_and_deactivate_never_commits_an_inactive_apartment_with_a_current_owner()
    {
        var (apartment, owner, _) = await SeedAsync();
        using var gate = new Barrier(2);

        var results = await Task.WhenAll(
            Task.Run(() => AddForStatusRaceInSeparateScope(apartment, owner, gate)),
            Task.Run(() => DeactivateInSeparateScope(apartment, gate)));

        Assert.Equal(1, results.Count(x => x is null));
        Assert.Single(results.Where(x => x is not null));
        await using var verify = NewScope();
        var status = await verify.Context.ApartmentUnits.AsNoTracking().Where(x => x.Id == apartment).Select(x => x.Status).SingleAsync();
        var hasCurrentOwner = await verify.Context.ApartmentOwnerships.AsNoTracking().AnyAsync(x => x.ApartmentUnitId == apartment && x.EndDate == null);
        Assert.False(status == MasterDataStatus.INACTIVE && hasCurrentOwner);
    }

    private async Task<ApartmentOwnershipException?> AddInSeparateScope(Guid apartment, Guid resident, Barrier gate)
    {
        await using var scope = NewScope();
        if (!gate.SignalAndWait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("Concurrent add operation did not reach the PostgreSQL race barrier.");
        try { await scope.Services.GetRequiredService<IApartmentOwnershipCommand>().AddOwnerAsync(apartment, resident, DateOnly.FromDateTime(DateTime.UtcNow), null, CancellationToken.None); return null; }
        catch (ApartmentOwnershipException exception) { return exception; }
    }

    private async Task<ApartmentOwnershipException?> EndInSeparateScope(Guid apartment, Guid ownership, Barrier gate)
    {
        await using var scope = NewScope();
        if (!gate.SignalAndWait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("Concurrent end operation did not reach the PostgreSQL race barrier.");
        try { await scope.Services.GetRequiredService<IApartmentOwnershipCommand>().EndOwnershipAsync(apartment, ownership, DateOnly.FromDateTime(DateTime.UtcNow), null, CancellationToken.None); return null; }
        catch (ApartmentOwnershipException exception) { return exception; }
    }

    private async Task<Exception?> AddForStatusRaceInSeparateScope(Guid apartment, Guid resident, Barrier gate)
    {
        await using var scope = NewScope();
        if (!gate.SignalAndWait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("Concurrent add/status operation did not reach the PostgreSQL race barrier.");
        try
        {
            await scope.Services.GetRequiredService<IApartmentOwnershipCommand>()
                .AddOwnerAsync(apartment, resident, DateOnly.FromDateTime(DateTime.UtcNow), null, CancellationToken.None);
            return null;
        }
        catch (ApartmentOwnershipException exception) { return exception; }
    }

    private async Task<Exception?> DeactivateInSeparateScope(Guid apartment, Barrier gate)
    {
        await using var scope = NewScope();
        if (!gate.SignalAndWait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("Concurrent add/status operation did not reach the PostgreSQL race barrier.");
        try
        {
            await scope.Services.GetRequiredService<IApartmentStatusCommand>()
                .SetAsync(apartment, MasterDataStatus.INACTIVE, DateOnly.FromDateTime(DateTime.UtcNow), null, CancellationToken.None);
            return null;
        }
        catch (ApartmentStatusException exception) { return exception; }
    }

    private async Task<(Guid Apartment, Guid First, Guid Second)> SeedAsync()
    {
        await using var scope = NewScope();
        await scope.Services.GetRequiredService<ResidentsDbContext>().Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var apartment = new ApartmentUnit($"CON-{Guid.NewGuid():N}"[..16], 1, now, await ApartmentTypeTestData.CreateAsync(scope.Context));
        var first = new Resident($"R{Guid.NewGuid():N}"[..18], "Owner A", now);
        var second = new Resident($"R{Guid.NewGuid():N}"[..18], "Owner B", now);
        scope.Context.ApartmentUnits.Add(apartment);
        scope.Services.GetRequiredService<ResidentsDbContext>().Residents.AddRange(first, second);
        await scope.Context.SaveChangesAsync();
        await scope.Services.GetRequiredService<ResidentsDbContext>().SaveChangesAsync();
        return (apartment.Id, first.Id, second.Id);
    }

    private Scope NewScope() => new(factory.Services.CreateAsyncScope());

    private sealed class Scope(AsyncServiceScope scope) : IAsyncDisposable
    {
        public IServiceProvider Services => scope.ServiceProvider;
        public ApartmentsDbContext Context => Services.GetRequiredService<ApartmentsDbContext>();
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}
