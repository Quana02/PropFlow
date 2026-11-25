using System.Text.Json;
using PropFlow.Web.Client.Shared.Forms;
using PropFlow.Web.Client.Features.Apartment.Models;

namespace PropFlow.UnitTests;

public sealed class DirectoryFilterInputTests
{
    [Theory]
    [InlineData(null,true,null)] [InlineData("",true,null)] [InlineData(" 2 ",true,2)] [InlineData("0",true,0)]
    [InlineData("-1",false,null)] [InlineData("1.5",false,null)] [InlineData("abc",false,null)] [InlineData("999999999999999",false,null)]
    public void Floor_is_optional_but_never_silently_accepts_invalid_text(string? text,bool valid,int? expected)
    {
        Assert.Equal(valid,DirectoryFilterInput.TryFloor(text,out var value));Assert.Equal(expected,value);
    }
    [Theory]
    [InlineData(null,true,null)] [InlineData("",true,null)] [InlineData("75.5",true,"75.5")] [InlineData("75,5",true,"75.5")]
    [InlineData("0",true,"0")] [InlineData("-1",false,null)] [InlineData("NaN",false,null)] [InlineData("1.000,00",false,null)]
    public void Area_accepts_decimal_comma_or_point_without_thousands_or_non_numbers(string? text,bool valid,string? expected)
    {
        Assert.Equal(valid,DirectoryFilterInput.TryArea(text,out var value));
        Assert.Equal(expected is null?(decimal?)null:decimal.Parse(expected,System.Globalization.CultureInfo.InvariantCulture),value);
    }
    [Fact]
    public void Older_api_missing_resident_names_is_unknown_not_a_vacant_apartment()
    {
        var item=JsonSerializer.Deserialize<ApartmentListItem>("""{"id":"00000000-0000-0000-0000-000000000001","unitNumber":"QA-01","floorNumber":1,"apartmentUnitTypeId":"00000000-0000-0000-0000-000000000002","unitType":"Test","usableAreaM2":25,"status":"ACTIVE","owners":[],"isOccupied":true}""",new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Null(item.CurrentResidents);Assert.True(ApartmentResidencyPresentation.RequiresRefresh(item));Assert.NotEqual("Căn hộ trống",ApartmentResidencyPresentation.EmptyLabel(item));
        Assert.Equal("Căn hộ trống",ApartmentResidencyPresentation.EmptyLabel(item with {IsOccupied=false,CurrentResidents=[]}));
        Assert.False(ApartmentResidencyPresentation.RequiresRefresh(item with {IsOccupied=false,CurrentResidents=[]}));
    }
}
