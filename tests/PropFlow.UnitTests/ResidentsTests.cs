using PropFlow.Modules.Residents.Domain.ResidentApartments;
using PropFlow.Modules.Residents.Domain.Residents;

namespace PropFlow.UnitTests;

public class ResidentsTests
{
    private readonly DateTimeOffset _now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Resident_Constructor_GeneratesId_AndValidatesRequiredFields()
    {
        var resident = new Resident("RES-001", "Alice Smith", _now, email: "alice@example.com");

        Assert.NotEqual(Guid.Empty, resident.Id);
        Assert.Equal("RES-001", resident.ResidentCode);
        Assert.Equal("Alice Smith", resident.FullName);
        Assert.Equal("alice@example.com", resident.Email);
        Assert.Equal(ResidentStatus.ACTIVE, resident.Status);
        Assert.Null(resident.UserId);
        Assert.Equal(_now, resident.CreatedAt);

        Assert.Throws<ArgumentException>(() => new Resident("", "Alice", _now));
        Assert.Throws<ArgumentException>(() => new Resident("RES-001", "", _now));
    }

    [Fact]
    public void Resident_LinkUserAccount_AndProfileUpdates_WorkDeterministically()
    {
        var resident = new Resident("RES-001", "Alice Smith", _now);
        var userId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var linkTime = _now.AddDays(1);

        resident.LinkUserAccount(userId, actor, linkTime);
        Assert.Equal(userId, resident.UserId);
        Assert.Equal(actor, resident.UpdatedBy);
        Assert.Equal(linkTime, resident.UpdatedAt);

        Assert.Throws<ArgumentException>(() => resident.LinkUserAccount(Guid.Empty, actor, linkTime));

        var updateTime = linkTime.AddDays(1);
        resident.UpdateProfile("Alice Johnson", "0987654321", "alice.j@example.com", "Note updated", actor, updateTime);
        Assert.Equal("Alice Johnson", resident.FullName);
        Assert.Equal("0987654321", resident.PhoneNumber);
        Assert.Equal(updateTime, resident.UpdatedAt);
    }

    [Fact]
    public void ResidentApartment_Constructor_ValidatesForeignKeysAndDates()
    {
        var residentId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var startDate = new DateOnly(2026, 1, 1);
        var endDate = new DateOnly(2025, 12, 31); // Invalid: before start

        Assert.Throws<ArgumentException>(() => new ResidentApartment(Guid.Empty, unitId, "OWNER", startDate, _now));
        Assert.Throws<ArgumentException>(() => new ResidentApartment(residentId, Guid.Empty, "OWNER", startDate, _now));
        Assert.Throws<ArgumentException>(() => new ResidentApartment(residentId, unitId, "OWNER", startDate, _now, endDate: endDate));

        var resApartment = new ResidentApartment(residentId, unitId, "TENANT", startDate, _now, isPrimary: true);
        Assert.NotEqual(Guid.Empty, resApartment.Id);
        Assert.Equal(residentId, resApartment.ResidentId);
        Assert.Equal(unitId, resApartment.ApartmentUnitId);
        Assert.Equal("TENANT", resApartment.RelationshipTypeCode);
        Assert.True(resApartment.IsPrimary);
        Assert.Equal(ResidencyStatus.ACTIVE, resApartment.Status);
        Assert.True(resApartment.IsActiveAt(new DateOnly(2026, 6, 1)));
        Assert.False(resApartment.IsActiveAt(new DateOnly(2025, 12, 31)));
    }

    [Fact]
    public void ResidentApartment_EndResidency_ValidatesEndDate_AndSetsEnded()
    {
        var residentId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var startDate = new DateOnly(2026, 1, 1);
        var resApartment = new ResidentApartment(residentId, unitId, "TENANT", startDate, _now);

        var actor = Guid.NewGuid();
        var endTime = _now.AddDays(30);

        Assert.Throws<ArgumentException>(() => resApartment.EndResidency(new DateOnly(2025, 12, 1), actor, endTime));

        var validEndDate = new DateOnly(2026, 12, 31);
        resApartment.EndResidency(validEndDate, actor, endTime);
        Assert.Equal(ResidencyStatus.ENDED, resApartment.Status);
        Assert.Equal(validEndDate, resApartment.EndDate);
        Assert.Equal(endTime, resApartment.UpdatedAt);
        Assert.False(resApartment.IsActiveAt(new DateOnly(2027, 1, 1)));
    }
}
