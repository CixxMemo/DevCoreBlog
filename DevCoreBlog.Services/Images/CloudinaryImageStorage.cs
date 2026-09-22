using CloudinaryDotNet;
using Microsoft.Extensions.Logging;

namespace DevCoreBlog.Services.Images;

public sealed class CloudinaryImageStorage : IImageStorage
{
    private readonly Cloudinary _cloudinary;
    private readonly CloudinaryImageUploadRequestFactory _requestFactory;
    private readonly ILogger<CloudinaryImageStorage> _logger;

    public CloudinaryImageStorage(
        CloudinaryImageUploadRequestFactory requestFactory,
        ILogger<CloudinaryImageStorage> logger)
    {
        var cloudName = Environment.GetEnvironmentVariable("CLOUDINARY_CLOUD_NAME");
        var apiKey = Environment.GetEnvironmentVariable("CLOUDINARY_API_KEY");
        var apiSecret = Environment.GetEnvironmentVariable("CLOUDINARY_API_SECRET");

        if (string.IsNullOrWhiteSpace(cloudName) ||
            string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(apiSecret))
        {
            throw new InvalidOperationException(
                "Cloudinary credentials are required for image storage.");
        }

        _requestFactory = requestFactory;
        _logger = logger;
        _cloudinary = new Cloudinary(new Account(cloudName, apiKey, apiSecret));
        _cloudinary.Api.Secure = true;
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
                uploadResult.Height);
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
