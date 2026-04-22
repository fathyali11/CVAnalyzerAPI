using System.IO;
using Microsoft.AspNetCore.Http;

namespace CVAnalyzerAPI.Extensions;

public static class FileValidationExtensions
{
    private static readonly byte[] PdfSignature = { 0x25, 0x50, 0x44, 0x46 }; 
    private static readonly byte[] DocxSignature = { 0x50, 0x4B, 0x03, 0x04 };

    public static bool IsValidDocumentSignature(this IFormFile file)
    {
        if (file == null || file.Length == 0)
            return false;

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (ext == ".txt")
            return IsValidTextFile(file);

        if (file.Length < 4)
            return false;

        using var stream = file.OpenReadStream();
        var headerBytes = new byte[4];

        try
        {
            stream.ReadExactly(headerBytes, 0, 4);
        }
        catch (EndOfStreamException)
        {
            return false;
        }
        finally
        {
            stream.Position = 0;
        }

        if (ext == ".pdf")
            return headerBytes.SequenceEqual(PdfSignature);

        if (ext == ".docx")
            return headerBytes.SequenceEqual(DocxSignature);

        return false;
    }

    private static bool IsValidTextFile(IFormFile file)
    {
        using var stream = file.OpenReadStream();

        var buffer = new byte[512];
        var bytesToRead = (int)Math.Min(file.Length, buffer.Length);

        var bytesRead = stream.Read(buffer, 0, bytesToRead);
        stream.Position = 0;
        for (int i = 0; i < bytesRead; i++)
        {
            if (buffer[i] == 0x00)
            {
                return false;
            }
        }

        return true;
    }
}