using DevCoreBlog.Services.Images;
using DevCoreBlog.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DevCoreBlog.Services;

public sealed class ImageService : IImageService
{
    private const string StorageFailureMessage =
        "Image storage is temporarily unavailable. Try again.";

    private readonly ImageUploadPolicy _policy;
    private readonly IImageStorage _storage;
    private readonly ILogger<ImageService> _logger;

    public ImageService(
        ImageUploadPolicy policy,
        IImageStorage storage,
        ILogger<ImageService> logger)
    {
        _policy = policy;
        _storage = storage;
        _logger = logger;
    }

    public async Task<ImageUploadOutcome> UploadImageAsync(
        IFormFile? file,
        CancellationToken cancellationToken = default)
    {
        var validation = await _policy.ValidateAsync(file, cancellationToken);
        if (!validation.IsValid || file is null || validation.ProviderFormat is null)
        {
            return ImageUploadOutcome.Failure(
                validation.FailureKind,
                validation.Message);
        }

        await using var stream = file.OpenReadStream();
        var storedImage = await _storage.UploadAsync(
            stream,
            validation.ProviderFormat,
            cancellationToken);

        if (!IsSafeStorageResult(storedImage))
        {
            _logger.LogWarning("Image storage returned an unsuccessful or invalid result.");
            return ImageUploadOutcome.Failure(
                ImageUploadFailureKind.StorageUnavailable,
                StorageFailureMessage);
        }

        return ImageUploadOutcome.Success(storedImage.Url!);
    }

    private bool IsSafeStorageResult(ImageStorageOutcome result)
    {
        return result.Succeeded &&
            Uri.TryCreate(result.Url, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            _policy.IsAllowedProviderFormat(result.Format) &&
            result.Width is > 0 and <= ImageUploadPolicy.MaximumDimension &&
            result.Height is > 0 and <= ImageUploadPolicy.MaximumDimension;
    }
}
