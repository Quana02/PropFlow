using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

internal static class ApartmentTypeTestData
{
    public static async Task<Guid> CreateAsync(ApartmentsDbContext db, string? name = null)
    {
        await db.Database.MigrateAsync();
        var type = new ApartmentUnitType(name ?? $"Test type {Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        db.ApartmentUnitTypes.Add(type);
        await db.SaveChangesAsync();
        return type.Id;
    }
}
