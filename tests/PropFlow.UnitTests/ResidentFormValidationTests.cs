using System.ComponentModel.DataAnnotations;
using PropFlow.Web.Client.Features.Resident.Models;

namespace PropFlow.UnitTests;

public sealed class ResidentFormValidationTests
{
    [Fact]
    public void Changing_identity_type_revalidates_the_existing_number()
    {
        var form = ValidForm("CMND", "123456789");
        Assert.DoesNotContain(Validate(form), x => x.MemberNames.Contains(nameof(form.IdentityNumber)));

        form.IdentityType = "CCCD";

        var error = Assert.Single(Validate(form), x => x.MemberNames.Contains(nameof(form.IdentityNumber)));
        Assert.Equal("Số CCCD phải gồm đúng 12 chữ số.", error.ErrorMessage);
    }

    [Theory]
    [InlineData("CCCD", "064204010555", true)]
    [InlineData("CCCD", "06420401055", false)]
    [InlineData("CMND", "123456789", true)]
    [InlineData("CMND", "123456789012", true)]
    [InlineData("CMND", "1234567890", false)]
    public void Identity_formats_match_the_business_rules(string type, string number, bool valid)
    {
        var errors = Validate(ValidForm(type, number));
        Assert.Equal(valid, !errors.Any(x => x.MemberNames.Contains(nameof(CreateResidentRequest.IdentityNumber))));
    }

    private static CreateResidentRequest ValidForm(string type, string number) => new()
    {
        FullName = "Trần Hồng Quân",
        DateOfBirth = new DateOnly(2004, 2, 2),
        Nationality = "Việt Nam",
        IdentityType = type,
        IdentityNumber = number,
        PhoneNumber = "0363602027",
        ApartmentUnitId = Guid.NewGuid(),
        RelationshipKind = "OWNER_ONLY"
    };

    private static IReadOnlyList<ValidationResult> Validate(CreateResidentRequest form)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(form, new ValidationContext(form), results, true);
        return results;
    }
}
