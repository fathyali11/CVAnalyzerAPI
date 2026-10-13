using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using CVAnalyzerAPI.Consts;
using Microsoft.Extensions.Options;

namespace CVAnalyzerAPI.Services.FileServices;

public class CloudinaryService : IFileService
{
    private readonly Cloudinary _cloudinary;
    private readonly ILogger<CloudinaryService> _logger;

    public CloudinaryService(IOptions<CloudinarySettings> cloudinarySettings, ILogger<CloudinaryService> logger)
    {
        _logger = logger;

        var account = new Account(
            cloudinarySettings.Value.CloudName,
            cloudinarySettings.Value.ApiKey,
            cloudinarySettings.Value.ApiSecret
        );

        _cloudinary = new Cloudinary(account) { Api = { Secure = true } };
    }

    public async Task<(string url,string publicId)> UploadFileAsync(IFormFile file, string folder, CancellationToken cancellationToken = default)
    {
        await using var stream = file.OpenReadStream();

        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(file.FileName, stream),
            Folder = folder,
            UseFilename = true,
            UniqueFilename = true,
            Overwrite = false
        };

        var result = await _cloudinary.UploadAsync(uploadParams, cancellationToken);

        if (result.Error != null)
        {
            _logger.LogError("Cloudinary upload failed: {Error}", result.Error.Message);
            throw new Exception($"Image upload failed: {result.Error.Message}");
        }

        _logger.LogInformation("Image uploaded successfully to Cloudinary");

        return (result.SecureUrl.ToString(), result.PublicId);
    }

    public async Task<bool> DeleteFileAsync(string publicId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(publicId))
            return false;

        var deleteParams = new DeletionParams(publicId);
        var result = await _cloudinary.DestroyAsync(deleteParams);

        if (result.Result == "ok")
        {
            _logger.LogInformation("Image deleted from Cloudinary");
            return true;
        }

        _logger.LogWarning("Failed to delete image from Cloudinary");
        return false;
    }
}
