using PdfSharpCore.Fonts;

namespace PropFlow.Modules.RemoteOnlineSign.Application;

// The slim production image has no system fonts. This SIL-licensed font is also
// shipped by the signature editor, and keeps typed signatures portable on Linux.
internal sealed class RemoteSignatureFontResolver : IFontResolver
{
    private const string Face = "PropFlowSignatureFallback";
    private readonly PdfSharpCore.Utils.FontResolver system = new();
    public string DefaultFontName => "Allura";
    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        try { return system.ResolveTypeface(familyName,isBold,isItalic); }
        catch (FileNotFoundException) { return new FontResolverInfo(Face); }
    }
    public byte[] GetFont(string faceName)
    {
        if(faceName!=Face)return system.GetFont(faceName);
        using var source=typeof(RemoteSignatureFontResolver).Assembly.GetManifestResourceStream("PropFlow.Modules.RemoteOnlineSign.Fonts.Allura.ttf")
            ?? throw new InvalidOperationException("Missing embedded signature font.");
        using var output=new MemoryStream();source.CopyTo(output);return output.ToArray();
    }
}
