using PropFlow.Modules.Apartments.Application;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;

namespace PropFlow.UnitTests;

public class ApartmentsTests
{
    private readonly DateTimeOffset _now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ApartmentUnit_Constructor_GeneratesId_AndSetsDefaults()
    {
        var typeId = Guid.NewGuid();
        var unit = new ApartmentUnit("U-101", 1, _now, typeId, 75.5m, 2, 2, null, "Luxury suite");

        Assert.NotEqual(Guid.Empty, unit.Id);
        
        Assert.Equal("U-101", unit.UnitNumber);
        Assert.Equal(1, unit.FloorNumber);
        Assert.Equal(typeId, unit.ApartmentUnitTypeId);
        Assert.Equal(75.5m, unit.UsableAreaM2);
        Assert.Equal(2, unit.BedroomCount);
        Assert.Equal("Luxury suite", unit.Description);
        Assert.Equal(MasterDataStatus.ACTIVE, unit.Status);
        Assert.Equal(_now, unit.CreatedAt);
        Assert.Equal(_now, unit.UpdatedAt);
    }

    [Fact]
    public void ApartmentUnit_Constructor_ThrowsOnInvalidArgs()
    {
        Assert.Throws<ArgumentException>(() => new ApartmentUnit("", 1, _now, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new ApartmentUnit("   ", 1, _now, Guid.NewGuid()));
    }

    [Fact]
    public void ApartmentUnit_ActivateAndDeactivate_TransitionsProperly()
    {
        var unit = new ApartmentUnit("U-101", 1, _now, Guid.NewGuid());
        var actor = Guid.NewGuid();
        var later = _now.AddDays(1);

        unit.Deactivate(actor, later);
        Assert.Equal(MasterDataStatus.INACTIVE, unit.Status);
        Assert.Equal(actor, unit.UpdatedBy);
        Assert.Equal(later, unit.UpdatedAt);

        var evenLater = later.AddDays(1);
        unit.Activate(actor, evenLater);
        Assert.Equal(MasterDataStatus.ACTIVE, unit.Status);
        Assert.Equal(evenLater, unit.UpdatedAt);
    }
    [Fact]
    public void IApartmentStatisticsReader_Interface_IsDefined()
    {
        // Verify the interface exists and can be referenced
        var interfaceType = typeof(IApartmentStatisticsReader);
        Assert.NotNull(interfaceType);
        Assert.Equal("IApartmentStatisticsReader", interfaceType.Name);
    }
}
