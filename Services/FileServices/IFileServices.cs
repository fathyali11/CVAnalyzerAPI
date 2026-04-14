namespace CVAnalyzerAPI.Services.FileServices;

public interface IFileService
{
    Task<(string url, string publicId)> UploadFileAsync(IFormFile file, string folder, CancellationToken cancellationToken = default);
    Task<bool> DeleteFileAsync(string publicId, CancellationToken cancellationToken = default);
}
