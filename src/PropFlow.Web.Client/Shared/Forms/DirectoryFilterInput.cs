using System.Globalization;

namespace PropFlow.Web.Client.Shared.Forms;

public static class DirectoryFilterInput
{
    public static bool TryFloor(string? text,out int? value)
    {
        value=null;if(string.IsNullOrWhiteSpace(text))return true;
        if(!int.TryParse(text.Trim(),NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var number)||number<0)return false;
        value=number;return true;
    }
    public static bool TryArea(string? text,out decimal? value)
    {
        value=null;if(string.IsNullOrWhiteSpace(text))return true;
        if(!decimal.TryParse(text.Trim().Replace(',','.'),NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var number)||number<0)return false;
        value=number;return true;
    }
    public static int? Floor(string? text)=>TryFloor(text,out var value)?value:null;
    public static decimal? Area(string? text)=>TryArea(text,out var value)?value:null;
    public static bool? Flag(string? text)=>bool.TryParse(text,out var value)?value:null;
}
