namespace CVAnalyzerAPI.Extensions;

public static class FileValidationExtensions
{
    public static bool IsValidPdfSignature(this IFormFile file)
    {
        if (file == null || file.Length < 4) return false;

        var pdfSignature = new byte[] { 0x25, 0x50, 0x44, 0x46 };

        using var stream = file.OpenReadStream();
        var headerBytes = new byte[4];

        stream.ReadExactly(headerBytes, 0, 4);

        stream.Position = 0;

        return headerBytes.SequenceEqual(pdfSignature);
    }
}
