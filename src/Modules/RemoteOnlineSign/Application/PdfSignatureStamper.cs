using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf.IO;
using PropFlow.Modules.RemoteOnlineSign.Domain;

namespace PropFlow.Modules.RemoteOnlineSign.Application;

public interface IPdfSignatureStamper
{
    Task<byte[]> StampAsync(Stream source, ContractSignatureField field, ContractSigningSession session, CancellationToken ct);
}

public sealed class PdfSignatureStamper : IPdfSignatureStamper
{
    private static readonly HashSet<string> Fonts = new(StringComparer.Ordinal) { "Arial", "Times New Roman", "Courier New" };

    public Task<byte[]> StampAsync(Stream source, ContractSignatureField field, ContractSigningSession session, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var document = PdfReader.Open(source, PdfDocumentOpenMode.Modify);
        if (field.Page < 1 || field.Page > document.PageCount) throw new RemoteContractException(400, "invalid_signature_field", "Trang ký không hợp lệ.");
        var page = document.Pages[field.Page - 1];
        if (field.X < 0 || field.Y < 0 || field.Width <= 0 || field.Height <= 0 || field.X + field.Width > (decimal)page.Width.Point || field.Y + field.Height > (decimal)page.Height.Point)
            throw new RemoteContractException(400, "invalid_signature_coordinates", "Vị trí ký nằm ngoài trang PDF.");
        using (var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
        {
            var box = new XRect((double)field.X, (double)field.Y, (double)field.Width, (double)field.Height);
            if (session.Method == SignatureMethod.TYPE && session.SignatureImage is null)
            {
                if (string.IsNullOrWhiteSpace(session.TypedSignatureText) || string.IsNullOrWhiteSpace(session.TypedSignatureFont) || !Fonts.Contains(session.TypedSignatureFont))
                    throw new RemoteContractException(400, "invalid_typed_signature", "Chữ ký gõ không hợp lệ.");
                graphics.DrawString(session.TypedSignatureText, new XFont(session.TypedSignatureFont, Math.Min((double)field.Height * .65, 28), XFontStyle.Italic), XBrushes.Black, box, XStringFormats.Center);
            }
            else
            {
                if (session.SignatureImage is null || session.SignatureImage.Length == 0) throw new RemoteContractException(400, "invalid_signature_image", "Ảnh chữ ký không hợp lệ.");
                using var image = XImage.FromStream(() => new MemoryStream(session.SignatureImage, writable: false));
                graphics.DrawImage(image, box);
            }
        }
        using var result = new MemoryStream();
        document.Save(result, false);
        return Task.FromResult(result.ToArray());
    }
}
