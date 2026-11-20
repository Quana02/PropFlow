using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Residents.Infrastructure.Persistence;

namespace PropFlow.UnitTests;

public sealed class ResidentsModelSnapshotTests
{
    [Fact]
    public void Existing_household_and_profile_migrations_are_represented_in_the_model_snapshot()
    {
        using var context = new ResidentsDbContext(new DbContextOptionsBuilder<ResidentsDbContext>().UseNpgsql().Options);
        Assert.False(context.Database.HasPendingModelChanges());
    }
}
