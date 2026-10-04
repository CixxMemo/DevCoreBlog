using CloudinaryDotNet;
using Microsoft.Extensions.Logging;

namespace DevCoreBlog.Services.Images;

/// <summary>Uploads validated image streams through the injected Cloudinary client.</summary>
public sealed class CloudinaryImageStorage : IImageStorage
{
    private readonly ICloudinaryUploadApi _cloudinary;
    private readonly CloudinaryImageUploadRequestFactory _requestFactory;
    private readonly ILogger<CloudinaryImageStorage> _logger;

    public CloudinaryImageStorage(
        ICloudinaryUploadApi cloudinary,
        CloudinaryImageUploadRequestFactory requestFactory,
        ILogger<CloudinaryImageStorage> logger)
    {
        _cloudinary = cloudinary;
        _requestFactory = requestFactory;
        _logger = logger;
    }

    public async Task<ImageStorageOutcome> UploadAsync(
        Stream stream,
        string providerFormat,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var uploadResult = await _cloudinary.UploadAsync(
                _requestFactory.Create(stream, providerFormat),
                cancellationToken);

            if (uploadResult.Error is not null || uploadResult.SecureUrl is null)
            {
                _logger.LogWarning("Cloudinary rejected an image upload.");
                return ImageStorageOutcome.Failure();
            }

            return ImageStorageOutcome.Success(
                uploadResult.SecureUrl.ToString(),
                uploadResult.Format,
                uploadResult.Width,
                uploadResult.Height,
                uploadResult.PublicId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Cloudinary image upload failed with exception type {ExceptionType}.",
                exception.GetType().Name);
            return ImageStorageOutcome.Failure();
        }
    }
}
