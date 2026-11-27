using System.Security.Cryptography;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using PropFlow.Modules.RemoteOnlineSign.Application;
using PropFlow.Modules.RemoteOnlineSign.Domain;

namespace PropFlow.UnitTests;

public sealed class RemoteOnlineSignPdfStamperTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScL7WQAAAABJRU5ErkJggg==");
    [Theory] [InlineData(SignatureMethod.DRAW)] [InlineData(SignatureMethod.UPLOAD)]
    public async Task Image_signature_produces_parseable_changed_pdf(SignatureMethod method) { var source = Pdf(1, 0); var output = await new PdfSignatureStamper().StampAsync(new MemoryStream(source), Field(), new ContractSigningSession { Method = method, SignatureImage = Png }, default); using var parsed = PdfReader.Open(new MemoryStream(output), PdfDocumentOpenMode.Import); Assert.Equal(1, parsed.PageCount); Assert.NotEqual(Convert.ToHexString(SHA256.HashData(source)), Convert.ToHexString(SHA256.HashData(output))); }
    [Theory] [InlineData(0)] [InlineData(90)] [InlineData(180)] [InlineData(270)]
    public async Task Rotation_preserves_page_geometry_and_stamps_target_page(int rotation) { var source = Pdf(2, rotation); var output = await new PdfSignatureStamper().StampAsync(new MemoryStream(source), Field(page: 2), new ContractSigningSession { Method = SignatureMethod.TYPE, TypedSignatureText = "Signer", TypedSignatureFont = "Arial" }, default); using var parsed = PdfReader.Open(new MemoryStream(output), PdfDocumentOpenMode.Import); Assert.Equal(rotation, parsed.Pages[1].Rotate); Assert.Equal(2, parsed.PageCount); }
    [Fact] public async Task Invalid_field_and_invalid_type_font_are_rejected() { var stamper = new PdfSignatureStamper(); await Assert.ThrowsAsync<RemoteContractException>(() => stamper.StampAsync(new MemoryStream(Pdf(1,0)), Field(x: 700), new ContractSigningSession { Method=SignatureMethod.TYPE,TypedSignatureText="x",TypedSignatureFont="Arial"}, default)); await Assert.ThrowsAsync<RemoteContractException>(() => stamper.StampAsync(new MemoryStream(Pdf(1,0)), Field(), new ContractSigningSession { Method=SignatureMethod.TYPE,TypedSignatureText="x",TypedSignatureFont="Bad"}, default)); }
    [Fact] public async Task Original_stream_bytes_are_not_overwritten() { var source = Pdf(1,0); var before = SHA256.HashData(source); _ = await new PdfSignatureStamper().StampAsync(new MemoryStream(source), Field(), new ContractSigningSession { Method=SignatureMethod.TYPE,TypedSignatureText="Signer",TypedSignatureFont="Arial"}, default); Assert.Equal(before, SHA256.HashData(source)); }
    private static ContractSignatureField Field(int page=1,decimal x=10)=>new(){Page=page,X=x,Y=10,Width=100,Height=30};
    private static byte[] Pdf(int pages,int rotation) { using var d=new PdfDocument(); for(var i=0;i<pages;i++){var p=d.AddPage();p.Rotate=rotation;} using var s=new MemoryStream();d.Save(s,false);return s.ToArray(); }
}
